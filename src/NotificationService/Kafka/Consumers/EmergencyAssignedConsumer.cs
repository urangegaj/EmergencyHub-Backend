using System.Text.Json;
using Microsoft.Extensions.Options;
using NotificationService.Models;
using NotificationService.Services;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class EmergencyAssignedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyAssignedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.EmergencyAssigned;

    protected override async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Received null or empty message on {Topic}", Topic);
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("department_type", out var deptProp))
            return;

        var departmentType = deptProp.GetString() ?? string.Empty;

        if (!root.TryGetProperty("emergency_id", out var emergencyIdProp) ||
            !Guid.TryParse(emergencyIdProp.GetString(), out var emergencyId))
            return;

        if (!root.TryGetProperty("city_id", out var cityIdProp) ||
            !Guid.TryParse(cityIdProp.GetString(), out var cityId))
            return;

        using var scope = scopeFactory.CreateScope();
        var userLookup = scope.ServiceProvider.GetRequiredService<AuthUserLookupService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();

        var users = await userLookup.ListDepartmentUsersAsync(cityId, departmentType, ct);
        if (users.Count == 0)
        {
            logger.LogWarning(
                "No department users found for {Department} in city {CityId}, emergency {EmergencyId}",
                departmentType, cityId, emergencyId);
            return;
        }

        foreach (var user in users)
        {
            if (!Guid.TryParse(user.UserId, out var userId))
                continue;

            var subject = $"Emergency assigned to {departmentType}";
            var body =
                $"An emergency has been assigned to your department.\n\n" +
                $"Emergency ID: {emergencyId}\n" +
                $"Department: {departmentType}\n" +
                $"City ID: {cityId}";

            await dispatch.SendEmailNotificationAsync(
                userId,
                cityId,
                emergencyId,
                NotificationTypes.EmergencyAssigned,
                user.Email,
                subject,
                body,
                ct);
        }
    }
}
