namespace NotificationService.Models;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CityId { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid EmergencyId { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public NotificationStatus Status { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
