using Grpc.Core;

namespace PoliceService.Features.GetCases;

public interface IGetCasesHandler
{
    Task<GetCasesResponse> HandleAsync(GetCasesRequest request, ServerCallContext context);
}
