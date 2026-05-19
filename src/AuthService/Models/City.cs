namespace AuthService.Models;

public class City
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Region { get; set; } = null!;
    public string Country { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<User> Users { get; set; } = [];
}
