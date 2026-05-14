using Microsoft.AspNetCore.Mvc;
using StateMobile.API.Models;
using StateMobile.API.Services;

namespace StateMobile.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public ChatController(IDatabaseService db) => _db = db;

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteMessage([FromBody] DeleteMessageRequest request)
        {
            if (request.ForEveryone)
            {
                // Delete from main table so no one sees it
                await _db.ExecuteAsync("DELETE FROM Messages_Data WHERE MessageID = @MsgId", new { MsgId = request.MessageID });
            }
            else
            {
                // Mark as deleted only for this user
                await _db.ExecuteAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM MessageDeletions WHERE MessageID = @MsgId AND UserID = @UserId)
                    INSERT INTO MessageDeletions (MessageID, UserID, DeletedAt) VALUES (@MsgId, @UserId, GETDATE())", 
                    new { MsgId = request.MessageID, UserId = request.UserID });
            }
            return Ok();
        }
    }
}