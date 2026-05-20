namespace AuthService.Models;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;

    public ICollection<User> Users { get; set; } = [];
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
