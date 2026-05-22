using System.Text.Json;
using Confluent.Kafka;
using MedicalService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Kafka;
using DomainMedicalCaseStatus = MedicalService.Models.MedicalCaseStatus;

namespace MedicalService.Kafka;

public sealed class EmergencyAssignedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyAssignedConsumer> logger) : BackgroundService
{
    private const string DepartmentFilter = "Medical";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = settings.Value.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topics.EmergencyAssigned);

        int retryCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
                await HandleAsync(result.Message.Value, stoppingToken);
                consumer.Commit(result);
                retryCount = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                retryCount++;
                logger.LogError(ex, "Error consuming emergency.assigned (attempt {Attempt})", retryCount);
                if (retryCount >= 5)
                {
                    logger.LogError("Skipping message after {MaxRetries} consecutive failures", retryCount);
                    if (result is not null)
                        consumer.Commit(result);
                    retryCount = 0;
                }
                else
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        consumer.Close();
    }

    private async Task HandleAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("department_type", out var deptProp))
            return;
        var deptStr = deptProp.GetString() ?? "";
        if (!deptStr.Equals(DepartmentFilter, StringComparison.OrdinalIgnoreCase))
            return;

        if (!root.TryGetProperty("emergency_id", out var emergencyIdProp) ||
            !Guid.TryParse(emergencyIdProp.GetString(), out var emergencyId))
            return;

        if (!root.TryGetProperty("city_id", out var cityIdProp) ||
            !Guid.TryParse(cityIdProp.GetString(), out var cityId))
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalDbContext>();

        if (await db.Cases.AnyAsync(c => c.EmergencyId == emergencyId, ct))
        {
            logger.LogDebug(
                "Medical case already exists for emergency {EmergencyId}, skipping",
                emergencyId);
            return;
        }

        var now = DateTime.UtcNow;
        db.Cases.Add(new Models.MedicalCase
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
            "Created medical case for emergency {EmergencyId} in city {CityId}",
            emergencyId,
            cityId);
    }
}
