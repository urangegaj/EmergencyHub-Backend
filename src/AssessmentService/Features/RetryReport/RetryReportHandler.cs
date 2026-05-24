using AssessmentService.Features.Shared;

using AssessmentService.Data;
using AssessmentService.Models;
using AssessmentService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssessmentService.Features.RetryReport;

public class RetryReportHandler(
    AssessmentDbContext db,
    AssessmentPipelineService pipeline,
    ILogger<RetryReportHandler> logger) : IRetryReportHandler
{
    public async Task<Grpc.AssessmentReportResponse> HandleAsync(Grpc.RetryReportRequest request, ServerCallContext context)
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
        report.LastError  = lastError;
        report.Status     = aiResponse != null ? AssessmentReportStatus.Completed : AssessmentReportStatus.Failed;
        report.SentAt     = aiResponse != null ? DateTime.UtcNow : null;
        report.RetryCount++;

        await db.SaveChangesAsync(context.CancellationToken);

        return AssessmentMapper.ToResponse(report);
    }
}
