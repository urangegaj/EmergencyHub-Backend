using PoliceService.Features.Shared;

using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using PoliceService.Data;
using DomainPoliceCaseStatus = PoliceService.Models.PoliceCaseStatus;

namespace PoliceService.Features.GetCases;

public class GetCasesHandler(PoliceDbContext db) : IGetCasesHandler
{
    public async Task<GetCasesResponse> HandleAsync(GetCasesRequest request, ServerCallContext context)
    {
        var cityId = PoliceMapper.GetCityId(context);

        var query = db.Cases.Where(c => c.CityId == cityId);

        if (request.HasStatus)
            query = query.Where(c => c.Status == (DomainPoliceCaseStatus)request.Status);

        var cases = await query.ToListAsync(context.CancellationToken);

        var unitIds = cases
            .Where(c => c.AssignedUnitId.HasValue)
            .Select(c => c.AssignedUnitId!.Value)
            .ToHashSet();

        var units = unitIds.Count > 0
            ? await db.Units
                .Where(u => unitIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, context.CancellationToken)
            : [];

        var response = new GetCasesResponse();
        response.Cases.AddRange(cases.Select(c =>
            PoliceMapper.MapCase(c, c.AssignedUnitId.HasValue ? units.GetValueOrDefault(c.AssignedUnitId.Value) : null)));

        return response;
    }
}
