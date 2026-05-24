using AssessmentService.Models;

namespace AssessmentService.Features.Shared;

internal static class AssessmentMapper
{
    private static string StatusString(AssessmentReportStatus s) => s switch
    {
        AssessmentReportStatus.Pending => "PENDING",
        AssessmentReportStatus.Sent => "SENT",
        AssessmentReportStatus.Completed => "COMPLETED",
        AssessmentReportStatus.Failed => "FAILED",
        _ => s.ToString().ToUpperInvariant()
    };

    internal static Grpc.AssessmentReportResponse ToResponse(AssessmentReport report)
    {
        var response = new Grpc.AssessmentReportResponse
        {
            Id = report.Id.ToString(),
            EmergencyId = report.EmergencyId.ToString(),
            Status = StatusString(report.Status),
            RetryCount = report.RetryCount,
            CreatedAt = report.CreatedAt.ToString("o")
        };

        if (report.AiResponse != null) response.AiResponse = report.AiResponse;
        if (report.LastError != null) response.LastError = report.LastError;
        if (report.SentAt != null) response.SentAt = report.SentAt.Value.ToString("o");

        return response;
    }
}
