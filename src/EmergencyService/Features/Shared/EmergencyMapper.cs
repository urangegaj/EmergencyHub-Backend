using EmergencyService.Data;
using EmergencyService.Grpc;
using EmergencyService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace EmergencyService.Features.Shared;

internal static class EmergencyMapper
{
    internal static string CacheKey(Guid cityId) => $"emergencies:city:{cityId}";

    internal static async Task<Models.Emergency> FetchAsync(
        EmergencyDbContext db, Guid emergencyId, Guid cityId, CancellationToken ct)
        => await db.Emergencies
            .AsNoTracking()
            .Include(e => e.EmergencyType)
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.Id == emergencyId && e.CityId == cityId, ct)
           ?? throw new RpcException(new Status(StatusCode.NotFound, "Emergency not found."));

    internal static CachedEmergency ToCached(Models.Emergency e) => new(
        e.Id.ToString(),
        e.CityId.ToString(),
        e.ReportedByUserId.ToString(),
        e.EmergencyTypeId.ToString(),
        e.EmergencyType?.Name ?? "",
        e.Description,
        e.Address,
        e.Status.ToString(),
        e.Version,
        e.CreatedAt.ToString("O"),
        e.UpdatedAt.ToString("O"),
        e.Assignments.Select(a => new CachedAssignment(
            a.Id.ToString(),
            a.DepartmentType.ToString(),
            a.AssignedAt.ToString("O"),
            a.ClosedAt?.ToString("O"))).ToList());

    internal static EmergencyResponse ToResponseFromCached(CachedEmergency e)
    {
        var response = new EmergencyResponse
        {
            Id = e.Id,
            CityId = e.CityId,
            ReportedByUserId = e.ReportedByUserId,
            EmergencyTypeId = e.EmergencyTypeId,
            EmergencyTypeName = e.EmergencyTypeName,
            Description = e.Description,
            Address = e.Address,
            Status = e.Status,
            Version = e.Version,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        };

        foreach (var a in e.Assignments)
        {
            var assignment = new AssignmentResponse
            {
                Id = a.Id,
                DepartmentType = a.DepartmentType,
                AssignedAt = a.AssignedAt
            };
            if (a.ClosedAt is not null)
                assignment.ClosedAt = a.ClosedAt;
            response.Assignments.Add(assignment);
        }

        return response;
    }

    internal static EmergencyResponse ToResponse(Models.Emergency e)
    {
        var response = new EmergencyResponse
        {
            Id = e.Id.ToString(),
            CityId = e.CityId.ToString(),
            ReportedByUserId = e.ReportedByUserId.ToString(),
            EmergencyTypeId = e.EmergencyTypeId.ToString(),
            EmergencyTypeName = e.EmergencyType?.Name ?? "",
            Description = e.Description,
            Address = e.Address,
            Status = e.Status.ToString(),
            Version = e.Version,
            CreatedAt = e.CreatedAt.ToString("O"),
            UpdatedAt = e.UpdatedAt.ToString("O")
        };

        foreach (var a in e.Assignments)
        {
            var assignment = new AssignmentResponse
            {
                Id = a.Id.ToString(),
                DepartmentType = a.DepartmentType.ToString(),
                AssignedAt = a.AssignedAt.ToString("O")
            };
            if (a.ClosedAt.HasValue)
                assignment.ClosedAt = a.ClosedAt.Value.ToString("O");
            response.Assignments.Add(assignment);
        }

        return response;
    }
}
