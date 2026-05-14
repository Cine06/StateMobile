using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using StateMobile.API.Models;
using System.Data;

namespace StateMobile.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public NotificationController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("{aisno}")]
        public async Task<IActionResult> GetNotifications(string aisno)
        {
            var list = new List<NotificationModel>();
            try
            {
                var connectionString = _configuration.GetConnectionString("Notification");
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand("spNotification_GetList", connection);
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@aisno", aisno);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new NotificationModel
                    {
                        Code = reader["Code"] != DBNull.Value ? Convert.ToInt64(reader["Code"]) : 0,
                        Date = reader["Date"] != DBNull.Value ? Convert.ToDateTime(reader["Date"]) : DateTime.Now,
                        ModuleName = reader["ModuleName"]?.ToString() ?? "",
                        Message = reader["Message"]?.ToString() ?? "",
                        RequestBy = reader["Requestby"]?.ToString() ?? "",
                        ForApproval = reader["ForApproval"]?.ToString() ?? "",
                        WidgetName = reader["WidgetName"]?.ToString() ?? "",
                        ModuleCode = reader["ModuleCode"] != DBNull.Value ? Convert.ToInt32(reader["ModuleCode"]) : 0,
                        InternetURL = reader["InternetURL"]?.ToString() ?? "",
                        LocalURL = reader["LocalURL"]?.ToString() ?? "",
                        URL = reader["URL"]?.ToString() ?? "",
                        AISNo = reader["AISNo"]?.ToString() ?? "",
                        Done = reader["Done"] != DBNull.Value ? Convert.ToInt32(reader["Done"]) : 0,
                        DateRead = reader["DateRead"] != DBNull.Value ? Convert.ToDateTime(reader["DateRead"]) : (DateTime?)null
                    });
                }

                return Ok(new { Success = true, Data = list });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Success = false, Message = ex.Message });
            }
        }
    }
}