using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace AssessmentService.Kafka;

public sealed class EmergencyAssignedConsumer(
    AssignmentCache cache,
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
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Shared.Kafka.Topics.EmergencyAssigned);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);

                using var doc = JsonDocument.Parse(result.Message.Value);
                var root = doc.RootElement;

                if (!Guid.TryParse(root.GetProperty("emergency_id").GetString(), out var emergencyId))
                {
                    consumer.Commit(result);
                    continue;
                }

                var departmentType = root.GetProperty("department_type").GetString() ?? string.Empty;
                cache.Add(emergencyId, departmentType);

                consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error consuming emergency.assigned");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }
}
