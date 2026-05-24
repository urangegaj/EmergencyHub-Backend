using Grpc.Core;

namespace AssessmentService.Features.GetReport;

public interface IGetReportHandler
{
    Task<Grpc.AssessmentReportResponse> HandleAsync(Grpc.GetReportRequest request, ServerCallContext context);
}
