using System.Text.Json;
using Confluent.Kafka;
using FireService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Auth;
using Shared.Kafka;
using Shared.Redis;
using DomainFireCaseStatus = FireService.Models.FireCaseStatus;
using DomainFireUnitStatus = FireService.Models.FireUnitStatus;

namespace FireService.Services;

public class FireGrpcService(
    FireDbContext db,
    IProducer<string, string> producer,
    IDistributedLock distributedLock,
    IRedisCache cache,
    ILogger<FireGrpcService> logger) : Fire.FireBase
{
    private static readonly TimeSpan UnitsCacheTtl = TimeSpan.FromSeconds(15);

    public override async Task<GetCasesResponse> GetCases(GetCasesRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        var query = db.Cases.Where(c => c.CityId == cityId);

        if (request.HasStatus)
            query = query.Where(c => c.Status == (DomainFireCaseStatus)request.Status);

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

    public override async Task<FireCaseResponse> GetCase(GetCaseRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var fireCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire case not found."));

        Models.FireUnit? unit = null;
        if (fireCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([fireCase.AssignedUnitId.Value], context.CancellationToken);

        return MapCase(fireCase, unit);
    }

    public override async Task<FireCaseResponse> UpdateCase(UpdateCaseRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var fireCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire case not found."));

        var newStatus = (DomainFireCaseStatus)request.Status;

        var validTransition =
            (fireCase.Status == DomainFireCaseStatus.OPEN && newStatus == DomainFireCaseStatus.IN_PROGRESS) ||
            (fireCase.Status == DomainFireCaseStatus.IN_PROGRESS && newStatus == DomainFireCaseStatus.CLOSED);

        if (!validTransition)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid status transition: {fireCase.Status} → {newStatus}."));

        Models.FireUnit? unit = null;
        IAsyncDisposable? unitLock = null;
        try
        {
            if (newStatus == DomainFireCaseStatus.IN_PROGRESS && request.HasUnitId)
            {
                if (!Guid.TryParse(request.UnitId, out var unitId))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

                unitLock = await distributedLock.TryAcquireAsync(
                    $"lock:unit:{cityId}:{unitId}", TimeSpan.FromSeconds(10), context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.Unavailable, "Unit is currently being assigned. Please retry."));

                unit = await db.Units
                    .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire unit not found."));

                fireCase.AssignedUnitId = unit.Id;
                unit.Status = DomainFireUnitStatus.DEPLOYED;
            }

            if (newStatus == DomainFireCaseStatus.CLOSED)
            {
                fireCase.ClosedAt = DateTime.UtcNow;

                if (fireCase.AssignedUnitId.HasValue)
                {
                    unit = await db.Units.FindAsync([fireCase.AssignedUnitId.Value], context.CancellationToken);
                    if (unit is not null)
                        unit.Status = DomainFireUnitStatus.AVAILABLE;
                }
            }

            fireCase.Status = newStatus;
            fireCase.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(context.CancellationToken);
            await cache.InvalidateAsync($"fire:units:city:{cityId}", context.CancellationToken);
        }
        finally
        {
            if (unitLock is not null) await unitLock.DisposeAsync();
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                emergency_id = fireCase.EmergencyId.ToString(),
                case_id = fireCase.Id.ToString(),
                city_id = fireCase.CityId.ToString(),
                department_type = "Fire",
                status = newStatus.ToString(),
                assigned_unit_id = fireCase.AssignedUnitId?.ToString()
            });

            await producer.ProduceAsync(
                Topics.DepartmentCaseUpdated,
                new Message<string, string> { Key = fireCase.EmergencyId.ToString(), Value = payload },
                context.CancellationToken);

            logger.LogInformation(
                "Published {Topic} for emergency {EmergencyId}, case {CaseId}, status {Status}",
                Topics.DepartmentCaseUpdated,
                fireCase.EmergencyId,
                fireCase.Id,
                newStatus);
        }
        catch (ProduceException<string, string> ex)
        {
            logger.LogError(
                ex,
                "Failed to publish {Topic} for emergency {EmergencyId} after DB update",
                Topics.DepartmentCaseUpdated,
                fireCase.EmergencyId);
            throw new RpcException(new Status(StatusCode.Internal, "Case updated but event publish failed."));
        }

        if (unit is null && fireCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([fireCase.AssignedUnitId.Value], context.CancellationToken);

        return MapCase(fireCase, unit);
    }

    public override async Task<GetUnitsResponse> GetUnits(GetUnitsRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);
        var cacheKey = $"fire:units:city:{cityId}";

        var cached = await cache.GetAsync<List<CachedUnit>>(cacheKey, context.CancellationToken);
        if (cached is not null)
        {
            var hit = new GetUnitsResponse();
            hit.Units.AddRange(cached.Select(u => new FireUnitResponse
            {
                Id     = u.Id,
                CityId = u.CityId,
                Name   = u.Name,
                Status = Enum.Parse<FireUnitStatus>(u.Status)
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

    public override async Task<FireUnitResponse> UpdateUnitStatus(UpdateUnitStatusRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.UnitId, out var unitId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

        var unit = await db.Units
            .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire unit not found."));

        unit.Status = (DomainFireUnitStatus)request.Status;

        await db.SaveChangesAsync(context.CancellationToken);
        await cache.InvalidateAsync($"fire:units:city:{cityId}", context.CancellationToken);

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

    private static FireCaseResponse MapCase(Models.FireCase c, Models.FireUnit? unit)
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

    private static FireUnitResponse MapUnit(Models.FireUnit u) => new()
    {
        Id     = u.Id.ToString(),
        CityId = u.CityId.ToString(),
        Name   = u.Name,
        Status = (FireUnitStatus)u.Status,
    };
}
