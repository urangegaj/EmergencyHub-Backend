using FireService.Models;
using Grpc.Core;
using Shared.Auth;
using DomainFireCaseStatus = FireService.Models.FireCaseStatus;
using DomainFireUnitStatus = FireService.Models.FireUnitStatus;

namespace FireService.Features.Shared;

internal static class FireMapper
{
    internal static readonly TimeSpan UnitsCacheTtl = TimeSpan.FromSeconds(15);

    internal static string UnitCacheKey(Guid cityId) => $"fire:units:city:{cityId}";

    internal static Guid GetCityId(ServerCallContext context)
    {
        var value = context.RequestHeaders.GetValue(ClaimNames.CityId)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing city_id metadata."));

        if (!Guid.TryParse(value, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id in metadata."));

        return cityId;
    }

    internal static FireCaseResponse MapCase(FireCase c, FireUnit? unit)
    {
        var response = new FireCaseResponse
        {
            Id          = c.Id.ToString(),
            EmergencyId = c.EmergencyId.ToString(),
            CityId      = c.CityId.ToString(),
            Status      = (FireCaseStatus)c.Status,
            CreatedAt   = c.CreatedAt.ToString("O"),
            UpdatedAt   = c.UpdatedAt.ToString("O"),
        };

        if (c.AssignedUnitId.HasValue)
            response.AssignedUnitId = c.AssignedUnitId.Value.ToString();

        if (unit is not null)
            response.AssignedUnitName = unit.Name;

        if (c.ClosedAt.HasValue)
            response.ClosedAt = c.ClosedAt.Value.ToString("O");

        return response;
    }

    internal static FireUnitResponse MapUnit(FireUnit u) => new()
    {
        Id     = u.Id.ToString(),
        CityId = u.CityId.ToString(),
        Name   = u.Name,
        Status = (FireUnitStatus)u.Status,
    };
}
