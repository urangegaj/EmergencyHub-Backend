using MedicalService.Features.Shared;

using System.Text.Json;
using Confluent.Kafka;
using Grpc.Core;
using MedicalService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Kafka;
using Shared.Redis;
using DomainMedicalCaseStatus = MedicalService.Models.MedicalCaseStatus;
using DomainMedicalUnitStatus = MedicalService.Models.MedicalUnitStatus;

namespace MedicalService.Features.UpdateCase;

public class UpdateCaseHandler(
    MedicalDbContext db,
    IProducer<string, string> producer,
    IDistributedLock distributedLock,
    IRedisCache cache,
    ILogger<UpdateCaseHandler> logger) : IUpdateCaseHandler
{
    public async Task<MedicalCaseResponse> HandleAsync(UpdateCaseRequest request, ServerCallContext context)
    {
        var cityId = MedicalMapper.GetCityId(context);

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
        IAsyncDisposable? unitLock = null;
        try
        {
            if (newStatus == DomainMedicalCaseStatus.IN_PROGRESS && request.HasUnitId)
            {
                if (!Guid.TryParse(request.UnitId, out var unitId))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid unit_id."));

                unitLock = await distributedLock.TryAcquireAsync(
                    $"lock:unit:{cityId}:{unitId}", TimeSpan.FromSeconds(10), context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.Unavailable, "Unit is currently being assigned. Please retry."));

                unit = await db.Units
                    .FirstOrDefaultAsync(u => u.Id == unitId && u.CityId == cityId, context.CancellationToken)
                    ?? throw new RpcException(new Status(StatusCode.NotFound, "Medical unit not found."));

                medicalCase.AssignedUnitId = unit.Id;
                unit.Status = DomainMedicalUnitStatus.DEPLOYED;
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
            await cache.InvalidateAsync(MedicalMapper.UnitCacheKey(cityId), context.CancellationToken);
        }
        finally
        {
            if (unitLock is not null) await unitLock.DisposeAsync();
        }

        var payload = JsonSerializer.Serialize(new
        {
            emergency_id = medicalCase.EmergencyId.ToString(),
            case_id      = medicalCase.Id.ToString(),
            city_id      = medicalCase.CityId.ToString(),
            department_type = "Medical",
            status       = newStatus.ToString(),
            assigned_unit_id = medicalCase.AssignedUnitId?.ToString()
        });

        try
        {
            await producer.ProduceAsync(
                Topics.DepartmentCaseUpdated,
                new Message<string, string> { Key = medicalCase.EmergencyId.ToString(), Value = payload },
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish DepartmentCaseUpdated for medical case {CaseId}", medicalCase.Id);
        }

        if (unit is null && medicalCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([medicalCase.AssignedUnitId.Value], context.CancellationToken);

        return MedicalMapper.MapCase(medicalCase, unit);
    }
}
