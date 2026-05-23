using System.Text.Json;
using Confluent.Kafka;
using PoliceService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Auth;
using Shared.Kafka;
using Shared.Redis;
using DomainPoliceCaseStatus = PoliceService.Models.PoliceCaseStatus;
using DomainPoliceUnitStatus = PoliceService.Models.PoliceUnitStatus;

namespace PoliceService.Services;

public class PoliceGrpcService(
    PoliceDbContext db,
    IProducer<string, string> producer,
    IDistributedLock distributedLock,
    IRedisCache cache) : Police.PoliceBase
{
    private static readonly TimeSpan UnitsCacheTtl = TimeSpan.FromSeconds(15);

    public override async Task<GetCasesResponse> GetCases(GetCasesRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

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
            MapCase(c, c.AssignedUnitId.HasValue ? units.GetValueOrDefault(c.AssignedUnitId.Value) : null)));

        return response;
    }

    public override async Task<PoliceCaseResponse> GetCase(GetCaseRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var policeCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Police case not found."));

        Models.PoliceUnit? unit = null;
        if (policeCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([policeCase.AssignedUnitId.Value], context.CancellationToken);

        return MapCase(policeCase, unit);
    }

    public override async Task<PoliceCaseResponse> UpdateCase(UpdateCaseRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var policeCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Police case not found."));

        var newStatus = (DomainPoliceCaseStatus)request.Status;

        var validTransition =
            (policeCase.Status == DomainPoliceCaseStatus.OPEN && newStatus == DomainPoliceCaseStatus.IN_PROGRESS) ||
            (policeCase.Status == DomainPoliceCaseStatus.IN_PROGRESS && newStatus == DomainPoliceCaseStatus.CLOSED);

        if (!validTransition)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid status transition: {policeCase.Status} → {newStatus}."));

        Models.PoliceUnit? unit = null;
        IAsyncDisposable? unitLock = null;
        try
        {
            if (newStatus == DomainPoliceCaseStatus.IN_PROGRESS && request.HasUnitId)
            {
                if (!Guid.TryParse(request.UnitId, out var unitId))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

                unitLock = await distributedLock.TryAcquireAsync(
                    $"lock:unit:{cityId}:{unitId}", TimeSpan.FromSeconds(10), context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.Unavailable, "Unit is currently being assigned. Please retry."));

                unit = await db.Units
                    .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, "Police unit not found."));

                policeCase.AssignedUnitId = unit.Id;
                unit.Status = DomainPoliceUnitStatus.DEPLOYED;
            }

            if (newStatus == DomainPoliceCaseStatus.CLOSED)
            {
                policeCase.ClosedAt = DateTime.UtcNow;

                if (policeCase.AssignedUnitId.HasValue)
                {
                    unit = await db.Units.FindAsync([policeCase.AssignedUnitId.Value], context.CancellationToken);
                    if (unit is not null)
                        unit.Status = DomainPoliceUnitStatus.AVAILABLE;
                }
            }

            policeCase.Status = newStatus;
            policeCase.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(context.CancellationToken);
            await cache.InvalidateAsync($"police:units:city:{cityId}", context.CancellationToken);
        }
        finally
        {
            if (unitLock is not null) await unitLock.DisposeAsync();
        }

        var payload = JsonSerializer.Serialize(new
        {
            emergency_id = policeCase.EmergencyId.ToString(),
            case_id = policeCase.Id.ToString(),
            city_id = policeCase.CityId.ToString(),
            department_type = "Police",
            status = newStatus.ToString(),
            assigned_unit_id = policeCase.AssignedUnitId?.ToString()
        });

        await producer.ProduceAsync(
            Topics.DepartmentCaseUpdated,
            new Message<string, string> { Key = policeCase.EmergencyId.ToString(), Value = payload },
            context.CancellationToken);

        if (unit is null && policeCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([policeCase.AssignedUnitId.Value], context.CancellationToken);

        return MapCase(policeCase, unit);
    }

    public override async Task<GetUnitsResponse> GetUnits(GetUnitsRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);
        var cacheKey = $"police:units:city:{cityId}";

        var cached = await cache.GetAsync<List<CachedUnit>>(cacheKey, context.CancellationToken);
        if (cached is not null)
        {
            var hit = new GetUnitsResponse();
            hit.Units.AddRange(cached.Select(u => new PoliceUnitResponse
            {
                Id     = u.Id,
                CityId = u.CityId,
                Name   = u.Name,
                Status = Enum.Parse<PoliceUnitStatus>(u.Status)
            }));
            return hit;
        }

        var units = await db.Units
            .Where(u => u.CityId == cityId)
            .ToListAsync(context.CancellationToken);

        await cache.SetAsync(
            cacheKey,
            units.Select(u => new CachedUnit(u.Id.ToString(), u.CityId.ToString(), u.Name, u.Status.ToString())).ToList(),
            UnitsCacheTtl,
            context.CancellationToken);

        var response = new GetUnitsResponse();
        response.Units.AddRange(units.Select(MapUnit));
        return response;
    }

    public override async Task<PoliceUnitResponse> UpdateUnitStatus(UpdateUnitStatusRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.UnitId, out var unitId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

        var unit = await db.Units
            .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Police unit not found."));

        unit.Status = (DomainPoliceUnitStatus)request.Status;

        await db.SaveChangesAsync(context.CancellationToken);
        await cache.InvalidateAsync($"police:units:city:{cityId}", context.CancellationToken);

        return MapUnit(unit);
    }

    private static Guid GetCityId(ServerCallContext context)
    {
        var value = context.RequestHeaders.GetValue(ClaimNames.CityId)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing city_id metadata."));

        if (!Guid.TryParse(value, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id in metadata."));

        return cityId;
    }

    private static PoliceCaseResponse MapCase(Models.PoliceCase c, Models.PoliceUnit? unit)
    {
        var response = new PoliceCaseResponse
        {
            Id          = c.Id.ToString(),
            EmergencyId = c.EmergencyId.ToString(),
            CityId      = c.CityId.ToString(),
            Status      = (PoliceCaseStatus)c.Status,
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

    private static PoliceUnitResponse MapUnit(Models.PoliceUnit u) => new()
    {
        Id     = u.Id.ToString(),
        CityId = u.CityId.ToString(),
        Name   = u.Name,
        Status = (PoliceUnitStatus)u.Status,
    };
}
