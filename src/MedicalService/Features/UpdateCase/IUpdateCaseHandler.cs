using Grpc.Core;

namespace MedicalService.Features.UpdateCase;

public interface IUpdateCaseHandler
{
    Task<MedicalCaseResponse> HandleAsync(UpdateCaseRequest request, ServerCallContext context);
}
