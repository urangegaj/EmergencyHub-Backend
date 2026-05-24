using Grpc.Core;

namespace PoliceService.Features.GetCase;

public interface IGetCaseHandler
{
    Task<PoliceCaseResponse> HandleAsync(GetCaseRequest request, ServerCallContext context);
}
