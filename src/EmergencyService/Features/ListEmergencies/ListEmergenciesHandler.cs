using EmergencyService.Features.Shared;

using EmergencyService.Data;
using EmergencyService.Grpc;
using EmergencyService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using Shared.Redis;

namespace EmergencyService.Features.ListEmergencies;

public class ListEmergenciesHandler(EmergencyDbContext db, IRedisCache cache) : IListEmergenciesHandler
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<ListEmergenciesResponse> HandleAsync(ListEmergenciesRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        var isFiltered = !string.IsNullOrEmpty(request.Status)
            || !string.IsNullOrEmpty(request.TypeName)
            || request.FromTs != 0
            || request.ToTs != 0
            || !string.IsNullOrEmpty(request.Q);

        if (!isFiltered)
        {
            var cacheKey = EmergencyMapper.CacheKey(cityId);
            var cached = await cache.GetAsync<List<CachedEmergency>>(cacheKey);
            if (cached is not null)
            {
                var hit = new ListEmergenciesResponse
                {
                    TotalCount = cached.Count,
                    Page = 1,
                    PageSize = cached.Count
                };
                hit.Emergencies.AddRange(cached.Select(EmergencyMapper.ToResponseFromCached));
                return hit;
            }

            var all = await db.Emergencies
                .Include(e => e.EmergencyType)
                .Include(e => e.Assignments)
                .Where(e => e.CityId == cityId)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync(context.CancellationToken);

            await cache.SetAsync(cacheKey, all.Select(EmergencyMapper.ToCached).ToList(), CacheTtl);

            var unfiltered = new ListEmergenciesResponse
            {
                TotalCount = all.Count,
                Page = 1,
                PageSize = all.Count
            };
            unfiltered.Emergencies.AddRange(all.Select(EmergencyMapper.ToResponse));
            return unfiltered;
        }

        var query = db.Emergencies
            .Include(e => e.EmergencyType)
            .Include(e => e.Assignments)
            .Where(e => e.CityId == cityId);

        if (!string.IsNullOrEmpty(request.Status)
            && Enum.TryParse<EmergencyStatus>(request.Status, ignoreCase: true, out var statusFilter))
            query = query.Where(e => e.Status == statusFilter);

        if (!string.IsNullOrEmpty(request.TypeName))
            query = query.Where(e => e.EmergencyType.Name.ToLower() == request.TypeName.ToLower());

        if (request.FromTs != 0)
        {
            var from = DateTimeOffset.FromUnixTimeMilliseconds(request.FromTs).UtcDateTime;
            query = query.Where(e => e.CreatedAt >= from);
        }

        if (request.ToTs != 0)
        {
            var to = DateTimeOffset.FromUnixTimeMilliseconds(request.ToTs).UtcDateTime;
            query = query.Where(e => e.CreatedAt <= to);
        }

        if (!string.IsNullOrEmpty(request.Q))
        {
            var searchTerm = request.Q;
            query = query.Where(e => e.SearchVector!.Matches(EF.Functions.PlainToTsQuery("english", searchTerm)));
        }

        var total = await query.CountAsync(context.CancellationToken);

        query = request.SortBy == "status"
            ? (request.Order == "asc" ? query.OrderBy(e => e.Status) : query.OrderByDescending(e => e.Status))
            : (request.Order == "asc" ? query.OrderBy(e => e.CreatedAt) : query.OrderByDescending(e => e.CreatedAt));

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;

        var emergencies = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListEmergenciesResponse
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
        response.Emergencies.AddRange(emergencies.Select(EmergencyMapper.ToResponse));
        return response;
    }
}
