namespace StateMobile.API.Models
{
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class UserResponse
    {
        public string UserID { get; set; } = string.Empty;
        public string AISNo { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string Photo { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
    }
}
