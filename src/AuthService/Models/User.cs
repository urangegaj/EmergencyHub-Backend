using Shared.Enums;

namespace AuthService.Models;

public class User
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public Guid RoleId { get; set; }
    public DepartmentType? Department { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public City City { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public UserProfile? Profile { get; set; }
}
