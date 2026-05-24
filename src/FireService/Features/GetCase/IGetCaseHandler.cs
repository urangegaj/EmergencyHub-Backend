using Grpc.Core;

namespace FireService.Features.GetCase;

public interface IGetCaseHandler
{
    Task<FireCaseResponse> HandleAsync(GetCaseRequest request, ServerCallContext context);
}
