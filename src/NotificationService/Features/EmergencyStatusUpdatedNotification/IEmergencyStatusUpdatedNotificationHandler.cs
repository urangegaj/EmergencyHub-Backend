namespace NotificationService.Features.EmergencyStatusUpdatedNotification;

public interface IEmergencyStatusUpdatedNotificationHandler
{
    Task HandleMessageAsync(string? json, CancellationToken ct);
}
