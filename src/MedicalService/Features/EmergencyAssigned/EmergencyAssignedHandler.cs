using System.Text.Json;
using MedicalService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DomainMedicalCaseStatus = MedicalService.Models.MedicalCaseStatus;

namespace MedicalService.Features.EmergencyAssigned;

public class EmergencyAssignedHandler(
    MedicalDbContext db,
    ILogger<EmergencyAssignedHandler> logger) : IEmergencyAssignedHandler
{
    public async Task HandleAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Received null or empty message on EmergencyAssigned, skipping");
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("department_type", out var departmentProp))
        {
            logger.LogWarning("Message missing department_type, skipping");
            return;
        }

        var departmentType = departmentProp.GetString();
        if (!string.Equals(departmentType, "Medical", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Ignoring assignment for department {DepartmentType}", departmentType);
            return;
        }

        if (!root.TryGetProperty("emergency_id", out var emergencyIdProp)
            || !Guid.TryParse(emergencyIdProp.GetString(), out var emergencyId))
        {
            logger.LogWarning("Message missing or invalid emergency_id, skipping");
            return;
        }

        if (!root.TryGetProperty("city_id", out var cityIdProp)
            || !Guid.TryParse(cityIdProp.GetString(), out var cityId))
        {
            logger.LogWarning("Message missing or invalid city_id for emergency {EmergencyId}, skipping", emergencyId);
            return;
        }

        var exists = await db.Cases.AnyAsync(c => c.EmergencyId == emergencyId, ct);
        if (exists)
        {
            logger.LogInformation(
                "Medical case already exists for emergency {EmergencyId}, skipping (idempotent)", emergencyId);
            return;
        }

        var now = DateTime.UtcNow;
        db.Cases.Add(new MedicalService.Models.MedicalCase
        {
            Id = Guid.NewGuid(),
            EmergencyId = emergencyId,
            CityId = cityId,
            Status = DomainMedicalCaseStatus.OPEN,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created medical case for emergency {EmergencyId} in city {CityId}", emergencyId, cityId);
    }
}
