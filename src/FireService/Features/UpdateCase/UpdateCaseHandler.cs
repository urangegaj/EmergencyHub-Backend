using FireService.Features.Shared;

using System.Text.Json;
using Confluent.Kafka;
using FireService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Kafka;
using Shared.Redis;
using DomainFireCaseStatus = FireService.Models.FireCaseStatus;
using DomainFireUnitStatus = FireService.Models.FireUnitStatus;

namespace FireService.Features.UpdateCase;

public class UpdateCaseHandler(
    FireDbContext db,
    IProducer<string, string> producer,
    IDistributedLock distributedLock,
    IRedisCache cache,
    ILogger<UpdateCaseHandler> logger) : IUpdateCaseHandler
{
    public async Task<FireCaseResponse> HandleAsync(UpdateCaseRequest request, ServerCallContext context)
    {
        var cityId = FireMapper.GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var fireCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Fire case not found."));

        var newStatus = (DomainFireCaseStatus)request.Status;

        var validTransition =
            (fireCase.Status == DomainFireCaseStatus.OPEN && newStatus == DomainFireCaseStatus.IN_PROGRESS) ||
            (fireCase.Status == DomainFireCaseStatus.IN_PROGRESS && newStatus == DomainFireCaseStatus.CLOSED);

        if (!validTransition)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid status transition: {fireCase.Status} → {newStatus}."));

        FireService.Models.FireUnit? unit = null;
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
                unit.Status = DomainFireUnitStatus.ON_SCENE;
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
            await cache.InvalidateAsync(FireMapper.UnitCacheKey(cityId), context.CancellationToken);
        }
        finally
        {
            if (unitLock is not null) await unitLock.DisposeAsync();
        }

        try
        {
            await producer.ProduceAsync(
                Topics.DepartmentCaseUpdated,
                new Message<string, string>
                {
                    Key = fireCase.EmergencyId.ToString(),
                    Value = JsonSerializer.Serialize(new
                    {
                        emergency_id = fireCase.EmergencyId.ToString(),
                        case_id = fireCase.Id.ToString(),
                        city_id = fireCase.CityId.ToString(),
                        department_type = "Fire",
                        status = newStatus.ToString(),
                        assigned_unit_id = fireCase.AssignedUnitId?.ToString()
                    })
                },
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

        return FireMapper.MapCase(fireCase, unit);
    }
}
