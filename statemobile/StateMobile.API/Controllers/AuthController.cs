using Microsoft.AspNetCore.Mvc;
using StateMobile.API.Models;
using StateMobile.API.Services;
using System.Data;

namespace StateMobile.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IDatabaseService _dbService;
    private readonly IAuthenticationService _authService;

    private readonly IConfiguration _configuration;

    public AuthController(IDatabaseService dbService, IAuthenticationService authService, IConfiguration configuration)
    {
        _dbService = dbService;
        _authService = authService;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { Success = false, Message = "Username and password are required" });

        var user = await _authService.AuthenticateUserAsync(request.Username, request.Password);
        
        if (user != null)
        {
            return Ok(new { Success = true, User = user, Message = "Login successful" });
        }

        return Unauthorized(new { Success = false, Message = "Invalid credentials" });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || 
            string.IsNullOrWhiteSpace(request.OldPassword) || 
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { Success = false, Message = "Username, old password, and new password are required" });
        }

        // 1. Verify old password
        var user = await _authService.AuthenticateUserAsync(request.Username, request.OldPassword);
        if (user == null)
        {
            return Unauthorized(new { Success = false, Message = "Old password is incorrect" });
        }

        try
        {
            var connectionString = _configuration.GetConnectionString("MISD");
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            await connection.OpenAsync();

            /* Skip 15-day restriction as requested by user (missing column/SPs) */

            // 3. Change SQL Login Password
            // Note: Preventing SQL Injection by replacing ' with '' just in case. But ideally dynamic SQL for passwords requires care.
            string safePassword = request.NewPassword.Replace("'", "''");
            string safeUsername = request.Username.Replace("'", "''");
            string alterQuery = $"ALTER LOGIN [{safeUsername}] WITH PASSWORD = '{safePassword}'";
            using var alterCmd = new Microsoft.Data.SqlClient.SqlCommand(alterQuery, connection);
            await alterCmd.ExecuteNonQueryAsync();

            /* Skip updating last password update timestamp */

            return Ok(new { Success = true, Message = "Password updated successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Success = false, Message = "Error updating password.", Details = ex.Message });
        }
    }
}

public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string Username, string OldPassword, string NewPassword);
