namespace StateMobile.API.Models;

public class User
{
    public string UserID { get; set; } = string.Empty;
    public string AISNo { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string Photo { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string FullName => string.Join(' ', (new[] { FirstName, LastName }).Where(s => !string.IsNullOrEmpty(s)));
}