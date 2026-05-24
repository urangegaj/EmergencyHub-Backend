using EmergencyService.Features.Shared;

using System.Text.Json;
using Confluent.Kafka;
using EmergencyService.Data;
using EmergencyService.Grpc;
using EmergencyService.Models;
using EmergencyService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using Shared.Kafka;
using Shared.Redis;

namespace EmergencyService.Features.AssignEmergency;

public class AssignEmergencyHandler(
    EmergencyDbContext db,
    IProducer<string, string> producer,
    PollRegistry pollRegistry,
    IRedisCache cache) : IAssignEmergencyHandler
{
    public async Task<EmergencyResponse> HandleAsync(AssignEmergencyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));
        if (!Guid.TryParse(request.AssignedByUserId, out var assignedBy))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid assigned_by_user_id."));
        if (request.Departments.Count == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "At least one department is required."));

        var departments = new List<DepartmentType>();
        foreach (var dept in request.Departments)
        {
            if (!Enum.TryParse<DepartmentType>(dept, ignoreCase: true, out var parsed))
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown department: {dept}"));
            departments.Add(parsed);
        }

        var saved = false;
        var newAssignments = new List<EmergencyAssignment>();
        Models.Emergency emergency = null!;
        var oldStatus = EmergencyStatus.Reported;

        for (var attempt = 0; attempt < 3 && !saved; attempt++)
        {
            db.ChangeTracker.Clear();
            newAssignments = [];

            emergency = await db.Emergencies
                .Include(e => e.EmergencyType)
                .Include(e => e.Assignments)
                .FirstOrDefaultAsync(e => e.Id == emergencyId && e.CityId == cityId)
                ?? throw new RpcException(new Status(StatusCode.NotFound, "Emergency not found."));

            var existingDepts = emergency.Assignments.Select(a => a.DepartmentType).ToHashSet();
            foreach (var dept in departments)
            {
                if (existingDepts.Contains(dept)) continue;
                var a = new EmergencyAssignment { EmergencyId = emergencyId, DepartmentType = dept };
                db.Assignments.Add(a);
                newAssignments.Add(a);
            }

            oldStatus = emergency.Status;
            emergency.Status = EmergencyStatus.Dispatched;
            emergency.UpdatedAt = DateTime.UtcNow;
            emergency.Version++;

            db.StatusHistory.Add(new EmergencyStatusHistory
            {
                EmergencyId = emergencyId,
                Status = EmergencyStatus.Dispatched,
                ChangedByUserId = assignedBy
            });

            try
            {
                await db.SaveChangesAsync();
                saved = true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { }
        }

        if (!saved)
            throw new RpcException(new Status(StatusCode.Aborted, "Concurrent modification conflict, please retry."));

        await cache.InvalidateAsync(EmergencyMapper.CacheKey(cityId));
        pollRegistry.Signal(emergencyId);

        foreach (var assignment in newAssignments)
        {
            await producer.ProduceAsync(
                Topics.EmergencyAssigned,
                new Message<string, string>
                {
                    Key = emergencyId.ToString(),
                    Value = JsonSerializer.Serialize(new
                    {
                        emergency_id = emergencyId.ToString(),
                        city_id = cityId.ToString(),
                        department_type = assignment.DepartmentType.ToString(),
                        assignment_id = assignment.Id.ToString(),
                        assigned_at = assignment.AssignedAt.ToString("O")
                    })
                });
        }

        await producer.ProduceAsync(
            Topics.EmergencyStatusUpdated,
            new Message<string, string>
            {
                Key = emergencyId.ToString(),
                Value = JsonSerializer.Serialize(new
                {
                    emergency_id = emergencyId.ToString(),
                    city_id = cityId.ToString(),
                    old_status = oldStatus.ToString(),
                    new_status = EmergencyStatus.Dispatched.ToString(),
                    updated_at = emergency.UpdatedAt.ToString("O")
                })
            });

        await db.Entry(emergency).Collection(e => e.Assignments).LoadAsync();
        return EmergencyMapper.ToResponse(emergency);
    }
}
