using FireService.Features.Shared;

using FireService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace FireService.Features.GetCase;

public class GetCaseHandler(FireDbContext db) : IGetCaseHandler
{
    public async Task<FireCaseResponse> HandleAsync(GetCaseRequest request, ServerCallContext context)
    {
        var cityId = FireMapper.GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var fireCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire case not found."));

        FireService.Models.FireUnit? unit = null;
        if (fireCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([fireCase.AssignedUnitId.Value], context.CancellationToken);

        return FireMapper.MapCase(fireCase, unit);
    }
}
