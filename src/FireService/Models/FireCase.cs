namespace FireService.Models;

public class FireCase
{
    public Guid Id { get; set; }
    public Guid EmergencyId { get; set; }
    public Guid CityId { get; set; }
    public FireCaseStatus Status { get; set; }
    public Guid? AssignedUnitId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
