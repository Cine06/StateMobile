using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using StateMobile.API.Services;
using StateMobile.API.Models;
using StateMobile.API.Hubs;

namespace StateMobile.API.Controllers;

[ApiController]
[Route("[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly IDatabaseService _dbService;
    private readonly IHubContext<NotificationHub> _hubContext;

    public NotificationsController(IDatabaseService dbService, IHubContext<NotificationHub> hubContext)
    {
        _dbService = dbService;
        _hubContext = hubContext;
    }

    [HttpPost]
    public async Task<IActionResult> SendNotification([FromBody] NotificationModel notification)
    {
        var success = await _dbService.SendNotificationAsync(notification);
        if (success)
        {
            await _hubContext.Clients.Group($"user_{notification.AISNo}").SendAsync("ReceiveNotification", notification);
            return Ok(new { Success = true });
        }
        return BadRequest(new { Success = false, Message = "Failed to save notification" });
    }

    [HttpGet("{aisno}")]
    public async Task<IActionResult> GetNotifications(string aisno)
    {
        var notifications = await _dbService.GetNotificationsAsync(aisno);
        return Ok(notifications);
    }

    [HttpGet("{aisno}/unread-counts")]
    public async Task<IActionResult> GetUnreadCounts(string aisno)
    {
        var counts = await _dbService.GetUnreadCountsAsync(aisno);
        return Ok(counts);
    }

    [HttpPatch("{code}/read")]
    public async Task<IActionResult> MarkAsRead(long code)
    {
        var success = await _dbService.MarkNotificationAsReadAsync(code);
        return success ? Ok() : NotFound();
    }

    [HttpDelete("{code}")]
    public async Task<IActionResult> DeleteNotification(long code)
    {
        var success = await _dbService.DeleteNotificationAsync(code);
        return success ? Ok() : NotFound();
    }

    [HttpPatch("{code}/archive")]
    public async Task<IActionResult> ArchiveNotification(long code)
    {
        var success = await _dbService.ArchiveNotificationAsync(code);
        return success ? Ok() : NotFound();
    }
}