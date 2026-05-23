namespace NotificationService.Models;

public enum NotificationStatus
{
    PENDING = 0,
    SENT = 1,
    PERMANENTLY_FAILED = 2,
    IN_APP_ONLY = 3
}
