using MedicalService.Features.Shared;

using Grpc.Core;
using MedicalService.Data;
using Microsoft.EntityFrameworkCore;
using Shared.Redis;
using DomainMedicalUnitStatus = MedicalService.Models.MedicalUnitStatus;

namespace MedicalService.Features.UpdateUnitStatus;

public class UpdateUnitStatusHandler(MedicalDbContext db, IRedisCache cache) : IUpdateUnitStatusHandler
{
    public async Task<MedicalUnitResponse> HandleAsync(UpdateUnitStatusRequest request, ServerCallContext context)
    {
        var cityId = MedicalMapper.GetCityId(context);

        if (!Guid.TryParse(request.UnitId, out var unitId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

        var unit = await db.Units
            .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Medical unit not found."));

        unit.Status = (DomainMedicalUnitStatus)request.Status;

        await db.SaveChangesAsync(context.CancellationToken);
        await cache.InvalidateAsync(MedicalMapper.UnitCacheKey(cityId), context.CancellationToken);

        return MedicalMapper.MapUnit(unit);
    }
}
