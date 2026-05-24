using Grpc.Core;

namespace FireService.Features.UpdateUnitStatus;

public interface IUpdateUnitStatusHandler
{
    Task<FireUnitResponse> HandleAsync(UpdateUnitStatusRequest request, ServerCallContext context);
}
