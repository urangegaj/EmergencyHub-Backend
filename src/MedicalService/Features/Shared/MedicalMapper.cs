using Grpc.Core;
using MedicalService.Models;
using Shared.Auth;
using DomainMedicalCaseStatus = MedicalService.Models.MedicalCaseStatus;
using DomainMedicalUnitStatus = MedicalService.Models.MedicalUnitStatus;

namespace MedicalService.Features.Shared;

internal static class MedicalMapper
{
    internal static readonly TimeSpan UnitsCacheTtl = TimeSpan.FromSeconds(15);

    internal static string UnitCacheKey(Guid cityId) => $"medical:units:city:{cityId}";

    internal static Guid GetCityId(ServerCallContext context)
    {
        var value = context.RequestHeaders.GetValue(ClaimNames.CityId)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing city_id metadata."));

        if (!Guid.TryParse(value, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id in metadata."));

        return cityId;
    }

    internal static MedicalCaseResponse MapCase(MedicalCase c, MedicalUnit? unit)
    {
        var response = new MedicalCaseResponse
        {
            Id = c.Id.ToString(),
            EmergencyId = c.EmergencyId.ToString(),
            CityId = c.CityId.ToString(),
            Status = (MedicalCaseStatus)c.Status,
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

    internal static MedicalUnitResponse MapUnit(MedicalUnit u) => new()
    {
        Id = u.Id.ToString(),
        CityId = u.CityId.ToString(),
        Name = u.Name,
        Status = (MedicalUnitStatus)u.Status,
    };
}
