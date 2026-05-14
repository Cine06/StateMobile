using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Data;

namespace YourBackendAPI.Hubs
{
    public class ChatHub : Hub
    {
        private readonly string _connectionString = "Data Source=192.168.1.9\\VTP,1500;" +
                                                    "Initial Catalog=Chat;" +
                                                    "User ID=MISD-FMCP;" +
                                                    "Password=FMCP@State1;" +
                                                    "Trust Server Certificate=True;" +
                                                    "Encrypt=False;";

        // ✅ When user connects, mark as online
        public override async Task OnConnectedAsync()
        {
            var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
            
            if (!string.IsNullOrEmpty(userId))
            {
                // Update database
                await UpdateUserStatus(userId, true, Context.ConnectionId);
                
                // Broadcast to all clients
                await Clients.All.SendAsync("UserStatusChanged", userId, true);
                
                Console.WriteLine($"✅ User {userId} connected (ID: {Context.ConnectionId})");
            }
            
            await base.OnConnectedAsync();
        }

        // ✅ When user disconnects, mark as offline
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = await GetUserIdByConnectionId(Context.ConnectionId);
            
            if (!string.IsNullOrEmpty(userId))
            {
                await UpdateUserStatus(userId, false, null);
                await Clients.All.SendAsync("UserStatusChanged", userId, false);
                
                Console.WriteLine($"❌ User {userId} disconnected");
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        // Join a chat room
        public async Task JoinRoom(string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            Console.WriteLine($"✅ User {Context.ConnectionId} joined room {roomId}");
        }

        // Leave a chat room
        public async Task LeaveRoom(string roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
            Console.WriteLine($"❌ User {Context.ConnectionId} left room {roomId}");
        }

        // Send message to room
        public async Task SendMessageToRoom(string roomId, string senderId, string message)
        {
            await Clients.Group(roomId).SendAsync("ReceiveMessage", roomId, senderId, message);
            Console.WriteLine($"📨 Message sent to room {roomId} from {senderId}");
        }

        // ✅ Update user status in database
        private async Task UpdateUserStatus(string userId, bool isOnline, string? connectionId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand("spChat_UpdateUserStatus", connection);
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@UserID", userId);
                command.Parameters.AddWithValue("@IsOnline", isOnline);
                command.Parameters.AddWithValue("@ConnectionID", (object?)connectionId ?? DBNull.Value);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error updating user status: {ex.Message}");
            }
        }

        // ✅ Get UserID from ConnectionID
        private async Task<string?> GetUserIdByConnectionId(string connectionId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = "SELECT UserID FROM UserStatus WHERE ConnectionID = @ConnectionID";
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@ConnectionID", connectionId);

                var result = await command.ExecuteScalarAsync();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting UserID: {ex.Message}");
                return null;
            }
        }
    }
}