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

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatMessageModel message)
        {
            var sql = "INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead, IsDeleted) " +
                      "VALUES (@RoomID, @SenderID, ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0, 0); " +
                      "SELECT SCOPE_IDENTITY();";
            
            var messageId = await _db.ExecuteScalarAsync<long>(sql, new 
            { 
                RoomID = message.RoomID, 
                SenderID = message.SenderID, 
                Text = message.MessageText 
            });

            if (message.Attachment != null)
            {
                await _db.ExecuteAsync("INSERT INTO MessageAttachments (MessageID, FileName, FileType, AttachmentData) " +
                                     "VALUES (@MsgId, @Name, @Type, @Data)", new 
                { 
                    MsgId = messageId, 
                    Name = message.Attachment.FileName, 
                    Type = message.Attachment.FileType, 
                    Data = message.Attachment.Data 
                });
            }

            message.MessageID = messageId;
            await _hubContext.Clients.Group(message.RoomID.ToString()).SendAsync("ReceiveMessage", message);
            return Ok(message);
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteMessage([FromBody] DeleteMessageRequest request)
        {
            if (request.ForEveryone)
            {
                await _db.ExecuteAsync("UPDATE Messages_Data SET IsDeleted = 1, EncryptedText = NULL WHERE MessageID = @MsgId", 
                    new { MsgId = request.MessageID });
            }
            else
            {
                await _db.ExecuteAsync("INSERT INTO MessageDeletions (MessageID, UserID, DeletedAt) VALUES (@MsgId, @UserId, GETDATE())", 
                    new { MsgId = request.MessageID, UserId = request.UserID });
            }

            await _hubContext.Clients.Group(request.RoomID.ToString()).SendAsync("MessageDeleted", request.MessageID, request.ForEveryone);
            return Ok();
        }
    }
}