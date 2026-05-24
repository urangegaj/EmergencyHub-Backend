using EmergencyService.Features.Shared;

using EmergencyService.Data;
using EmergencyService.Grpc;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace EmergencyService.Features.GetEmergency;

public class GetEmergencyHandler(EmergencyDbContext db) : IGetEmergencyHandler
{
    public async Task<EmergencyResponse> HandleAsync(GetEmergencyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        var emergency = await db.Emergencies
            .Include(e => e.EmergencyType)
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.Id == emergencyId && e.CityId == cityId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Emergency not found."));

        return EmergencyMapper.ToResponse(emergency);
    }
}
