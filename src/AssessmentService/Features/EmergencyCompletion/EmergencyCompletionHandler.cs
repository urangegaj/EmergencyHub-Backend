using AssessmentService.Features.Shared;

using System.Text.Json;
using AssessmentService.Data;
using AssessmentService.Kafka;
using AssessmentService.Models;
using AssessmentService.Services;
using EmergencyService.Grpc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssessmentService.Features.EmergencyCompletion;

public class EmergencyCompletionHandler(
    AssessmentDbContext db,
    AssessmentPipelineService pipeline,
    AssignmentCache assignmentCache,
    Emergency.EmergencyClient emergencyClient,
    ILogger<EmergencyCompletionHandler> logger) : IEmergencyCompletionHandler
{
    public async Task HandleAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Received null or empty CDC message, skipping");
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("op", out var opElement)) return;
        if (opElement.GetString() != "u") return;

        if (!root.TryGetProperty("after", out var after) || after.ValueKind == JsonValueKind.Null) return;
        if (!root.TryGetProperty("before", out var before) || before.ValueKind == JsonValueKind.Null) return;

        if (!after.TryGetProperty("Status", out var afterStatusProp) || !afterStatusProp.TryGetInt32(out var afterStatus))
        {
            logger.LogWarning("CDC message missing after.Status");
            return;
        }

        if (afterStatus != 3) return;

        if (!before.TryGetProperty("Status", out var beforeStatusProp) || !beforeStatusProp.TryGetInt32(out var beforeStatus))
        {
            logger.LogWarning("CDC message missing before.Status");
            return;
        }

        if (beforeStatus == 3) return;

        if (!after.TryGetProperty("Id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var emergencyId))
        {
            logger.LogWarning("CDC message missing or invalid after.Id");
            return;
        }

        if (!after.TryGetProperty("CityId", out var cityIdProp) || !Guid.TryParse(cityIdProp.GetString(), out var cityId))
        {
            logger.LogWarning("CDC message missing or invalid after.CityId");
            return;
        }

        if (!after.TryGetProperty("Description", out var descriptionProp))
        {
            logger.LogWarning("CDC message missing after.Description");
            return;
        }

        var description = descriptionProp.GetString() ?? string.Empty;

        if (!after.TryGetProperty("Address", out var addressProp))
        {
            logger.LogWarning("CDC message missing after.Address");
            return;
        }

        var address = addressProp.GetString() ?? string.Empty;

        if (!after.TryGetProperty("CreatedAt", out var createdAtProp))
        {
            logger.LogWarning("CDC message missing after.CreatedAt");
            return;
        }

        var createdAt = createdAtProp.GetDateTime();

        if (!after.TryGetProperty("UpdatedAt", out var resolvedAtProp))
        {
            logger.LogWarning("CDC message missing after.UpdatedAt");
            return;
        }

        var resolvedAt = resolvedAtProp.GetDateTime();
        var durationMinutes = (int)(resolvedAt - createdAt).TotalMinutes;

        var departments = assignmentCache.Get(emergencyId);

        if (departments is null)
        {
            logger.LogWarning("Cache miss for emergency {Id} — falling back to gRPC", emergencyId);
            var response = await emergencyClient.GetEmergencyAsync(
                new GetEmergencyRequest { EmergencyId = emergencyId.ToString(), CityId = cityId.ToString() },
                cancellationToken: ct);
            departments = response.Assignments.Select(a => a.DepartmentType).ToList();
        }

        if (await db.Reports.AnyAsync(r => r.EmergencyId == emergencyId, ct)) return;

        var payload = JsonSerializer.Serialize(new
        {
            emergency_id = emergencyId,
            city_id = cityId,
            description,
            address,
            departments_responded = departments,
            created_at = createdAt,
            resolved_at = resolvedAt,
            duration_minutes = durationMinutes
        });

        var report = new AssessmentReport
        {
            Id = Guid.NewGuid(),
            EmergencyId = emergencyId,
            CityId = cityId,
            ReportPayload = payload,
            CreatedAt = DateTime.UtcNow
        };

        var (aiResponse, lastError) = await pipeline.RunAsync(report, ct);

        report.AiResponse = aiResponse;
        report.LastError = lastError;
        report.Status = aiResponse != null ? AssessmentReportStatus.Completed : AssessmentReportStatus.Failed;
        report.SentAt = aiResponse != null ? DateTime.UtcNow : null;
        report.RetryCount = 0;

        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);
        assignmentCache.Remove(emergencyId);
    }
}
