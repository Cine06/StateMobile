using StateMobile.API.Models;

namespace StateMobile.API.Services;

public interface IAuthenticationService
{
    Task<User?> AuthenticateUserAsync(string username, string password);
}