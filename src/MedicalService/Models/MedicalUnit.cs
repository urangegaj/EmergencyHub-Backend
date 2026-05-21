namespace MedicalService.Models;

public class MedicalUnit
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public MedicalUnitStatus Status { get; set; }
}
