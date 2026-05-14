using Microsoft.AspNetCore.Mvc;
using StateMobile.API.Services;

namespace StateMobile.API.Controllers;

[ApiController]
[Route("[controller]")]
public class UsersController : ControllerBase
{
    private readonly IDatabaseService _dbService;

    public UsersController(IDatabaseService dbService)
    {
        _dbService = dbService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? term)
    {
        var users = await _dbService.SearchUsersAsync(term ?? "");
        return Ok(users);
    }
}