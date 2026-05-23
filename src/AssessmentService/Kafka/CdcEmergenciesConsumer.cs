using System.Text.Json;
using AssessmentService.Data;
using AssessmentService.Models;
using Confluent.Kafka;
using EmergencyService.Grpc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AssessmentService.Kafka;

public sealed class CdcEmergenciesConsumer(
    AssignmentCache cache,
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<CdcEmergenciesConsumer> logger) : BackgroundService
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
        consumer.Subscribe(Shared.Kafka.Topics.CdcEmergencies);

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
                logger.LogError(ex, "Error consuming cdc.emergencies");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task HandleAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("op", out var opElement)) return;
        if (opElement.GetString() != "u") return;

        if (!root.TryGetProperty("after", out var after) || after.ValueKind == JsonValueKind.Null) return;
        if (!root.TryGetProperty("before", out var before) || before.ValueKind == JsonValueKind.Null) return;

        var afterStatus = after.GetProperty("Status").GetInt32();
        if (afterStatus != 3) return;

        var beforeStatus = before.GetProperty("Status").GetInt32();
        if (beforeStatus == 3) return;

        if (!Guid.TryParse(after.GetProperty("Id").GetString(), out var emergencyId)) return;
        if (!Guid.TryParse(after.GetProperty("CityId").GetString(), out var cityId)) return;

        var description = after.GetProperty("Description").GetString() ?? string.Empty;
        var address = after.GetProperty("Address").GetString() ?? string.Empty;
        var createdAt = after.GetProperty("CreatedAt").GetDateTime();
        var resolvedAt = after.GetProperty("UpdatedAt").GetDateTime();
        var durationMinutes = (int)(resolvedAt - createdAt).TotalMinutes;

        var departments = cache.Get(emergencyId);

        if (departments is null)
        {
            logger.LogWarning("Cache miss for emergency {Id} — falling back to gRPC", emergencyId);
            using var fallbackScope = scopeFactory.CreateScope();
            var grpcClient = fallbackScope.ServiceProvider.GetRequiredService<Emergency.EmergencyClient>();
            var response = await grpcClient.GetEmergencyAsync(
                new GetEmergencyRequest { EmergencyId = emergencyId.ToString(), CityId = cityId.ToString() },
                cancellationToken: ct);
            departments = response.Assignments.Select(a => a.DepartmentType).ToList();
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();

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

        db.Reports.Add(new AssessmentReport
        {
            Id = Guid.NewGuid(),
            EmergencyId = emergencyId,
            CityId = cityId,
            ReportPayload = payload,
            Status = AssessmentReportStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        cache.Remove(emergencyId);
    }
}
