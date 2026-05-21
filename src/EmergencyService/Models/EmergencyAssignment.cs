using Shared.Enums;

namespace EmergencyService.Models;

public class EmergencyAssignment
{
    public Guid Id { get; set; }
    public Guid EmergencyId { get; set; }
    public DepartmentType DepartmentType { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public Emergency Emergency { get; set; } = null!;
}
