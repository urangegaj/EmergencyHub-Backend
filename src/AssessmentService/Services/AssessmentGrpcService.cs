using AssessmentService.Data;
using AssessmentService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace AssessmentService.Services;

public sealed class AssessmentGrpcService(
    AssessmentDbContext db,
    AssessmentPipelineService pipeline,
    ILogger<AssessmentGrpcService> logger) : Grpc.Assessment.AssessmentBase
{
    public override async Task<Grpc.AssessmentReportResponse> GetReport(
        Grpc.GetReportRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id"));

        var report = await db.Reports.FirstOrDefaultAsync(r => r.EmergencyId == emergencyId, context.CancellationToken);
        if (report is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"No report for emergency {emergencyId}"));

        return ToResponse(report);
    }

    public override async Task<Grpc.AssessmentReportResponse> RetryReport(
        Grpc.RetryReportRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id"));

        var report = await db.Reports.FirstOrDefaultAsync(r => r.EmergencyId == emergencyId, context.CancellationToken);
        if (report is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"No report for emergency {emergencyId}"));

        if (report.Status == AssessmentReportStatus.Completed)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Report already completed"));

        logger.LogInformation("Retrying report for emergency {Id} (attempt #{Retry})", emergencyId, report.RetryCount + 1);

        var (aiResponse, lastError) = await pipeline.RunAsync(report, context.CancellationToken);

        report.AiResponse = aiResponse;
        report.LastError = lastError;
        report.Status = aiResponse != null ? AssessmentReportStatus.Completed : AssessmentReportStatus.Failed;
        report.SentAt = aiResponse != null ? DateTime.UtcNow : null;
        report.RetryCount++;

        await db.SaveChangesAsync(context.CancellationToken);

        return ToResponse(report);
    }

    private static Grpc.AssessmentReportResponse ToResponse(AssessmentReport report)
    {
        var response = new Grpc.AssessmentReportResponse
        {
            Id = report.Id.ToString(),
            EmergencyId = report.EmergencyId.ToString(),
            Status = report.Status.ToString(),
            RetryCount = report.RetryCount,
            CreatedAt = report.CreatedAt.ToString("o")
        };

        if (report.AiResponse != null) response.AiResponse = report.AiResponse;
        if (report.LastError != null) response.LastError = report.LastError;
        if (report.SentAt != null) response.SentAt = report.SentAt.Value.ToString("o");

        return response;
    }
}
