using Grpc.Core;

namespace MedicalService.Features.GetCases;

public interface IGetCasesHandler
{
    Task<GetCasesResponse> HandleAsync(GetCasesRequest request, ServerCallContext context);
}
