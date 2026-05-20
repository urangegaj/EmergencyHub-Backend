namespace FireService.Models;

public class FireUnit
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FireUnitStatus Status { get; set; }
}
