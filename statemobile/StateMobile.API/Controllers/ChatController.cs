using Microsoft.AspNetCore.Mvc;
using StateMobile.API.Models;
using StateMobile.API.Services;
using Microsoft.AspNetCore.SignalR;
using StateMobile.API.Hubs;

namespace StateMobile.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly IHubContext<ChatHub> _hubContext;

        public ChatController(IDatabaseService db, IHubContext<ChatHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
        }

        [HttpGet("rooms/{userId}")]
        public async Task<IActionResult> GetUserRooms(string userId)
        {
            var rooms = await _db.QueryAsync<ChatRoomModel>("EXEC spChat_GetUserRooms @UserID", new { UserID = userId });
            return Ok(rooms);
        }

        [HttpGet("messages/{roomId}/{userId}")]
        public async Task<IActionResult> GetMessages(Guid roomId, string userId)
        {
            var messages = await _db.QueryAsync<ChatMessageModel>("EXEC spChat_GetMessages @RoomID, @UserID", 
                new { RoomID = roomId, UserID = userId });
            return Ok(messages);
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatMessageModel message)
        {
            // Insert using the view/trigger provided in schema
            var sql = "INSERT INTO Messages (RoomID, SenderID, MessageText, Timestamp, IsRead) VALUES (@RoomID, @SenderID, @MessageText, GETDATE(), 0); SELECT SCOPE_IDENTITY();";
            var messageId = await _db.ExecuteScalarAsync<long>(sql, new 
            { 
                RoomID = message.RoomID, 
                SenderID = message.SenderID, 
                MessageText = message.MessageText 
            });

            message.MessageID = messageId;
            message.Timestamp = DateTime.Now;

            // Notify via SignalR
            await _hubContext.Clients.Group(message.RoomID.ToString()).SendAsync("ReceiveMessage", message);

            return Ok(message);
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteMessage([FromBody] DeleteMessageRequest request)
        {
            if (request.ForEveryone)
            {
                // Delete for everyone (actually just remove from main table or mark as deleted)
                // Based on schema, we might need a 'IsDeleted' column in Messages_Data, 
                // but since it's not there, we'll use MessageDeletions for all users or a specific flag.
                // For now, let's assume 'Delete for everyone' removes it from Messages_Data.
                await _db.ExecuteAsync("DELETE FROM Messages_Data WHERE MessageID = @MsgId", new { MsgId = request.MessageID });
            }
            else
            {
                // Delete for me
                await _db.ExecuteAsync("INSERT INTO MessageDeletions (MessageID, UserID, DeletedAt) VALUES (@MsgId, @UserId, GETDATE())", 
                    new { MsgId = request.MessageID, UserId = request.UserID });
            }

            await _hubContext.Clients.Group(request.RoomID.ToString()).SendAsync("MessageDeleted", request.MessageID, request.ForEveryone);
            return Ok();
        }

        [HttpPost("find-room")]
        public async Task<IActionResult> FindOrCreateRoom([FromBody] FindRoomRequest request)
        {
            var room = await _db.QueryFirstOrDefaultAsync<ChatRoomModel>("EXEC spChat_FindOneOnOneRoom @CurrentUserID, @TargetUserID", 
                new { CurrentUserID = request.CurrentUserID, TargetUserID = request.TargetUserID });

            if (room == null)
            {
                // Create new room logic
                var roomId = Guid.NewGuid();
                await _db.ExecuteAsync("INSERT INTO ChatRooms (RoomID, IsGroup, CreatedAt) VALUES (@Id, 0, GETDATE())", new { Id = roomId });
                await _db.ExecuteAsync("INSERT INTO ChatParticipants (RoomID, UserID) VALUES (@Rid, @U1), (@Rid, @U2)", 
                    new { Rid = roomId, U1 = request.CurrentUserID, U2 = request.TargetUserID });
                
                room = new ChatRoomModel { RoomID = roomId, IsGroup = false };
            }

            return Ok(room);
        }
    }

    public class DeleteMessageRequest
    {
        public long MessageID { get; set; }
        public string UserID { get; set; }
        public Guid RoomID { get; set; }
        public bool ForEveryone { get; set; }
    }

    public class FindRoomRequest
    {
        public string CurrentUserID { get; set; }
        public string TargetUserID { get; set; }
    }
}