using EmergencyService.Grpc;
using EmergencyService.Features.CreateEmergency;
using EmergencyService.Features.GetEmergency;
using EmergencyService.Features.ListEmergencies;
using EmergencyService.Features.AssignEmergency;
using EmergencyService.Features.PollEmergency;
using Grpc.Core;

namespace EmergencyService.Services;

public class EmergencyGrpcService(
    ICreateEmergencyHandler createEmergency,
    IGetEmergencyHandler getEmergency,
    IListEmergenciesHandler listEmergencies,
    IAssignEmergencyHandler assignEmergency,
    IPollEmergencyHandler pollEmergency) : EmergencyService.Grpc.Emergency.EmergencyBase
{
    public override Task<EmergencyResponse> CreateEmergency(CreateEmergencyRequest request, ServerCallContext context)
        => createEmergency.HandleAsync(request, context);

    public override Task<EmergencyResponse> GetEmergency(GetEmergencyRequest request, ServerCallContext context)
        => getEmergency.HandleAsync(request, context);

    public override Task<ListEmergenciesResponse> ListEmergencies(ListEmergenciesRequest request, ServerCallContext context)
        => listEmergencies.HandleAsync(request, context);

    public override Task<EmergencyResponse> AssignEmergency(AssignEmergencyRequest request, ServerCallContext context)
        => assignEmergency.HandleAsync(request, context);

    public override Task<EmergencyResponse> PollEmergency(PollEmergencyRequest request, ServerCallContext context)
        => pollEmergency.HandleAsync(request, context);
}
