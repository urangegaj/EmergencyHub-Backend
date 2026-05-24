using Grpc.Core;

namespace FireService.Features.GetCases;

public interface IGetCasesHandler
{
    Task<GetCasesResponse> HandleAsync(GetCasesRequest request, ServerCallContext context);
}
