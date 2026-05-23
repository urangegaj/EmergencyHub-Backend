using System.Text.Json;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class DepartmentCaseUpdatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<DepartmentCaseUpdatedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.DepartmentCaseUpdated;

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

        if (!root.TryGetProperty("department_type", out var deptProp))
            return;

        if (!root.TryGetProperty("status", out var statusProp))
            return;

        var departmentType = deptProp.GetString() ?? string.Empty;
        var caseStatus = statusProp.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(caseStatus))
        {
            logger.LogWarning(
                "department.case.updated missing status for emergency {EmergencyId}; skipping",
                emergencyId);
            return;
        }

        root.TryGetProperty("assigned_unit_id", out var unitProp);
        var assignedUnitId = unitProp.ValueKind == JsonValueKind.String ? unitProp.GetString() : null;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var userLookup = scope.ServiceProvider.GetRequiredService<AuthUserLookupService>();
        var dispatch = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();

        var users = await userLookup.ListDepartmentUsersAsync(cityId, departmentType, ct);
        if (users.Count == 0)
        {
            logger.LogWarning(
                "No department users found for case update {Department} city {CityId} emergency {EmergencyId}",
                departmentType, cityId, emergencyId);
            return;
        }

        var subject = $"{departmentType} case {FormatCaseEvent(caseStatus)}";
        var body =
            $"A department case was updated.\n\n" +
            $"Emergency ID: {emergencyId}\n" +
            $"Department: {departmentType}\n" +
            $"Case status: {caseStatus}\n" +
            (assignedUnitId is not null ? $"Assigned unit: {assignedUnitId}\n" : string.Empty) +
            $"City ID: {cityId}";

        foreach (var user in users)
        {
            if (!Guid.TryParse(user.UserId, out var userId))
                continue;

            var alreadyNotified = await NotificationIdempotency.ExistsAsync(
                db,
                emergencyId,
                NotificationTypes.DepartmentCaseUpdated,
                userId,
                departmentType,
                caseStatus,
                ct);

            if (alreadyNotified)
            {
                logger.LogInformation(
                    "Skipping duplicate department.case.updated for user {UserId}, emergency {EmergencyId}, department {Department}, status {CaseStatus}",
                    userId, emergencyId, departmentType, caseStatus);
                continue;
            }

            await dispatch.SendEmailNotificationAsync(
                userId,
                cityId,
                emergencyId,
                NotificationTypes.DepartmentCaseUpdated,
                user.Email,
                subject,
                body,
                departmentType,
                caseStatus,
                ct);
        }
    }

    private static string FormatCaseEvent(string status) => status switch
    {
        "OPEN" => "opened",
        "IN_PROGRESS" => "moved in progress",
        "CLOSED" => "closed",
        _ => "updated"
    };
}
