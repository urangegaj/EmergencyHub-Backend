using Microsoft.EntityFrameworkCore;
using NotificationService.Data;

namespace NotificationService.Services;

public static class NotificationIdempotency
{
    public static Task<bool> ExistsAsync(
        NotificationDbContext db,
        Guid emergencyId,
        string type,
        Guid userId,
        string? fromStatus,
        string? toStatus,
        CancellationToken ct)
        => db.Notifications.AnyAsync(
            n => n.EmergencyId == emergencyId
                 && n.Type == type
                 && n.UserId == userId
                 && n.FromStatus == fromStatus
                 && n.ToStatus == toStatus,
            ct);
}
