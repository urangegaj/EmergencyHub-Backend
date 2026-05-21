using System.Text.Json;
using Confluent.Kafka;
using MedicalService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Auth;
using Shared.Kafka;
using DomainMedicalCaseStatus = MedicalService.Models.MedicalCaseStatus;
using DomainMedicalUnitStatus = MedicalService.Models.MedicalUnitStatus;

namespace MedicalService.Services;

public class MedicalGrpcService(MedicalDbContext db, IProducer<string, string> producer) : Medical.MedicalBase
{
    public override async Task<GetCasesResponse> GetCases(GetCasesRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        var query = db.Cases.Where(c => c.CityId == cityId);

        if (request.HasStatus)
            query = query.Where(c => c.Status == (DomainMedicalCaseStatus)request.Status);

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

    public override async Task<MedicalCaseResponse> GetCase(GetCaseRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var medicalCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Medical case not found."));

        Models.MedicalUnit? unit = null;
        if (medicalCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([medicalCase.AssignedUnitId.Value], context.CancellationToken);

        return MapCase(medicalCase, unit);
    }

    public override async Task<MedicalCaseResponse> UpdateCase(UpdateCaseRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var medicalCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Medical case not found."));

        var newStatus = (DomainMedicalCaseStatus)request.Status;

        var validTransition =
            (medicalCase.Status == DomainMedicalCaseStatus.OPEN && newStatus == DomainMedicalCaseStatus.IN_PROGRESS) ||
            (medicalCase.Status == DomainMedicalCaseStatus.IN_PROGRESS && newStatus == DomainMedicalCaseStatus.CLOSED);

        if (!validTransition)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid status transition: {medicalCase.Status} → {newStatus}."));

        Models.MedicalUnit? unit = null;

        if (newStatus == DomainMedicalCaseStatus.IN_PROGRESS)
        {
            if (request.HasUnitId)
            {
                if (!Guid.TryParse(request.UnitId, out var unitId))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

                unit = await db.Units
                    .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, "Medical unit not found."));

                medicalCase.AssignedUnitId = unit.Id;
                unit.Status = DomainMedicalUnitStatus.DEPLOYED;
            }
        }

        if (newStatus == DomainMedicalCaseStatus.CLOSED)
        {
            medicalCase.ClosedAt = DateTime.UtcNow;

            if (medicalCase.AssignedUnitId.HasValue)
            {
                unit = await db.Units.FindAsync([medicalCase.AssignedUnitId.Value], context.CancellationToken);
                if (unit is not null)
                    unit.Status = DomainMedicalUnitStatus.AVAILABLE;
            }
        }

        medicalCase.Status = newStatus;
        medicalCase.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(context.CancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            emergency_id = medicalCase.EmergencyId.ToString(),
            case_id = medicalCase.Id.ToString(),
            city_id = medicalCase.CityId.ToString(),
            status = newStatus.ToString(),
            assigned_unit_id = medicalCase.AssignedUnitId?.ToString()
        });

        await producer.ProduceAsync(
            Topics.DepartmentCaseUpdated,
            new Message<string, string> { Key = medicalCase.EmergencyId.ToString(), Value = payload },
            context.CancellationToken);

        if (unit is null && medicalCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([medicalCase.AssignedUnitId.Value], context.CancellationToken);

        return MapCase(medicalCase, unit);
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

    public override async Task<MedicalUnitResponse> UpdateUnitStatus(UpdateUnitStatusRequest request, ServerCallContext context)
    {
        var cityId = GetCityId(context);

        if (!Guid.TryParse(request.UnitId, out var unitId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

        var unit = await db.Units
            .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Medical unit not found."));

        unit.Status = (DomainMedicalUnitStatus)request.Status;

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

    private static MedicalCaseResponse MapCase(Models.MedicalCase c, Models.MedicalUnit? unit)
    {
        var response = new MedicalCaseResponse
        {
            Id          = c.Id.ToString(),
            EmergencyId = c.EmergencyId.ToString(),
            CityId      = c.CityId.ToString(),
            Status      = (MedicalCaseStatus)c.Status,
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

    private static MedicalUnitResponse MapUnit(Models.MedicalUnit u) => new()
    {
        Id     = u.Id.ToString(),
        CityId = u.CityId.ToString(),
        Name   = u.Name,
        Status = (MedicalUnitStatus)u.Status,
    };
}
