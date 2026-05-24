using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;

namespace NotificationService.Features.EmergencyAssignedNotification;

public class EmergencyAssignedNotificationHandler(
    NotificationDbContext db,
    AuthUserLookupService userLookup,
    NotificationDispatchService dispatch,
    ILogger<EmergencyAssignedNotificationHandler> logger) : IEmergencyAssignedNotificationHandler
{
    public async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Received null or empty emergency.assigned message, skipping");
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

        if (!root.TryGetProperty("assignment_id", out var assignmentIdProp) ||
            !Guid.TryParse(assignmentIdProp.GetString(), out var assignmentId))
        {
            logger.LogWarning(
                "emergency.assigned missing assignment_id for emergency {EmergencyId}; cannot apply event idempotency",
                emergencyId);
            return;
        }

        var assignmentKey = assignmentId.ToString();

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

            var alreadyNotified = await NotificationIdempotency.ExistsAsync(
                db,
                emergencyId,
                NotificationTypes.EmergencyAssigned,
                userId,
                departmentType,
                assignmentKey,
                ct);

            if (alreadyNotified)
            {
                logger.LogInformation(
                    "Skipping duplicate emergency.assigned for user {UserId}, emergency {EmergencyId}, assignment {AssignmentId}",
                    userId, emergencyId, assignmentId);
                continue;
            }

            var subject = $"Emergency assigned to {departmentType}";
            var body =
                $"An emergency has been assigned to your department.\n\n" +
                $"Emergency ID: {emergencyId}\n" +
                $"Department: {departmentType}\n" +
                $"Assignment ID: {assignmentId}\n" +
                $"City ID: {cityId}";

            await dispatch.SendEmailNotificationAsync(
                userId,
                cityId,
                emergencyId,
                NotificationTypes.EmergencyAssigned,
                user.Email,
                subject,
                body,
                departmentType,
                assignmentKey,
                ct);
        }
    }
}
