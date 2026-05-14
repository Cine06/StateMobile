using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using StateMobile.API.Models;
using StateMobile.API.Services;

namespace StateMobile.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IDatabaseService _dbService;

        public UserController(IConfiguration configuration, IDatabaseService dbService)
        {
            _configuration = configuration;
            _dbService = dbService;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers([FromQuery] string? searchTerm, [FromQuery] Guid? excludeRoomId = null)
        {
            try
            {
                var users = await _dbService.SearchUsersAsync(searchTerm ?? string.Empty, excludeRoomId);
                return Ok(new { Success = true, Data = users });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("update-photo")]
        public async Task<IActionResult> UpdateProfilePicture([FromBody] UpdatePhotoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.AISNo) || string.IsNullOrWhiteSpace(request.PhotoBase64))
            {
                return BadRequest(new { Success = false, Message = "AISNo and Photo are required" });
            }

            try
            {
                var connectionString = _configuration.GetConnectionString("MISD");
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand("spUser_UpdateProfilePicture", connection);
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@AISNo", request.AISNo);

                // Convert Base64 string back to byte array for VarBinary column
                byte[] photoBytes = Convert.FromBase64String(request.PhotoBase64);
                var photoParam = new SqlParameter("@Photo", System.Data.SqlDbType.VarBinary)
                {
                    Value = photoBytes
                };
                command.Parameters.Add(photoParam);

                await command.ExecuteNonQueryAsync();

                return Ok(new { Success = true, Message = "Profile picture updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Success = false, Message = ex.Message });
            }
        }
        [HttpPost("update-info")]
        public async Task<IActionResult> UpdateProfileInfo([FromBody] UpdateInfoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.AISNo))
            {
                return BadRequest(new { Success = false, Message = "AISNo is required" });
            }

            try
            {
                var connectionString = _configuration.GetConnectionString("MISD");
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand("spUser_UpdateProfileInfo", connection);
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@AISNo", request.AISNo);
                command.Parameters.AddWithValue("@Nickname", request.Nickname ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Mobile", request.Mobile ?? (object)DBNull.Value);

                using var reader = await command.ExecuteReaderAsync();
                int isSuccess = 0;
                DateTime? nextAvailableDate = null;

                if (await reader.ReadAsync())
                {
                    isSuccess = reader.GetInt32(0);
                    if (isSuccess == 0 && !reader.IsDBNull(1))
                    {
                        nextAvailableDate = reader.GetDateTime(1);
                    }
                }

                if (isSuccess == 0)
                {
                    string dateStr = nextAvailableDate.HasValue ? nextAvailableDate.Value.ToString("MMMM dd, yyyy") : "after 15 days from your last update";
                    return BadRequest(new { Success = false, Message = $"You can only update your profile information once every 15 days. You can next update on {dateStr}." });
                }

                return Ok(new { Success = true, Message = "Profile info updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Success = false, Message = ex.Message });
            }
        }
    }

    public class UpdatePhotoRequest
    {
        public string AISNo { get; set; } = string.Empty;
        public string PhotoBase64 { get; set; } = string.Empty;
    }

    public class UpdateInfoRequest
    {
        public string AISNo { get; set; } = string.Empty;
        public string? Nickname { get; set; }
        public string? Mobile { get; set; }
    }
}