using EmergencyService.Grpc;
using Grpc.Core;

namespace EmergencyService.Features.GetEmergency;

public interface IGetEmergencyHandler
{
    Task<EmergencyResponse> HandleAsync(GetEmergencyRequest request, ServerCallContext context);
}
