using System.Text.Json;
using Confluent.Kafka;
using FireService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Auth;
using Shared.Kafka;
using DomainFireCaseStatus = FireService.Models.FireCaseStatus;
using DomainFireUnitStatus = FireService.Models.FireUnitStatus;

namespace FireService.Services;

public class FireGrpcService(FireDbContext db, IProducer<string, string> producer) : Fire.FireBase
{
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

        if (newStatus == DomainFireCaseStatus.IN_PROGRESS)
        {
            if (request.HasUnitId)
            {
                if (!Guid.TryParse(request.UnitId, out var unitId))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

                unit = await db.Units
                    .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire unit not found."));

                fireCase.AssignedUnitId = unit.Id;
                unit.Status = DomainFireUnitStatus.DEPLOYED;
            }
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

        if (unit is null && fireCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([fireCase.AssignedUnitId.Value], context.CancellationToken);

        return MapCase(fireCase, unit);
    }

    public override async Task<GetUnitsResponse> GetUnits(GetUnitsRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        var units = await db.Units
            .Where(u => u.CityId == cityId)
            .ToListAsync(context.CancellationToken);

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
