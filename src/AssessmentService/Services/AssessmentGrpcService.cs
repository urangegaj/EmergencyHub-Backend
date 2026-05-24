using AssessmentService.Features.GetReport;
using AssessmentService.Features.RetryReport;
using Grpc.Core;

namespace AssessmentService.Services;

public sealed class AssessmentGrpcService(
    IGetReportHandler getReport,
    IRetryReportHandler retryReport) : Grpc.Assessment.AssessmentBase
{
    public override Task<Grpc.AssessmentReportResponse> GetReport(
        Grpc.GetReportRequest request, ServerCallContext context)
        => getReport.HandleAsync(request, context);

    public override Task<Grpc.AssessmentReportResponse> RetryReport(
        Grpc.RetryReportRequest request, ServerCallContext context)
        => retryReport.HandleAsync(request, context);
}
