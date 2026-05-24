using Grpc.Core;

namespace PoliceService.Features.UpdateUnitStatus;

public interface IUpdateUnitStatusHandler
{
    Task<PoliceUnitResponse> HandleAsync(UpdateUnitStatusRequest request, ServerCallContext context);
}
