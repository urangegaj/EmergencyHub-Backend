using EmergencyService.Grpc;
using Grpc.Core;

namespace EmergencyService.Features.PollEmergency;

public interface IPollEmergencyHandler
{
    Task<EmergencyResponse> HandleAsync(PollEmergencyRequest request, ServerCallContext context);
}
