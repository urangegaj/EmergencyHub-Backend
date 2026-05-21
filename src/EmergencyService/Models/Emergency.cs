using Shared.Enums;

namespace EmergencyService.Models;

public class Emergency
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public Guid ReportedByUserId { get; set; }
    public Guid EmergencyTypeId { get; set; }
    public string Description { get; set; } = null!;
    public string Address { get; set; } = null!;
    public EmergencyStatus Status { get; set; } = EmergencyStatus.Reported;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public EmergencyType EmergencyType { get; set; } = null!;
    public ICollection<EmergencyStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<EmergencyAssignment> Assignments { get; set; } = [];
}
