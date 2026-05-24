using EmergencyService.Grpc;
using Grpc.Core;

namespace EmergencyService.Features.CreateEmergency;

public interface ICreateEmergencyHandler
{
    Task<EmergencyResponse> HandleAsync(CreateEmergencyRequest request, ServerCallContext context);
}
