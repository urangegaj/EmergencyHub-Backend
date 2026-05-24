using PoliceService.Features.Shared;

using System.Text.Json;
using Confluent.Kafka;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PoliceService.Data;
using Shared.Kafka;
using Shared.Redis;
using DomainPoliceCaseStatus = PoliceService.Models.PoliceCaseStatus;
using DomainPoliceUnitStatus = PoliceService.Models.PoliceUnitStatus;

namespace PoliceService.Features.UpdateCase;

public class UpdateCaseHandler(
    PoliceDbContext db,
    IProducer<string, string> producer,
    IDistributedLock distributedLock,
    IRedisCache cache,
    ILogger<UpdateCaseHandler> logger) : IUpdateCaseHandler
{
    public async Task<PoliceCaseResponse> HandleAsync(UpdateCaseRequest request, ServerCallContext context)
    {
        var cityId = PoliceMapper.GetCityId(context);

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
            await cache.InvalidateAsync(PoliceMapper.UnitCacheKey(cityId), context.CancellationToken);
        }
        finally
        {
            if (unitLock is not null) await unitLock.DisposeAsync();
        }

        var payload = JsonSerializer.Serialize(new
        {
            emergency_id = policeCase.EmergencyId.ToString(),
            case_id      = policeCase.Id.ToString(),
            city_id      = policeCase.CityId.ToString(),
            department_type = "Police",
            status       = newStatus.ToString(),
            assigned_unit_id = policeCase.AssignedUnitId?.ToString()
        });

        try
        {
            await producer.ProduceAsync(
                Topics.DepartmentCaseUpdated,
                new Message<string, string> { Key = policeCase.EmergencyId.ToString(), Value = payload },
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish DepartmentCaseUpdated for police case {CaseId}", policeCase.Id);
        }

        if (unit is null && policeCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([policeCase.AssignedUnitId.Value], context.CancellationToken);

        return PoliceMapper.MapCase(policeCase, unit);
    }
}
