using Grpc.Core;

namespace MedicalService.Features.GetUnits;

public interface IGetUnitsHandler
{
    Task<GetUnitsResponse> HandleAsync(GetUnitsRequest request, ServerCallContext context);
}
