using Grpc.Core;

namespace AssessmentService.Features.RetryReport;

public interface IRetryReportHandler
{
    Task<Grpc.AssessmentReportResponse> HandleAsync(Grpc.RetryReportRequest request, ServerCallContext context);
}
