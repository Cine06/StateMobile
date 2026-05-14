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
            try
            {
                // 1. Insert Message Text (Encrypted)
                var msgSql = @"
                    INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead, IsDeleted) 
                    VALUES (@RoomID, @SenderID, ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0, 0); 
                    SELECT SCOPE_IDENTITY();";

                var messageId = await _db.ExecuteScalarAsync<long>(msgSql, new 
                { 
                    RoomID = message.RoomID, 
                    SenderID = message.SenderID, 
                    Text = message.MessageText ?? "" 
                });

                // 2. Insert Attachment (Raw Binary - avoids encryption limits)
                if (message.Attachment != null && message.Attachment.Data != null)
                {
                    var attachSql = @"
                        INSERT INTO MessageAttachments (MessageID, FileName, FileType, AttachmentData, CreatedAt) 
                        VALUES (@MsgId, @Name, @Type, @Data, GETDATE())";
                    
                    await _db.ExecuteAsync(attachSql, new 
                    { 
                        MsgId = messageId, 
                        Name = message.Attachment.FileName, 
                        Type = message.Attachment.FileType, 
                        Data = message.Attachment.Data 
                    });
                }

                message.MessageID = messageId;
                message.Timestamp = DateTime.Now;

                // Notify others in real-time
                await _hubContext.Clients.Group(message.RoomID.ToString()).SendAsync("ReceiveMessage", message);

                return Ok(message);
            }
            catch (Exception ex)
            {
                // Log the actual error for debugging
                Console.WriteLine($"DB Error: {ex.Message}");
                return BadRequest(new { error = "Failed to save message to database", details = ex.Message });
            }
        }
    }
}