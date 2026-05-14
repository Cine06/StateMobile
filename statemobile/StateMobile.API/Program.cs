using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Increase Form and Body limits for large image uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = int.MaxValue;
    options.MemoryBufferThreshold = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 52428800; // 50MB
});

builder.Services.AddControllers();
builder.Services.AddSignalR();
// ... rest of your services ...

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.MapHub<StateMobile.API.Hubs.ChatHub>("/chatHub");
app.Run();