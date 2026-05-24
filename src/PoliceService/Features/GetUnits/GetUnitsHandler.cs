using PoliceService.Features.Shared;

using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using PoliceService.Data;
using PoliceService.Services;
using Shared.Redis;

namespace PoliceService.Features.GetUnits;

public class GetUnitsHandler(PoliceDbContext db, IRedisCache cache) : IGetUnitsHandler
{
    public async Task<GetUnitsResponse> HandleAsync(GetUnitsRequest request, ServerCallContext context)
    {
        var cityId = PoliceMapper.GetCityId(context);
        var cacheKey = PoliceMapper.UnitCacheKey(cityId);

        var cached = await cache.GetAsync<List<CachedUnit>>(cacheKey, context.CancellationToken);
        if (cached is not null)
        {
            var hit = new GetUnitsResponse();
            hit.Units.AddRange(cached.Select(u => new PoliceUnitResponse
            {
                Id = u.Id,
                CityId = u.CityId,
                Name = u.Name,
                Status = (PoliceUnitStatus)Enum.Parse<PoliceService.Models.PoliceUnitStatus>(u.Status)
            }));
            return hit;
        }

        var units = await db.Units
            .Where(u => u.CityId == cityId)
            .ToListAsync(context.CancellationToken);

        await cache.SetAsync(
            cacheKey,
            units.Select(u => new CachedUnit(u.Id.ToString(), u.CityId.ToString(), u.Name, u.Status.ToString())).ToList(),
            PoliceMapper.UnitsCacheTtl,
            context.CancellationToken);

        var response = new GetUnitsResponse();
        response.Units.AddRange(units.Select(PoliceMapper.MapUnit));
        return response;
    }
}
