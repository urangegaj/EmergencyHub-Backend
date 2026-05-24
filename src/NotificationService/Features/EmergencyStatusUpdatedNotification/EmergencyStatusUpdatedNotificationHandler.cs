using System.Text.Json;
using EmergencyService.Grpc;
using Microsoft.Extensions.Logging;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;

namespace NotificationService.Features.EmergencyStatusUpdatedNotification;

public class EmergencyStatusUpdatedNotificationHandler(
    NotificationDbContext db,
    Emergency.EmergencyClient emergencyClient,
    AuthUserLookupService userLookup,
    NotificationDispatchService dispatch,
    ILogger<EmergencyStatusUpdatedNotificationHandler> logger) : IEmergencyStatusUpdatedNotificationHandler
{
    public async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Received null or empty emergency.status.updated message, skipping");
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

        var hasOldStatus = root.TryGetProperty("old_status", out var oldStatusProp);
        var hasNewStatus = root.TryGetProperty("new_status", out var newStatusProp);

        if (!hasOldStatus || !hasNewStatus)
        {
            logger.LogWarning(
                "emergency.status.updated missing old_status/new_status for emergency {EmergencyId}; cannot apply transition idempotency",
                emergencyId);
            return;
        }

        var fromStatus = oldStatusProp.GetString() ?? string.Empty;
        var toStatus = newStatusProp.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fromStatus) || string.IsNullOrWhiteSpace(toStatus))
        {
            logger.LogWarning(
                "emergency.status.updated has empty transition for emergency {EmergencyId}; skipping",
                emergencyId);
            return;
        }

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

        var alreadyNotified = await NotificationIdempotency.ExistsAsync(
            db,
            emergencyId,
            NotificationTypes.EmergencyStatusUpdated,
            reporterId,
            fromStatus,
            toStatus,
            ct);

        if (alreadyNotified)
        {
            logger.LogInformation(
                "Skipping duplicate emergency.status.updated for user {UserId}, emergency {EmergencyId}, transition {FromStatus} -> {ToStatus}",
                reporterId, emergencyId, fromStatus, toStatus);
            return;
        }

        var subject = $"Emergency status updated to {toStatus}";
        var body =
            $"Your emergency status has changed.\n\n" +
            $"Emergency ID: {emergencyId}\n" +
            $"Previous status: {fromStatus}\n" +
            $"New status: {toStatus}\n" +
            $"City ID: {cityId}";

        await dispatch.SendEmailNotificationAsync(
            reporterId,
            cityId,
            emergencyId,
            NotificationTypes.EmergencyStatusUpdated,
            reporter.Email,
            subject,
            body,
            fromStatus,
            toStatus,
            ct);
    }
}
