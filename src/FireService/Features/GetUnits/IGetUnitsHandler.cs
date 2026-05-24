using Grpc.Core;

namespace FireService.Features.GetUnits;

public interface IGetUnitsHandler
{
    Task<GetUnitsResponse> HandleAsync(GetUnitsRequest request, ServerCallContext context);
}
