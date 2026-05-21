using EmergencyService.Data;
using EmergencyService.Grpc;
using EmergencyService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace EmergencyService.Services;

public class EmergencyGrpcService(EmergencyDbContext db) : EmergencyService.Grpc.Emergency.EmergencyBase
{
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

        var emergency = await db.Emergencies
            .Include(e => e.EmergencyType)
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.Id == emergencyId && e.CityId == cityId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Emergency not found."));

        var existingDepts = emergency.Assignments.Select(a => a.DepartmentType).ToHashSet();
        foreach (var dept in departments)
        {
            if (existingDepts.Contains(dept)) continue;
            db.Assignments.Add(new EmergencyAssignment
            {
                EmergencyId = emergencyId,
                DepartmentType = dept
            });
        }

        emergency.Status = EmergencyStatus.Dispatched;
        emergency.UpdatedAt = DateTime.UtcNow;
        emergency.Version++;

        db.StatusHistory.Add(new EmergencyStatusHistory
        {
            EmergencyId = emergencyId,
            Status = EmergencyStatus.Dispatched,
            ChangedByUserId = assignedBy
        });

        await db.SaveChangesAsync();

        await db.Entry(emergency).Collection(e => e.Assignments).LoadAsync();
        return ToResponse(emergency);
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
