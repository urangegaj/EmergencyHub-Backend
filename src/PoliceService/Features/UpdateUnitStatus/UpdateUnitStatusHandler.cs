using PoliceService.Features.Shared;

using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using PoliceService.Data;
using Shared.Redis;
using DomainPoliceUnitStatus = PoliceService.Models.PoliceUnitStatus;

namespace PoliceService.Features.UpdateUnitStatus;

public class UpdateUnitStatusHandler(PoliceDbContext db, IRedisCache cache) : IUpdateUnitStatusHandler
{
    public async Task<PoliceUnitResponse> HandleAsync(UpdateUnitStatusRequest request, ServerCallContext context)
    {
        var cityId = PoliceMapper.GetCityId(context);

        if (!Guid.TryParse(request.UnitId, out var unitId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

        var unit = await db.Units
            .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Police unit not found."));

        unit.Status = (DomainPoliceUnitStatus)request.Status;

        await db.SaveChangesAsync(context.CancellationToken);
        await cache.InvalidateAsync(PoliceMapper.UnitCacheKey(cityId), context.CancellationToken);

        return PoliceMapper.MapUnit(unit);
    }
}
