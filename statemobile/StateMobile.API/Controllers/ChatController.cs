using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using StateMobile.API.Hubs;
using StateMobile.API.Models;
using StateMobile.API.Services;

namespace StateMobile.API.Controllers;

[ApiController]
[Route("[controller]")]
public class ChatController : ControllerBase
{
    private readonly IDatabaseService _dbService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IHubContext<NotificationHub> _notifHubContext;

    public ChatController(IDatabaseService dbService, IHubContext<ChatHub> hubContext, IHubContext<NotificationHub> notifHubContext)
    {
        _dbService = dbService;
        _hubContext = hubContext;
        _notifHubContext = notifHubContext;
    }

    [HttpGet("rooms/{userId}")]
    public async Task<IActionResult> GetChatRooms(string userId)
    {
        var rooms = await _dbService.GetUserChatRoomsAsync(userId);
        return Ok(rooms);
    }

    [HttpGet("rooms/{roomId}/info")]
    public async Task<IActionResult> GetRoomInfo(Guid roomId, [FromQuery] string? userId = null)
    {
        var room = await _dbService.GetChatRoomAsync(roomId, userId);
        return room != null ? Ok(room) : NotFound();
    }

    [HttpGet("messages/{roomId}")]
    public async Task<IActionResult> GetMessages(Guid roomId, [FromQuery] string? userId = null)
    {
        try
        {
            Console.WriteLine($"📨 [Controller] GetMessages called for room {roomId}, userId={userId}");
            
            // Direct call without timeout wrapper - let the service handle timeouts
            var messages = await _dbService.GetChatMessagesAsync(roomId, userId);
            
            // Always return something, never let exceptions bubble up
            if (messages == null)
                messages = new List<ChatMessageModel>();
            
            Console.WriteLine($"✅ [Controller] GetMessages returning {messages.Count} messages");
            return Ok(messages);
        }
        catch (OperationCanceledException ocEx)
        {
            Console.WriteLine($"❌ [Controller] GetMessages timeout: {ocEx.Message}");
            // Return gracefully instead of closing connection
            return Ok(new List<ChatMessageModel>());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [Controller] GetMessages error: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"❌ [Controller] Inner: {ex.InnerException?.Message}");
            Console.WriteLine($"❌ [Controller] Stack: {ex.StackTrace}");
            
            // Return empty list instead of error to prevent socket closure
            // The global exception handler will still log it
            return Ok(new List<ChatMessageModel>());
        }
    }

    [HttpGet("messages/{roomId}/page")]
    public async Task<IActionResult> GetMessagesPage(
        Guid roomId,
        [FromQuery] string? userId = null,
        [FromQuery] long? beforeMessageId = null,
        [FromQuery] DateTime? beforeTimestamp = null,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            if (pageSize <= 0) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

            Console.WriteLine($"📨 [Controller] GetMessagesPage room={roomId}, userId={userId}, beforeMessageId={beforeMessageId}, beforeTimestamp={beforeTimestamp}, pageSize={pageSize}");

            var page = await _dbService.GetChatMessagesPageAsync(roomId, userId, beforeMessageId, beforeTimestamp, pageSize);
            Console.WriteLine($"✅ [Controller] GetMessagesPage returning {page.Messages.Count} messages, hasMore={page.HasMoreOlderMessages}");
            return Ok(page);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [Controller] GetMessagesPage error: {ex.GetType().Name} - {ex.Message}");
            return Ok(new ChatMessagesPageResult());
        }
    }

    [HttpGet("messages/{messageId}/content")]
    public async Task<IActionResult> GetMessageContent(long messageId)
    {
        try
        {
            var content = await _dbService.GetChatMessageContentAsync(messageId);
            return content != null ? Ok(content) : NotFound();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [Controller] GetMessageContent error: {ex.GetType().Name} - {ex.Message}");
            return NotFound();
        }
    }

    [HttpPost("messages")]
    [RequestSizeLimit(128 * 1024 * 1024)] // Allow large PDF/base64 attachments
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        try
        {
            Console.WriteLine($"📤 Saving message to room {request.RoomId} (size: {request.MessageText?.Length ?? 0} chars)");
            var messageId = await _dbService.SendChatMessageAsync(request.RoomId, request.SenderId, request.MessageText);
            if (messageId > 0)
            {
                // 🔔 Push chat notification to participants via NotificationHub (for background/offline users)
                try
                {
                    Console.WriteLine($"🔔 Fetching participants for room {request.RoomId}...");
                    List<(string UserID, string AISNo)> participants = await _dbService.GetRoomParticipantAISNosAsync(request.RoomId);
                    int pCount = participants?.Count ?? 0;
                    Console.WriteLine($"🔔 Found {pCount} participants in room {request.RoomId}");

                    var senderName = await _dbService.GetUserFullNameAsync(request.SenderId);
                    Console.WriteLine($"🔔 Sender name: {senderName}");

                    // Build a short preview for the notification body
                    var preview = request.MessageText ?? "";
                    if (preview.StartsWith("[IMG:")) preview = "📷 Photo";
                    else if (preview.StartsWith("[FILE:")) preview = "📎 File";
                    else if (preview.StartsWith("[AUDIO:")) preview = "🎤 Voice Message";
                    else if (preview.Length > 100) preview = preview.Substring(0, 100) + "…";

                    var roomIdStr = request.RoomId.ToString();
                    int notifiedCount = 0;

                    foreach (var participant in participants)
                    {
                        if (participant.UserID == request.SenderId)
                        {
                            Console.WriteLine($"🔔 Skipping sender: {participant.UserID}");
                            continue;
                        }

                        var groupName = $"user_{participant.AISNo}";
                        Console.WriteLine($"🔔 Attempting to notify participant: {participant.UserID} (AISNo: {participant.AISNo}) via group {groupName}");
                        
                        try 
                        {
                            await _notifHubContext.Clients
                                .Group(groupName)
                                .SendAsync("ReceiveChatMessage", roomIdStr, senderName, preview);
                            notifiedCount++;
                        }
                        catch (Exception innerEx)
                        {
                            Console.WriteLine($"❌ Failed to send to group {groupName}: {innerEx.Message}");
                        }
                    }

                    Console.WriteLine($"🔔 Chat notification sent to {notifiedCount} participants in room {roomIdStr}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Chat push notification error: {ex.Message}");
                    Console.WriteLine($"⚠️ StackTrace: {ex.StackTrace}");
                }

                return Ok(new { messageId });
            }

            Console.WriteLine($"❌ SendMessage DB save failed for room {request.RoomId}");
            return BadRequest(new { error = "Failed to save message to database" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SendMessage exception: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPatch("messages/{roomId}/read")]
    public async Task<IActionResult> MarkMessagesAsRead(Guid roomId, [FromQuery] string userId)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserID required");
        var success = await _dbService.MarkChatMessagesAsReadAsync(roomId, userId);
        return success ? Ok() : BadRequest();
    }

    [HttpPost("rooms")]
    public async Task<IActionResult> GetOrCreateChatRoom([FromBody] CreateChatRoomRequest request)
    {
        if (request.IsGroup && request.TargetUserIds != null && request.TargetUserIds.Any())
        {
            var groupRoom = await _dbService.CreateGroupChatRoomAsync(request.CurrentUserId, request.TargetUserIds, request.TargetFullName);
            if (groupRoom == null) return BadRequest(new { error = "Failed to create group room in database." });
            return Ok(groupRoom);
        }

        var room = await _dbService.GetOrCreateChatRoomAsync(request.CurrentUserId, request.TargetUserId, request.TargetFullName);
        if (room == null)
        {
            Console.WriteLine($"❌ GetOrCreateChatRoom returned null for {request.CurrentUserId} -> {request.TargetUserId}");
            return BadRequest(new { error = "Failed to get or create chat room" });
        }
        return Ok(room);
    }

    [HttpPatch("rooms/{roomId}/delete")]
    public async Task<IActionResult> DeleteChatRoom(Guid roomId, [FromQuery] string userId)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserID required");
        var success = await _dbService.DeleteChatRoomAsync(roomId, userId);
        return success ? Ok() : NotFound();
    }

    [HttpPost("messages/{messageId}/reactions")]
    public async Task<IActionResult> AddReaction(long messageId)
    {
        var userId = HttpContext.Request.Query["userId"].ToString();
        var reactionType = HttpContext.Request.Query["reactionType"].ToString();
        
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserID required");
        if (string.IsNullOrEmpty(reactionType)) return BadRequest("ReactionType required");
        
        var success = await _dbService.AddReactionAsync(messageId, userId, reactionType);
        return success ? Ok() : BadRequest();
    }

    [HttpDelete("messages/{messageId}/reactions")]
    public async Task<IActionResult> RemoveReaction(long messageId)
    {
        var userId = HttpContext.Request.Query["userId"].ToString();
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserID required");

        var success = await _dbService.RemoveReactionAsync(messageId, userId);
        return success ? Ok() : BadRequest();
    }

    [HttpPost("messages/{messageId}/read")]
    public async Task<IActionResult> MarkAsRead(long messageId)
    {
        var userId = HttpContext.Request.Query["userId"].ToString();
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserID required");

        var success = await _dbService.MarkMessageAsReadAsync(messageId, userId);
        return success ? Ok() : BadRequest();
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(long messageId, [FromQuery] string userId, [FromQuery] bool forEveryone)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserID required");
        var result = await _dbService.DeleteMessageAsync(messageId, userId, forEveryone);
        
        if (result.success)
        {
            if (forEveryone && result.roomId != Guid.Empty)
            {
                // 📢 Notify all participants in the room via SignalR
                await _hubContext.Clients.Group(result.roomId.ToString()).SendAsync("MessageDeleted", result.roomId, messageId);
            }
            return Ok();
        }
        
        return BadRequest();
    }

    [HttpDelete("messages/bulk")]
    public async Task<IActionResult> DeleteMessages([FromBody] List<long> messageIds, [FromQuery] string userId, [FromQuery] bool forEveryone)
    {
        if (string.IsNullOrEmpty(userId)) return BadRequest("UserID required");
        if (messageIds == null || !messageIds.Any()) return Ok();

        var result = await _dbService.DeleteMessagesAsync(messageIds, userId, forEveryone);
        
        if (result.success)
        {
            if (forEveryone && result.roomId != Guid.Empty)
            {
                // 📢 Notify all participants for each deleted message
                foreach (var messageId in messageIds)
                {
                    await _hubContext.Clients.Group(result.roomId.ToString()).SendAsync("MessageDeleted", result.roomId, messageId);
                }
            }
            return Ok();
        }
        
        return BadRequest();
    }

    [HttpPatch("rooms/{roomId}")]
    public async Task<IActionResult> UpdateChatRoom(Guid roomId, [FromBody] UpdateChatRoomRequest request)
    {
        if (roomId != request.RoomID) return BadRequest("RoomID mismatch");
        var result = await _dbService.UpdateChatRoomAsync(roomId, request.RoomName, request.RoomPhoto, request.ActorID);
        
        if (result.success)
        {
            if (result.messageId > 0)
            {
                // Broadcast the system message in real-time
                await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ReceiveMessage", roomId.ToString(), result.messageId, "SYSTEM", result.systemText);
            }
            
            // Trigger UI refresh (for name/photo changes)
            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ChatUpdated", roomId);
            return Ok();
        }
        
        return BadRequest();
    }

    [HttpGet("rooms/{roomId}/participants")]
    public async Task<IActionResult> GetParticipants(Guid roomId)
    {
        var participants = await _dbService.GetChatParticipantsAsync(roomId);
        return Ok(participants);
    }


    [HttpPatch("rooms/{roomId}/participants/{userId}/role")]
    public async Task<IActionResult> UpdateParticipantRole(Guid roomId, string userId, [FromBody] UpdateParticipantRoleRequest request)
    {
        if (roomId != request.RoomID || userId != request.UserID) return BadRequest("ID mismatch");
        var result = await _dbService.UpdateParticipantRoleAsync(roomId, userId, request.IsAdmin, request.ActorID);
        if (result.success)
        {
            if (result.messageId > 0)
            {
                // Broadcast the system message in real-time
                await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ReceiveMessage", roomId.ToString(), result.messageId, "SYSTEM", result.systemText);
            }

            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ParticipantRoleUpdated", roomId, userId, request.IsAdmin);
            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ChatUpdated", roomId);
            return Ok();
        }
        return BadRequest();
    }

    [HttpDelete("rooms/{roomId}/participants/{userId}")]
    public async Task<IActionResult> RemoveParticipant(Guid roomId, string userId, [FromQuery] string? actorId = null)
    {
        var result = await _dbService.RemoveParticipantAsync(roomId, userId, actorId);
        if (!string.IsNullOrEmpty(result.systemText))
        {
            // Notify about participant removal
            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ParticipantRemoved", roomId, userId);
            
            // Broadcast the system message in real-time
            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ReceiveMessage", roomId.ToString(), result.messageId, "SYSTEM", result.systemText);
            
            // Trigger UI refresh
            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ChatUpdated", roomId);

            var updatedRoom = await _dbService.GetChatRoomAsync(roomId);
            if (updatedRoom != null)
            {
                await _hubContext.Clients.Group(roomId.ToString()).SendAsync("RoomUpdated", roomId, updatedRoom.RoomName, updatedRoom.RoomPhoto);
            }
        }
        return !string.IsNullOrEmpty(result.systemText) ? Ok() : BadRequest();
    }

    [HttpPost("rooms/{roomId}/participants")]
    public async Task<IActionResult> AddParticipants(Guid roomId, [FromBody] List<string> userIds, [FromQuery] string addedBy)
    {
        var results = await _dbService.AddParticipantsToRoomAsync(roomId, userIds, addedBy);
        if (results.Any())
        {
            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ParticipantsAdded", roomId, userIds);
            
            foreach (var res in results)
            {
                var systemText = $"{await _dbService.GetUserFullNameAsync(addedBy)} added {res.targetName} to the group";
                await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ReceiveMessage", roomId.ToString(), res.messageId, "SYSTEM", systemText);
            }

            await _hubContext.Clients.Group(roomId.ToString()).SendAsync("ChatUpdated", roomId);
        }
        return results.Any() ? Ok() : BadRequest();
    }
}