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
            if (!assignment.ClosedAt.HasValue)
            {
                assignment.ClosedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            db.ChangeTracker.Clear();
            var resolved = false;
            for (var attempt = 0; attempt < 3 && !resolved; attempt++)
            {
                var fresh = await db.Emergencies
                    .Include(e => e.Assignments)
                    .FirstOrDefaultAsync(e => e.Id == emergencyId, ct);

                if (fresh is null
                    || fresh.Status == EmergencyStatus.Resolved
                    || !fresh.Assignments.All(a => a.ClosedAt.HasValue))
                    break;

                var oldStatus = fresh.Status;
                fresh.Status = EmergencyStatus.Resolved;
                fresh.UpdatedAt = DateTime.UtcNow;
                fresh.Version++;

                db.StatusHistory.Add(new EmergencyStatusHistory
                {
                    EmergencyId = fresh.Id,
                    Status = EmergencyStatus.Resolved
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                    resolved = true;
                    await cache.InvalidateAsync($"emergencies:city:{fresh.CityId}", ct);
                    pollRegistry.Signal(fresh.Id);
                    await PublishStatusUpdatedAsync(fresh, oldStatus, EmergencyStatus.Resolved, ct);
                }
                catch (DbUpdateConcurrencyException) when (attempt < 2)
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
        else if (statusStr.Equals("IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            db.ChangeTracker.Clear();
            var transitioned = false;
            for (var attempt = 0; attempt < 3 && !transitioned; attempt++)
            {
                var fresh = await db.Emergencies
                    .FirstOrDefaultAsync(e => e.Id == emergencyId, ct);

                if (fresh is null || fresh.Status != EmergencyStatus.Dispatched)
                    break;

                fresh.Status = EmergencyStatus.InProgress;
                fresh.UpdatedAt = DateTime.UtcNow;
                fresh.Version++;

                db.StatusHistory.Add(new EmergencyStatusHistory
                {
                    EmergencyId = fresh.Id,
                    Status = EmergencyStatus.InProgress
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                    transitioned = true;
                    await cache.InvalidateAsync($"emergencies:city:{fresh.CityId}", ct);
                    pollRegistry.Signal(fresh.Id);
                    await PublishStatusUpdatedAsync(fresh, EmergencyStatus.Dispatched, EmergencyStatus.InProgress, ct);
                }
                catch (DbUpdateConcurrencyException) when (attempt < 2)
                {
                    db.ChangeTracker.Clear();
                }
            }
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
