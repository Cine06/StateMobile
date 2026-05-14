using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.IIS;
using StateMobile.API.Hubs;
using StateMobile.API.Services;
using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// Ensure AppVersion configuration reloads automatically when appsettings.json changes
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Configuration for SignalR
var signalRSettings = builder.Configuration.GetSection("SignalR");
string hubUrl = signalRSettings.GetValue<string>("HubUrl")!;

// ✅ Configure Kestrel to stay alive indefinitely
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
    options.Limits.MaxRequestBodySize = 128 * 1024 * 1024; // 128MB max body (for file attachments)
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 128 * 1024 * 1024;
});

// ✅ Prevent host from shutting down on its own
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30); // graceful shutdown only when manually stopped
});

// ✅ Add SignalR with longer timeouts to prevent idle disconnections
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);      // Ping clients every 30s
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);   // Only drop client after 2min of no response
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);       // Allow 30s for initial handshake
    options.MaximumReceiveMessageSize = 128 * 1024 * 1024;      // 128MB max message (for large file/image attachments)
});

// Fix CORS - Use correct IP address from ipconfig
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(
                "http://192.168.1.193:5103",  // Your correct IP
                "http://localhost:5103",
                "http://10.0.2.2:5103")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultBufferSize = 16 * 1024;
    });

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.DefaultBufferSize = 16 * 1024;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Register database service
builder.Services.AddScoped<IDatabaseService, DatabaseService>();

// Register authentication service
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, UserContext>();

Console.WriteLine("✅ IAuthenticationService and IUserContext registered successfully");

// Register background polling service
builder.Services.AddHostedService<DatabaseSubscriptionService>();

// Configure connection strings
builder.Services.AddScoped<IDbConnection>(sp =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// ✅ Auto-migrate: Ensure Messages table exists with NVARCHAR(MAX) MessageText
try
{
    var chatConnStr = builder.Configuration.GetConnectionString("Chat");
    if (!string.IsNullOrEmpty(chatConnStr))
    {
        using var migrationConn = new SqlConnection(chatConnStr);
        await migrationConn.OpenAsync();

        // Step 1: Create Messages_Data table if it doesn't exist
        using var createCmd = new SqlCommand("spDatabase_Migrate", migrationConn);
        createCmd.CommandType = CommandType.StoredProcedure;

        var result = await createCmd.ExecuteScalarAsync();
        Console.WriteLine($"📦 Messages table migration: {result}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ DB migration warning (non-fatal): {ex.Message}");
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAll");
app.UseAuthorization();

// ✅ Global exception handler middleware
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ [GLOBAL] Unhandled exception: {ex.GetType().Name} - {ex.Message}");
        Console.WriteLine($"❌ [GLOBAL] Stack trace: {ex.StackTrace}");
        
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            
            var response = new
            {
                error = ex.Message,
                type = ex.GetType().Name,
                innerError = ex.InnerException?.Message,
                stackTrace = ex.StackTrace
            };
            
            await context.Response.WriteAsJsonAsync(response);
        }
    }
});

// ✅ Configure MIME Types (e.g., allow .apk files)
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".apk"] = "application/vnd.android.package-archive";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

// Map controllers
app.MapControllers();

// Map SignalR hubs
app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<ChatHub>("/chatHub");

// Health check endpoint
app.MapGet("/health", (IWebHostEnvironment env, IConfiguration config) => {
    var latestVersion = config["AppVersion:LatestVersion"] ?? "1.0";
    var updatesFolder = Path.Combine(env.ContentRootPath, "wwwroot", "updates");
    
    // Find the actual latest APK file in the updates folder
    var latestApkFile = Directory.Exists(updatesFolder) 
        ? Directory.GetFiles(updatesFolder, "*.apk")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .FirstOrDefault()
        : null;

    var expectedApkName = $"StateMobile_v{latestVersion}.apk";
    var expectedApkPath = Path.Combine(updatesFolder, expectedApkName);
    
    return Results.Json(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        configuredVersion = latestVersion,
        expectedApkFile = expectedApkName,
        expectedApkExists = System.IO.File.Exists(expectedApkPath),
        
        // Auto-detected info
        actualLatestApkFound = latestApkFile?.Name ?? "No APK found",
        actualLatestApkPath = latestApkFile?.FullName,
        
        directories = new {
            contentRoot = env.ContentRootPath,
            webRoot = env.WebRootPath,
            updatesFolder = updatesFolder
        }
    });
});

// ✅ Server startup info
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  ✅ STATE MOBILE API SERVER RUNNING");
Console.WriteLine("  📡 Listening on: http://0.0.0.0:5103");
Console.WriteLine("  💬 ChatHub:         /chatHub");
Console.WriteLine("  🔔 NotificationHub: /notificationHub");
Console.WriteLine("  🏥 Health Check:    /health");
Console.WriteLine("  ⏹️  Press Ctrl+C to stop the server");
Console.WriteLine("═══════════════════════════════════════════════════");
app.MapGet("/", () => "State Mobile API is running");
    app.Run();
