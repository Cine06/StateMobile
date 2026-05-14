using Microsoft.AspNetCore.SignalR;
using StateMobile.API.Models;
using StateMobile.API.Services;
using System.Collections.Concurrent;

namespace StateMobile.API.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IDatabaseService _db;
        private static readonly ConcurrentDictionary<string, string> _onlineUsers = new();

        public ChatHub(IDatabaseService db)
        {
            _db = db;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers[userId] = Context.ConnectionId;
                await _db.ExecuteAsync("UPDATE UserStatus SET IsOnline = 1, LastSeen = GETDATE(), ConnectionID = @ConnId WHERE UserID = @UserId", 
                    new { ConnId = Context.ConnectionId, UserId = userId });
                
                await Clients.All.SendAsync("UserStatusChanged", userId, true);
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = _onlineUsers.FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers.TryRemove(userId, out _);
                await _db.ExecuteAsync("UPDATE UserStatus SET IsOnline = 0, LastSeen = GETDATE(), ConnectionID = NULL WHERE UserID = @UserId", 
                    new { UserId = userId });
                
                await Clients.All.SendAsync("UserStatusChanged", userId, false);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinRoom(string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        }

        public async Task LeaveRoom(string roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
        }

        public async Task SendMessage(ChatMessageModel message)
        {
            // Broadcast to the room
            await Clients.Group(message.RoomID.ToString()).SendAsync("ReceiveMessage", message);
        }

        public async Task DeleteMessage(long messageId, string roomId, bool forEveryone)
        {
            if (forEveryone)
            {
                await Clients.Group(roomId).SendAsync("MessageDeleted", messageId, true);
            }
            else
            {
                // Only notify the sender (handled client-side usually, but good for sync)
                await Clients.Caller.SendAsync("MessageDeleted", messageId, false);
            }
        }

        public async Task Typing(string roomId, string userId, bool isTyping)
        {
            await Clients.OthersInGroup(roomId).SendAsync("UserTyping", userId, isTyping);
        }
    }
}