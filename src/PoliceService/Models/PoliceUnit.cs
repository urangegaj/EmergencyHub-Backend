namespace PoliceService.Models;

public class PoliceUnit
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PoliceUnitStatus Status { get; set; }
}
