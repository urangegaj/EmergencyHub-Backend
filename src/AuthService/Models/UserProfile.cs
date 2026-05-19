namespace AuthService.Models;

public class UserProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Phone { get; set; }

    public User User { get; set; } = null!;
}
