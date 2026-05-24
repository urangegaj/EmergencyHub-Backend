using Grpc.Core;

namespace FireService.Features.UpdateCase;

public interface IUpdateCaseHandler
{
    Task<FireCaseResponse> HandleAsync(UpdateCaseRequest request, ServerCallContext context);
}
