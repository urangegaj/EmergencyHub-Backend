using System.Text.Json;
using EmergencyService.Grpc;
using Microsoft.Extensions.Options;
using NotificationService.Models;
using NotificationService.Services;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class EmergencyStatusUpdatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyStatusUpdatedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.EmergencyStatusUpdated;

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

        if (!root.TryGetProperty("old_status", out var oldStatusProp))
            return;

        if (!root.TryGetProperty("new_status", out var newStatusProp))
            return;

        var oldStatus = oldStatusProp.GetString() ?? string.Empty;
        var newStatus = newStatusProp.GetString() ?? string.Empty;

        using var scope = scopeFactory.CreateScope();
        var emergencyClient = scope.ServiceProvider.GetRequiredService<Emergency.EmergencyClient>();
        var userLookup = scope.ServiceProvider.GetRequiredService<AuthUserLookupService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();

        var emergency = await emergencyClient.GetEmergencyAsync(
            new GetEmergencyRequest
            {
                EmergencyId = emergencyId.ToString(),
                CityId = cityId.ToString()
            },
            cancellationToken: ct);

        if (!Guid.TryParse(emergency.ReportedByUserId, out var reporterId))
            return;

        var reporter = await userLookup.GetUserAsync(reporterId, ct);
        if (reporter is null)
            return;

        var subject = $"Emergency status updated to {newStatus}";
        var body =
            $"Your emergency status has changed.\n\n" +
            $"Emergency ID: {emergencyId}\n" +
            $"Previous status: {oldStatus}\n" +
            $"New status: {newStatus}\n" +
            $"City ID: {cityId}";

        await dispatch.SendEmailNotificationAsync(
            reporterId,
            cityId,
            emergencyId,
            NotificationTypes.EmergencyStatusUpdated,
            reporter.Email,
            subject,
            body,
            ct);
    }
}
