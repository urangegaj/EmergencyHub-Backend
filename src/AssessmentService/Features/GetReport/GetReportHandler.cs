using AssessmentService.Features.Shared;

using AssessmentService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace AssessmentService.Features.GetReport;

public class GetReportHandler(AssessmentDbContext db) : IGetReportHandler
{
    public async Task<Grpc.AssessmentReportResponse> HandleAsync(Grpc.GetReportRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id"));

        var report = await db.Reports.FirstOrDefaultAsync(r => r.EmergencyId == emergencyId, context.CancellationToken);
        if (report is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"No report for emergency {emergencyId}"));

        return AssessmentMapper.ToResponse(report);
    }
}
