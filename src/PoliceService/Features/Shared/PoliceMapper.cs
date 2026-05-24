using Grpc.Core;
using PoliceService.Models;
using Shared.Auth;
using DomainPoliceCaseStatus = PoliceService.Models.PoliceCaseStatus;
using DomainPoliceUnitStatus = PoliceService.Models.PoliceUnitStatus;

namespace PoliceService.Features.Shared;

internal static class PoliceMapper
{
    internal static readonly TimeSpan UnitsCacheTtl = TimeSpan.FromSeconds(15);

    internal static string UnitCacheKey(Guid cityId) => $"police:units:city:{cityId}";

    internal static Guid GetCityId(ServerCallContext context)
    {
        var value = context.RequestHeaders.GetValue(ClaimNames.CityId)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing city_id metadata."));

        if (!Guid.TryParse(value, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id in metadata."));

        return cityId;
    }

    internal static PoliceCaseResponse MapCase(PoliceCase c, PoliceUnit? unit)
    {
        var response = new PoliceCaseResponse
        {
            Id = c.Id.ToString(),
            EmergencyId = c.EmergencyId.ToString(),
            CityId = c.CityId.ToString(),
            Status = (PoliceCaseStatus)c.Status,
            CreatedAt = c.CreatedAt.ToString("O"),
            UpdatedAt = c.UpdatedAt.ToString("O"),
        };

        if (c.AssignedUnitId.HasValue)
            response.AssignedUnitId = c.AssignedUnitId.Value.ToString();

        if (unit is not null)
            response.AssignedUnitName = unit.Name;

        if (c.ClosedAt.HasValue)
            response.ClosedAt = c.ClosedAt.Value.ToString("O");

        return response;
    }

    internal static PoliceUnitResponse MapUnit(PoliceUnit u) => new()
    {
        Id = u.Id.ToString(),
        CityId = u.CityId.ToString(),
        Name = u.Name,
        Status = (PoliceUnitStatus)u.Status,
    };
}
