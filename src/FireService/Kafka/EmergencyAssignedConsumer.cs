using System.Text.Json;
using Confluent.Kafka;
using FireService.Data;
using FireService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Kafka;

namespace FireService.Kafka;

public sealed class EmergencyAssignedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyAssignedConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = settings.Value.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topics.EmergencyAssigned);
        logger.LogInformation("Subscribed to {Topic} (group {GroupId})", Topics.EmergencyAssigned, settings.Value.GroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if (result?.Message is null)
                    continue;

                await HandleMessageAsync(result.Message.Value, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                logger.LogError(ex, "Kafka consume error on {Topic}", Topics.EmergencyAssigned);
                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing {Topic} message", Topics.EmergencyAssigned);
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
        logger.LogInformation("EmergencyAssignedConsumer stopped");
    }

    private async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Received null or empty message on {Topic}, skipping", Topics.EmergencyAssigned);
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
        if (!string.Equals(departmentType, "Fire", StringComparison.OrdinalIgnoreCase))
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

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FireDbContext>();

        var exists = await db.Cases.AnyAsync(c => c.EmergencyId == emergencyId, ct);
        if (exists)
        {
            logger.LogInformation(
                "Fire case already exists for emergency {EmergencyId}, skipping (idempotent)",
                emergencyId);
            return;
        }

        var now = DateTime.UtcNow;
        db.Cases.Add(new FireCase
        {
            Id = Guid.NewGuid(),
            EmergencyId = emergencyId,
            CityId = cityId,
            Status = Models.FireCaseStatus.OPEN,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created fire case for emergency {EmergencyId} in city {CityId}",
            emergencyId,
            cityId);
    }
}
