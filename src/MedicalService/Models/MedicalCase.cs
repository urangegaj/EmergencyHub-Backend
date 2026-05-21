namespace MedicalService.Models;

public class MedicalCase
{
    public Guid Id { get; set; }
    public Guid EmergencyId { get; set; }
    public Guid CityId { get; set; }
    public MedicalCaseStatus Status { get; set; }
    public Guid? AssignedUnitId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
