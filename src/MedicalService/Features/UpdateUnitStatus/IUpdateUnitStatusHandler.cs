using Grpc.Core;

namespace MedicalService.Features.UpdateUnitStatus;

public interface IUpdateUnitStatusHandler
{
    Task<MedicalUnitResponse> HandleAsync(UpdateUnitStatusRequest request, ServerCallContext context);
}
