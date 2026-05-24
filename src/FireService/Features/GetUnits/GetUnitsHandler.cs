using FireService.Features.Shared;

using FireService.Data;
using FireService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Redis;

namespace FireService.Features.GetUnits;

public class GetUnitsHandler(FireDbContext db, IRedisCache cache) : IGetUnitsHandler
{
    public async Task<GetUnitsResponse> HandleAsync(GetUnitsRequest request, ServerCallContext context)
    {
        var cityId = FireMapper.GetCityId(context);
        var cacheKey = FireMapper.UnitCacheKey(cityId);

        var cached = await cache.GetAsync<List<CachedUnit>>(cacheKey, context.CancellationToken);
        if (cached is not null)
        {
            var hit = new GetUnitsResponse();
            hit.Units.AddRange(cached.Select(u => new FireUnitResponse
            {
                Id = u.Id,
                CityId = u.CityId,
                Name = u.Name,
                Status = (FireUnitStatus)Enum.Parse<FireService.Models.FireUnitStatus>(u.Status)
            }));
            return hit;
        }

        var units = await db.Units
            .Where(u => u.CityId == cityId)
            .ToListAsync(context.CancellationToken);

        await cache.SetAsync(
            cacheKey,
            units.Select(u => new CachedUnit(u.Id.ToString(), u.CityId.ToString(), u.Name, u.Status.ToString())).ToList(),
            FireMapper.UnitsCacheTtl,
            context.CancellationToken);

        var response = new GetUnitsResponse();
        response.Units.AddRange(units.Select(FireMapper.MapUnit));
        return response;
    }
}
