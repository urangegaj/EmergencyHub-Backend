using MedicalService.Features.Shared;

using Grpc.Core;
using MedicalService.Data;
using MedicalService.Services;
using Microsoft.EntityFrameworkCore;
using Shared.Redis;

namespace MedicalService.Features.GetUnits;

public class GetUnitsHandler(MedicalDbContext db, IRedisCache cache) : IGetUnitsHandler
{
    public async Task<GetUnitsResponse> HandleAsync(GetUnitsRequest request, ServerCallContext context)
    {
        var cityId = MedicalMapper.GetCityId(context);
        var cacheKey = MedicalMapper.UnitCacheKey(cityId);

        var cached = await cache.GetAsync<List<CachedUnit>>(cacheKey, context.CancellationToken);
        if (cached is not null)
        {
            var hit = new GetUnitsResponse();
            hit.Units.AddRange(cached.Select(u => new MedicalUnitResponse
            {
                Id     = u.Id,
                CityId = u.CityId,
                Name   = u.Name,
                Status = Enum.Parse<MedicalUnitStatus>(u.Status)
            }));
            return hit;
        }

        var units = await db.Units
            .Where(u => u.CityId == cityId)
            .ToListAsync(context.CancellationToken);

        await cache.SetAsync(
            cacheKey,
            units.Select(u => new CachedUnit(u.Id.ToString(), u.CityId.ToString(), u.Name, u.Status.ToString())).ToList(),
            MedicalMapper.UnitsCacheTtl,
            context.CancellationToken);

        var response = new GetUnitsResponse();
        response.Units.AddRange(units.Select(MedicalMapper.MapUnit));
        return response;
    }
}
