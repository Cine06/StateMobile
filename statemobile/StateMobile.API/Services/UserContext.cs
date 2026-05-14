using Microsoft.AspNetCore.Http;

namespace StateMobile.API.Services;

public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserID 
    {
        get 
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return null;

            if (context.Request.Headers.TryGetValue("X-User-ID", out var userId))
            {
                return userId.ToString();
            }
            if (context.Request.Query.TryGetValue("userId", out var queryUserId))
            {
                return queryUserId.ToString();
            }

            return context.User?.Identity?.Name;
        }
    }

    public string? AISNo
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return null;

            if (context.Request.Headers.TryGetValue("X-AIS-No", out var aisNo))
            {
                return aisNo.ToString();
            }

            if (context.Request.Query.TryGetValue("aisNo", out var queryAisNo))
            {
                return queryAisNo.ToString();
            }

            return null;
        }
    }
}
