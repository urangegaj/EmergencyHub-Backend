using System.Text.Json;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services.Email;

namespace NotificationService.Services;

public sealed class NotificationDispatchService(
    NotificationDbContext db,
    IEmailSender emailSender,
    ILogger<NotificationDispatchService> logger)
{
    public const int MaxEmailRetries = 5;

    public async Task SendEmailNotificationAsync(
        Guid userId,
        Guid cityId,
        Guid emergencyId,
        string notificationType,
        string to,
        string subject,
        string body,
        string? fromStatus,
        string? toStatus,
        CancellationToken ct)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CityId = cityId,
            Type = notificationType,
            EmergencyId = emergencyId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Status = NotificationStatus.PENDING,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        try
        {
            await emailSender.SendAsync(to, subject, body, ct);
            notification.Status = NotificationStatus.SENT;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            await EnqueueEmailRetryAsync(notification.Id, userId, cityId, emergencyId, notificationType, to, subject, body, ex.Message, ct);
            logger.LogWarning(ex,
                "Email send failed for user {UserId}, emergency {EmergencyId}; queued for retry",
                userId, emergencyId);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RetryEmailJobAsync(BackgroundJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EmailJobPayload>(job.Payload)
            ?? throw new InvalidOperationException("Invalid email job payload.");

        try
        {
            await emailSender.SendAsync(payload.To, payload.Subject, payload.Body, ct);

            job.Status = BackgroundJobStatus.COMPLETED;
            job.LastError = null;
            job.UpdatedAt = DateTime.UtcNow;

            var notification = await db.Notifications.FindAsync([payload.NotificationId], ct);
            if (notification is not null)
                notification.Status = NotificationStatus.SENT;
        }
        catch (Exception ex)
        {
            job.RetryCount++;
            job.LastError = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;

            if (job.RetryCount >= MaxEmailRetries)
            {
                job.Status = BackgroundJobStatus.PERMANENTLY_FAILED;
                job.NextRetryAt = null;

                var notification = await db.Notifications.FindAsync([payload.NotificationId], ct);
                if (notification is not null)
                    notification.Status = NotificationStatus.PERMANENTLY_FAILED;

                logger.LogError(ex,
                    "Email job {JobId} permanently failed after {Attempts} attempts",
                    job.Id, job.RetryCount);
            }
            else
            {
                job.Status = BackgroundJobStatus.PENDING;
                job.NextRetryAt = DateTime.UtcNow.Add(GetBackoff(job.RetryCount));
                logger.LogWarning(ex,
                    "Email job {JobId} failed attempt {Attempt}, next retry at {NextRetryAt}",
                    job.Id, job.RetryCount, job.NextRetryAt);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private Task EnqueueEmailRetryAsync(
        Guid notificationId,
        Guid userId,
        Guid cityId,
        Guid emergencyId,
        string notificationType,
        string to,
        string subject,
        string body,
        string error,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new EmailJobPayload
        {
            NotificationId = notificationId,
            UserId = userId,
            CityId = cityId,
            EmergencyId = emergencyId,
            NotificationType = notificationType,
            To = to,
            Subject = subject,
            Body = body
        });

        db.BackgroundJobs.Add(new BackgroundJob
        {
            Id = Guid.NewGuid(),
            Type = BackgroundJobTypes.EmailSend,
            Payload = payload,
            Status = BackgroundJobStatus.PENDING,
            RetryCount = 1,
            LastError = error,
            NextRetryAt = now.Add(GetBackoff(1)),
            CreatedAt = now,
            UpdatedAt = now
        });

        return Task.CompletedTask;
    }

    public static TimeSpan GetBackoff(int retryCount)
        => TimeSpan.FromSeconds(Math.Pow(2, retryCount) * 30);

    public sealed class EmailJobPayload
    {
        public Guid NotificationId { get; set; }
        public Guid UserId { get; set; }
        public Guid CityId { get; set; }
        public Guid EmergencyId { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}
