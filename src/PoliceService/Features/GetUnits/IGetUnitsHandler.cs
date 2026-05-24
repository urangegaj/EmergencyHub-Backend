using Grpc.Core;

namespace PoliceService.Features.GetUnits;

public interface IGetUnitsHandler
{
    Task<GetUnitsResponse> HandleAsync(GetUnitsRequest request, ServerCallContext context);
}
