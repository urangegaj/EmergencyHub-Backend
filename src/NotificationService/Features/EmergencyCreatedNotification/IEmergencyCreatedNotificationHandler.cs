namespace NotificationService.Features.EmergencyCreatedNotification;

public interface IEmergencyCreatedNotificationHandler
{
    Task HandleMessageAsync(string? json, CancellationToken ct);
}
