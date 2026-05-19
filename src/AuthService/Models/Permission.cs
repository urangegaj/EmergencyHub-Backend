namespace AuthService.Models;

public class Permission
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Resource { get; set; } = null!;
    public string Action { get; set; } = null!;

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
