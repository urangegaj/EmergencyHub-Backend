using System.Text.Json;
using Confluent.Kafka;
using EmergencyService.Data;
using EmergencyService.Models;
using EmergencyService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Enums;
using Shared.Kafka;
using Shared.Redis;

namespace EmergencyService.Kafka;

public sealed class DepartmentCaseUpdatedConsumer(
    IServiceScopeFactory scopeFactory,
    IProducer<string, string> producer,
    PollRegistry pollRegistry,
    IRedisCache cache,
    IOptions<KafkaSettings> settings,
    ILogger<DepartmentCaseUpdatedConsumer> logger) : BackgroundService
{
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
        consumer.Subscribe(Topics.DepartmentCaseUpdated);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                await HandleAsync(result.Message.Value, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error consuming department.case.updated");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task HandleAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!Guid.TryParse(root.GetProperty("emergency_id").GetString(), out var emergencyId))
            return;

        var deptStr = root.GetProperty("department_type").GetString() ?? "";
        if (!Enum.TryParse<DepartmentType>(deptStr, ignoreCase: true, out var dept))
            return;

        var statusStr = root.GetProperty("status").GetString() ?? "";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();

        var emergency = await db.Emergencies
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.Id == emergencyId, ct);

        if (emergency is null) return;

        var assignment = emergency.Assignments.FirstOrDefault(a => a.DepartmentType == dept);
        if (assignment is null) return;

        if (statusStr.Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            assignment.ClosedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (emergency.Assignments.All(a => a.ClosedAt.HasValue))
            {
                var oldStatus = emergency.Status;
                emergency.Status = EmergencyStatus.Resolved;
                emergency.UpdatedAt = DateTime.UtcNow;
                emergency.Version++;

                db.StatusHistory.Add(new EmergencyStatusHistory
                {
                    EmergencyId = emergency.Id,
                    Status = EmergencyStatus.Resolved
                });

                await db.SaveChangesAsync(ct);
                await cache.InvalidateAsync($"emergencies:city:{emergency.CityId}", ct);
                pollRegistry.Signal(emergency.Id);

                await PublishStatusUpdatedAsync(emergency, oldStatus, EmergencyStatus.Resolved, ct);
            }
        }
        else if (statusStr.Equals("IN_PROGRESS", StringComparison.OrdinalIgnoreCase)
                 && emergency.Status == EmergencyStatus.Dispatched)
        {
            emergency.Status = EmergencyStatus.InProgress;
            emergency.UpdatedAt = DateTime.UtcNow;
            emergency.Version++;

            db.StatusHistory.Add(new EmergencyStatusHistory
            {
                EmergencyId = emergency.Id,
                Status = EmergencyStatus.InProgress
            });

            await db.SaveChangesAsync(ct);
            await cache.InvalidateAsync($"emergencies:city:{emergency.CityId}", ct);
            pollRegistry.Signal(emergency.Id);

            await PublishStatusUpdatedAsync(emergency, EmergencyStatus.Dispatched, EmergencyStatus.InProgress, ct);
        }
    }

    private async Task PublishStatusUpdatedAsync(
        Models.Emergency emergency,
        EmergencyStatus oldStatus,
        EmergencyStatus newStatus,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            emergency_id = emergency.Id.ToString(),
            city_id = emergency.CityId.ToString(),
            old_status = oldStatus.ToString(),
            new_status = newStatus.ToString(),
            updated_at = emergency.UpdatedAt.ToString("O")
        });

        await producer.ProduceAsync(
            Topics.EmergencyStatusUpdated,
            new Message<string, string> { Key = emergency.Id.ToString(), Value = payload },
            ct);
    }
}
