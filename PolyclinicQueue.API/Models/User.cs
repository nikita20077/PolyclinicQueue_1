namespace PolyclinicQueue.API.Models;

public class User
{
    public int Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;   // Admin / Doctor / Patient
    public DateTime CreatedAt { get; set; }
}
