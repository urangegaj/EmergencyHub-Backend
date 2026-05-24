using System.Text.Json;
using Confluent.Kafka;
using EmergencyService.Data;
using EmergencyService.Grpc;
using EmergencyService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using Shared.Kafka;
using Shared.Redis;

namespace EmergencyService.Services;

public class EmergencyGrpcService(
    EmergencyDbContext db,
    IProducer<string, string> producer,
    PollRegistry pollRegistry,
    IRedisCache cache) : EmergencyService.Grpc.Emergency.EmergencyBase
{
    private static readonly TimeSpan EmergencyCacheTtl = TimeSpan.FromSeconds(30);

    public override async Task<EmergencyResponse> CreateEmergency(CreateEmergencyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));
        if (!Guid.TryParse(request.ReportedByUserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid reported_by_user_id."));
        if (!Guid.TryParse(request.EmergencyTypeId, out var typeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_type_id."));

        var emergencyType = await db.EmergencyTypes.FindAsync(typeId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Emergency type not found."));

        var emergency = new Models.Emergency
        {
            CityId = cityId,
            ReportedByUserId = userId,
            EmergencyTypeId = typeId,
            Description = request.Description,
            Address = request.Address
        };
        db.Emergencies.Add(emergency);

        db.StatusHistory.Add(new EmergencyStatusHistory
        {
            EmergencyId = emergency.Id,
            Status = EmergencyStatus.Reported,
            ChangedByUserId = userId
        });

        await db.SaveChangesAsync();
        await cache.InvalidateAsync(EmergencyCacheKey(cityId));

        await producer.ProduceAsync(
            Topics.EmergencyCreated,
            new Message<string, string>
            {
                Key = emergency.Id.ToString(),
                Value = JsonSerializer.Serialize(new
                {
                    emergency_id = emergency.Id.ToString(),
                    city_id = emergency.CityId.ToString(),
                    reported_by_user_id = emergency.ReportedByUserId.ToString(),
                    emergency_type_id = emergency.EmergencyTypeId.ToString(),
                    emergency_type_name = emergencyType.Name,
                    description = emergency.Description,
                    address = emergency.Address,
                    created_at = emergency.CreatedAt.ToString("O")
                })
            });

        emergency.EmergencyType = emergencyType;
        return ToResponse(emergency);
    }

    public override async Task<EmergencyResponse> GetEmergency(GetEmergencyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        var emergency = await db.Emergencies
            .Include(e => e.EmergencyType)
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.Id == emergencyId && e.CityId == cityId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Emergency not found."));

        return ToResponse(emergency);
    }

    public override async Task<ListEmergenciesResponse> ListEmergencies(ListEmergenciesRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        var isFiltered = !string.IsNullOrEmpty(request.Status)
            || !string.IsNullOrEmpty(request.TypeName)
            || request.FromTs != 0
            || request.ToTs != 0
            || !string.IsNullOrEmpty(request.Q);

        if (!isFiltered)
        {
            var cacheKey = EmergencyCacheKey(cityId);
            var cached = await cache.GetAsync<List<CachedEmergency>>(cacheKey);
            if (cached is not null)
            {
                var hit = new ListEmergenciesResponse
                {
                    TotalCount = cached.Count,
                    Page = 1,
                    PageSize = cached.Count
                };
                hit.Emergencies.AddRange(cached.Select(ToResponseFromCached));
                return hit;
            }

            var all = await db.Emergencies
                .Include(e => e.EmergencyType)
                .Include(e => e.Assignments)
                .Where(e => e.CityId == cityId)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync(context.CancellationToken);

            await cache.SetAsync(cacheKey, all.Select(ToCached).ToList(), EmergencyCacheTtl);

            var unfiltered = new ListEmergenciesResponse
            {
                TotalCount = all.Count,
                Page = 1,
                PageSize = all.Count
            };
            unfiltered.Emergencies.AddRange(all.Select(ToResponse));
            return unfiltered;
        }

        var query = db.Emergencies
            .Include(e => e.EmergencyType)
            .Include(e => e.Assignments)
            .Where(e => e.CityId == cityId);

        if (!string.IsNullOrEmpty(request.Status)
            && Enum.TryParse<EmergencyStatus>(request.Status, ignoreCase: true, out var statusFilter))
            query = query.Where(e => e.Status == statusFilter);

        if (!string.IsNullOrEmpty(request.TypeName))
            query = query.Where(e => e.EmergencyType.Name.ToLower() == request.TypeName.ToLower());

        if (request.FromTs != 0)
        {
            var from = DateTimeOffset.FromUnixTimeMilliseconds(request.FromTs).UtcDateTime;
            query = query.Where(e => e.CreatedAt >= from);
        }

        if (request.ToTs != 0)
        {
            var to = DateTimeOffset.FromUnixTimeMilliseconds(request.ToTs).UtcDateTime;
            query = query.Where(e => e.CreatedAt <= to);
        }

        if (!string.IsNullOrEmpty(request.Q))
        {
            var searchTerm = request.Q;
            query = query.Where(e => e.SearchVector!.Matches(EF.Functions.PlainToTsQuery("english", searchTerm)));
        }

        var total = await query.CountAsync(context.CancellationToken);

        query = request.SortBy == "status"
            ? (request.Order == "asc" ? query.OrderBy(e => e.Status) : query.OrderByDescending(e => e.Status))
            : (request.Order == "asc" ? query.OrderBy(e => e.CreatedAt) : query.OrderByDescending(e => e.CreatedAt));

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;

        var emergencies = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListEmergenciesResponse
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
        response.Emergencies.AddRange(emergencies.Select(ToResponse));
        return response;
    }

    public override async Task<EmergencyResponse> AssignEmergency(AssignEmergencyRequest request, ServerCallContext context)
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

        await cache.InvalidateAsync(EmergencyCacheKey(cityId));
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
        return ToResponse(emergency);
    }

    public override async Task<EmergencyResponse> PollEmergency(
        PollEmergencyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        var timeoutSeconds = request.TimeoutSeconds switch
        {
            <= 0 => 30,
            > 60 => 60,
            _    => request.TimeoutSeconds
        };

        var tcs = pollRegistry.Subscribe(emergencyId);
        try
        {
            var emergency = await FetchEmergencyAsync(emergencyId, cityId, context.CancellationToken);
            if (emergency.Version > request.Since)
                return ToResponse(emergency);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken, timeoutCts.Token);

            try
            {
                await tcs.Task.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
                when (timeoutCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
            {
            }

            emergency = await FetchEmergencyAsync(emergencyId, cityId, context.CancellationToken);
            return ToResponse(emergency);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(Status.DefaultCancelled);
        }
        finally
        {
            pollRegistry.Unsubscribe(emergencyId, tcs);
        }
    }

    private async Task<Models.Emergency> FetchEmergencyAsync(Guid emergencyId, Guid cityId, CancellationToken ct)
        => await db.Emergencies
            .AsNoTracking()
            .Include(e => e.EmergencyType)
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.Id == emergencyId && e.CityId == cityId, ct)
           ?? throw new RpcException(new Status(StatusCode.NotFound, "Emergency not found."));

    private static string EmergencyCacheKey(Guid cityId) => $"emergencies:city:{cityId}";

    private static CachedEmergency ToCached(Models.Emergency e) => new(
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

    private static EmergencyResponse ToResponseFromCached(CachedEmergency e)
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

    private static EmergencyResponse ToResponse(Models.Emergency e)
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
