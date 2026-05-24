namespace NotificationService.Features.EmergencyAssignedNotification;

public interface IEmergencyAssignedNotificationHandler
{
    Task HandleMessageAsync(string? json, CancellationToken ct);
}
