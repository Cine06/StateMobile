using Microsoft.Data.SqlClient;
using StateMobile.API.Function;
using StateMobile.API.Models;
using System.Data;

namespace StateMobile.API.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IConfiguration _configuration;

    public AuthenticationService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<User?> AuthenticateUserAsync(string username, string password)
    {
        
        try
        {
            // Use SQLActions to verify SQL Server credentials (async)
            using var sqlActions = new SQLActions();
            bool isAuthenticated = await sqlActions.ConnectedAsync(username, password);

            if (!isAuthenticated)
            {
                Console.WriteLine($"❌ SQL authentication failed for user: {username}");
                return null;
            }

            // Get user details from database
            return await GetUserDetailsAsync(username);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Authentication error: {ex.Message}");
            return null;
        }
    }

    private async Task<User?> GetUserDetailsAsync(string username)
    {
        try
        {
            var connString = _configuration.GetConnectionString("MISD") ?? _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connString))
            {
                Console.WriteLine("❌ MISD connection string is NULL or empty!");
                return CreateFallbackUser(username);
            }

            using var connection = new SqlConnection(connString);
            await connection.OpenAsync();

            User? user = await ExecuteGetDetailsAsync(connection, username);

            // If not found and username contains a hyphen (e.g., MISD-FMCP), try extracting just the ID part (e.g., FMCP)
            if (user == null && username.Contains('-'))
            {
                var parts = username.Split('-');
                if (parts.Length > 1)
                {
                    string shortUsername = parts[parts.Length - 1]; // Take the last part
                    Console.WriteLine($"🔍 Details not found for '{username}', retrying with '{shortUsername}'...");
                    user = await ExecuteGetDetailsAsync(connection, shortUsername);
                }
            }

            if (user != null)
            {
                return user;
            }
            else
            {
                Console.WriteLine($"⚠️ No user details found in DB for: {username}");
                return CreateFallbackUser(username);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetUserDetails error for {username}: {ex.Message}");
            return CreateFallbackUser(username);
        }
    }

    private async Task<User?> ExecuteGetDetailsAsync(SqlConnection connection, string userId)
    {
        using var command = new SqlCommand("spUser_GetDetailsByUserID", connection);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserID", userId);

        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new User
            {
                UserID = reader["UserID"]?.ToString() ?? "",
                AISNo = reader["AISNo"]?.ToString() ?? "",
                FirstName = reader["FirstName"]?.ToString() ?? "",
                MiddleName = reader.HasColumn("MiddleName") ? reader["MiddleName"]?.ToString() ?? "" : "",
                LastName = reader["LastName"]?.ToString() ?? "",
                DepartmentName = reader["DepartmentName"]?.ToString() ?? "",
                Photo = reader["Photo"] != DBNull.Value ? (reader["Photo"] is byte[] bytePhoto ? Convert.ToBase64String(bytePhoto) : reader["Photo"]?.ToString() ?? "") : "",
                Nickname = reader["Nickname"] != DBNull.Value ? reader["Nickname"]?.ToString() ?? "" : "",
                Mobile = reader["Mobile"] != DBNull.Value ? reader["Mobile"]?.ToString() ?? "" : ""
            };
        }
        return null;
    }

    private User CreateFallbackUser(string username)
    {
        Console.WriteLine($"⚠️ Creating fallback user for: {username}");

        var parts = username.Split('-');
        var firstName = parts.Length > 1 ? parts[1] : username;
        var department = parts.Length > 0 ? parts[0] : "Unknown";

        Console.WriteLine($"   Fallback FirstName: {firstName}");
        Console.WriteLine($"   Fallback Department: {department}");

        return new User
        {
            UserID = username,
            AISNo = "",
            FirstName = firstName,
            LastName = "",
            DepartmentName = department
        };
    }
}
