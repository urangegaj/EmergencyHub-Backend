using EmergencyService.Features.Shared;

using System.Text.Json;
using Confluent.Kafka;
using EmergencyService.Data;
using EmergencyService.Grpc;
using EmergencyService.Models;
using Grpc.Core;
using Shared.Enums;
using Shared.Kafka;
using Shared.Redis;

namespace EmergencyService.Features.CreateEmergency;

public class CreateEmergencyHandler(
    EmergencyDbContext db,
    IProducer<string, string> producer,
    IRedisCache cache) : ICreateEmergencyHandler
{
    public async Task<EmergencyResponse> HandleAsync(CreateEmergencyRequest request, ServerCallContext context)
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
        await cache.InvalidateAsync(EmergencyMapper.CacheKey(cityId));

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
        return EmergencyMapper.ToResponse(emergency);
    }
}
