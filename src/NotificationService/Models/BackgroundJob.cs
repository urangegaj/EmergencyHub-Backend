namespace NotificationService.Models;

public class BackgroundJob
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public BackgroundJobStatus Status { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
