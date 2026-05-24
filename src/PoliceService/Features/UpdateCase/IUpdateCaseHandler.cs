using Grpc.Core;

namespace PoliceService.Features.UpdateCase;

public interface IUpdateCaseHandler
{
    Task<PoliceCaseResponse> HandleAsync(UpdateCaseRequest request, ServerCallContext context);
}
