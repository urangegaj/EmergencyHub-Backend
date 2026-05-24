using FireService.Features.Shared;

using FireService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Redis;
using DomainFireUnitStatus = FireService.Models.FireUnitStatus;

namespace FireService.Features.UpdateUnitStatus;

public class UpdateUnitStatusHandler(FireDbContext db, IRedisCache cache) : IUpdateUnitStatusHandler
{
    public async Task<FireUnitResponse> HandleAsync(UpdateUnitStatusRequest request, ServerCallContext context)
    {
        var cityId = FireMapper.GetCityId(context);

        if (!Guid.TryParse(request.UnitId, out var unitId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

        var unit = await db.Units
            .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire unit not found."));

        unit.Status = (DomainFireUnitStatus)request.Status;

        await db.SaveChangesAsync(context.CancellationToken);
        await cache.InvalidateAsync(FireMapper.UnitCacheKey(cityId), context.CancellationToken);

        return FireMapper.MapUnit(unit);
    }
}
