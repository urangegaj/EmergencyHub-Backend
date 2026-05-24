using EmergencyService.Grpc;
using Grpc.Core;

namespace EmergencyService.Features.AssignEmergency;

public interface IAssignEmergencyHandler
{
    Task<EmergencyResponse> HandleAsync(AssignEmergencyRequest request, ServerCallContext context);
}
