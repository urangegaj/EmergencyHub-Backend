using EmergencyService.Grpc;
using Grpc.Core;

namespace EmergencyService.Features.ListEmergencies;

public interface IListEmergenciesHandler
{
    Task<ListEmergenciesResponse> HandleAsync(ListEmergenciesRequest request, ServerCallContext context);
}
