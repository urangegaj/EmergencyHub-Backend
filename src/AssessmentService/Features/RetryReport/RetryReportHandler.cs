using AssessmentService.Features.Shared;

using AssessmentService.Data;
using AssessmentService.Models;
using AssessmentService.Services;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssessmentService.Features.RetryReport;

public class RetryReportHandler(
    AssessmentDbContext db,
    IServiceScopeFactory scopeFactory,
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

        report.Status = AssessmentReportStatus.Pending;
        report.LastError = null;
        report.RetryCount++;

        await db.SaveChangesAsync(context.CancellationToken);

        var reportId = report.Id;
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedPipeline = scope.ServiceProvider.GetRequiredService<AssessmentPipelineService>();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();

            var fresh = await scopedDb.Reports.FindAsync(reportId);
            if (fresh is null) return;

            var (aiResponse, lastError) = await scopedPipeline.RunAsync(fresh, CancellationToken.None);
            fresh.AiResponse = aiResponse;
            fresh.LastError = lastError;
            fresh.Status = aiResponse != null ? AssessmentReportStatus.Completed : AssessmentReportStatus.Failed;
            fresh.SentAt = aiResponse != null ? DateTime.UtcNow : null;
            await scopedDb.SaveChangesAsync();
        });

        return AssessmentMapper.ToResponse(report);
    }
}
