using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class EmergencyCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyCreatedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.EmergencyCreated;

    protected override async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Received null or empty message on {Topic}", Topic);
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("emergency_id", out var emergencyIdProp) ||
            !Guid.TryParse(emergencyIdProp.GetString(), out var emergencyId))
            return;

        if (!root.TryGetProperty("city_id", out var cityIdProp) ||
            !Guid.TryParse(cityIdProp.GetString(), out var cityId))
            return;

        if (!root.TryGetProperty("reported_by_user_id", out var userIdProp) ||
            !Guid.TryParse(userIdProp.GetString(), out var userId))
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        var exists = await db.Notifications.AnyAsync(
            n => n.EmergencyId == emergencyId && n.Type == NotificationTypes.EmergencyCreated && n.UserId == userId,
            ct);

        if (exists)
            return;

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CityId = cityId,
            Type = NotificationTypes.EmergencyCreated,
            EmergencyId = emergencyId,
            Status = NotificationStatus.IN_APP_ONLY,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Persisted emergency.created notification for emergency {EmergencyId}", emergencyId);
    }
}
