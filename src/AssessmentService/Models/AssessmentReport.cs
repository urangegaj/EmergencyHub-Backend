namespace AssessmentService.Models;

public class AssessmentReport
{
    public Guid Id { get; set; }
    public Guid EmergencyId { get; set; }
    public Guid CityId { get; set; }
    public string ReportPayload { get; set; } = string.Empty;
    public AssessmentReportStatus Status { get; set; }
    public string? AiResponse { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
