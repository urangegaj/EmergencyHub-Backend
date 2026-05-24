using Grpc.Core;

namespace MedicalService.Features.GetCase;

public interface IGetCaseHandler
{
    Task<MedicalCaseResponse> HandleAsync(GetCaseRequest request, ServerCallContext context);
}
