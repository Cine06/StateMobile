using Microsoft.AspNetCore.SignalR;
using StateMobile.API.Services;
using System.Collections.Generic;
using System.Linq;

namespace StateMobile.API.Hubs;

public class ChatHub : Hub
{
    private readonly IDatabaseService _dbService;
    private readonly IHubContext<NotificationHub> _notifHubContext;

    public ChatHub(IDatabaseService dbService, IHubContext<NotificationHub> notifHubContext)
    {
        _dbService = dbService;
        _notifHubContext = notifHubContext;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
        if (!string.IsNullOrEmpty(userId))
        {
            await _dbService.UpdateUserStatusAsync(userId, true, Context.ConnectionId);
            await Clients.All.SendAsync("UserStatusChanged", userId, true);
            
            // Join all active rooms for this user automatically to ensure real-time updates everywhere
            try 
            {
                var rooms = await _dbService.GetUserChatRoomsAsync(userId);
                if (rooms != null)
                {
                    foreach (var room in rooms)
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomID.ToString());
                    }
                    Console.WriteLine($"✅ User {userId} auto-joined {rooms.Count} rooms on connection");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error auto-joining rooms for {userId}: {ex.Message}");
            }

            Console.WriteLine($"✅ User {userId} connected (ID: {Context.ConnectionId})");
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();
        if (!string.IsNullOrEmpty(userId))
        {
            await _dbService.UpdateUserStatusAsync(userId, false, null);
            await Clients.All.SendAsync("UserStatusChanged", userId, false);
            Console.WriteLine($"❌ User {userId} disconnected");
        }
        await base.OnDisconnectedAsync(exception);
    }
    // Join the user to a specific room group
    public async Task JoinRoom(string roomId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
    }

    // Join the user to multiple room groups at once
    public async Task JoinAllRooms(List<string> roomIds)
    {
        foreach (var roomId in roomIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        }
        Console.WriteLine($"✅ User joined {roomIds.Count} rooms");
    }

    // Leave the user from a specific room group
    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }

    // Broadcast message to everyone in the room (used by SendMessageToRoom)
    // ✅ Send message to room (excludes sender to avoid duplicates)
    public async Task SendMessageToRoom(string roomId, long messageId, string senderId, string message)
    {
        // 1. Send to active SignalR connections in the ChatHub room group
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveMessage", roomId, messageId, senderId, message);

        // 2. Push notification to participants via NotificationHub (for background/offline users)
        try
        {
            if (Guid.TryParse(roomId, out var roomGuid))
            {
                List<(string UserID, string AISNo)> participants = await _dbService.GetRoomParticipantAISNosAsync(roomGuid);
                var senderName = await _dbService.GetUserFullNameAsync(senderId);

                // Build a short preview for the notification body
                var preview = message ?? "";
                if (preview.StartsWith("[IMG:")) preview = "📷 Photo";
                else if (preview.StartsWith("[FILE:")) preview = "📎 File";
                else if (preview.Length > 100) preview = preview.Substring(0, 100) + "…";

                foreach (var participant in participants)
                {
                    if (participant.UserID == senderId) continue;

                    // Send via NotificationHub group
                    await _notifHubContext.Clients
                        .Group($"user_{participant.AISNo}")
                        .SendAsync("ReceiveChatMessage", roomId, senderName, preview);
                }
                
                int pCount = participants?.Count ?? 0;
                Console.WriteLine($"🔔 ChatHub: Push notification sent to {Math.Max(0, pCount - 1)} participants in room {roomId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ ChatHub: Push notification error: {ex.Message}");
        }
    }

    // ✅ Broadcast reaction to room
    public async Task SendReaction(string roomId, long messageId, string senderId, string reactionType)
    {
        await Clients.Group(roomId).SendAsync("MessageReactionReceived", roomId, messageId, senderId, reactionType);
    }

    // ✅ Broadcast reaction removal to room
    public async Task SendRemoveReaction(string roomId, long messageId, string userId)
    {
        await Clients.Group(roomId).SendAsync("MessageReactionRemoved", roomId, messageId, userId);
    }

    // ✅ Broadcast read receipt to room
    public async Task SendReadReceipt(string roomId, long messageId, string userId)
    {
        await Clients.Group(roomId).SendAsync("MessageReadReceived", messageId, userId);
    }

    // ✅ Broadcast room-level read receipt
    public async Task SendRoomReadReceipt(string roomId, string userId)
    {
        await Clients.OthersInGroup(roomId).SendAsync("RoomReadReceived", roomId, userId);
    }

    // ✅ Broadcast message deletion to room (excludes sender to avoid double-processing)
    public async Task NotifyMessageDeleted(string roomId, long messageId)
    {
        Console.WriteLine($"🗑️ NotifyMessageDeleted: room={roomId}, messageId={messageId}");
        await Clients.OthersInGroup(roomId).SendAsync("MessageDeleted", Guid.Parse(roomId), messageId);
    }
}