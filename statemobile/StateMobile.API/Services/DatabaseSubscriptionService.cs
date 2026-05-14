using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using StateMobile.API.Hubs;
using StateMobile.API.Models;
using Microsoft.Extensions.Hosting;
using System.Data;

namespace StateMobile.API.Services
{
    public class DatabaseSubscriptionService : BackgroundService
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IConfiguration _configuration;
        private int _lastMaxCode = 0;

        public DatabaseSubscriptionService(IHubContext<NotificationHub> hubContext, IConfiguration configuration)
        {
            _hubContext = hubContext;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine(">>>> Notification Background Polling Started <<<<");

            // Initial sync to find latest code
            _lastMaxCode = await GetMaxNotificationCode();
            Console.WriteLine($"[POLLING] Initial Max Code: {_lastMaxCode}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForNewNotifications(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Safe shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[POLLING ERROR] {DateTime.Now}: {ex.Message}");
                    Console.WriteLine($"🔍 StackTrace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"🔍 Inner Exception: {ex.InnerException.Message}");
                    }
                }

                try
                {
                    await Task.Delay(5000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> GetMaxNotificationCode()
        {
            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("Notification"));
                await connection.OpenAsync();
                using var command = new SqlCommand("spNotification_GetMaxCode", connection);
                command.CommandType = CommandType.StoredProcedure;
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            catch
            {
                return 0;
            }
        }

        private async Task CheckForNewNotifications(CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_configuration.GetConnectionString("Notification"));
            await connection.OpenAsync(stoppingToken);

            using var command = new SqlCommand("spNotification_GetNewNotifications", connection);
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@LastCode", _lastMaxCode);

            using var reader = await command.ExecuteReaderAsync(stoppingToken);
            while (await reader.ReadAsync(stoppingToken))
            {
                int currentCode = Convert.ToInt32(reader["Code"]);
                string aisNo = reader["AISNo"]?.ToString()?.Trim() ?? "";

                var newNotif = new NotificationModel
                {
                    Code = currentCode,
                    AISNo = aisNo,
                    Message = reader["Message"]?.ToString()?.Trim() ?? "",
                    ModuleName = reader["ModuleName"]?.ToString()?.Trim() ?? "Notification",
                    ModuleCode = reader["ModuleCode"] != DBNull.Value ? Convert.ToInt32(reader["ModuleCode"]) : 0,
                    URL = reader["URL"]?.ToString()?.Trim() ?? "",
                    Date = reader["Date"] != DBNull.Value ? (DateTime)reader["Date"] : DateTime.Now,
                    Done = 0 // New records are always unread
                };

                _lastMaxCode = currentCode;

                // Push to specific user group
                await _hubContext.Clients.Group($"user_{aisNo}").SendAsync("ReceiveNotification", newNotif, cancellationToken: stoppingToken);
                Console.WriteLine($"[POLLING] New Record Sent: {newNotif.Message} (Code: {currentCode}, To: {aisNo})");
            }
        }
    }
}
