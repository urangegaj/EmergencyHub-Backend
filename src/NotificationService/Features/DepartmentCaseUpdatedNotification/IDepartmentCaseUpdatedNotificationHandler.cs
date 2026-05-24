namespace NotificationService.Features.DepartmentCaseUpdatedNotification;

public interface IDepartmentCaseUpdatedNotificationHandler
{
    Task HandleMessageAsync(string? json, CancellationToken ct);
}
