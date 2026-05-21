using Shared.Enums;

namespace EmergencyService.Models;

public class EmergencyStatusHistory
{
    public Guid Id { get; set; }
    public Guid EmergencyId { get; set; }
    public EmergencyStatus Status { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public Guid? ChangedByUserId { get; set; }

    public Emergency Emergency { get; set; } = null!;
}
