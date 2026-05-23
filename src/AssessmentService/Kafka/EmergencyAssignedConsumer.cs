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

        int retryCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);

                using var doc = JsonDocument.Parse(result.Message.Value);
                var root = doc.RootElement;

                if (!root.TryGetProperty("emergency_id", out var emergencyIdProp) ||
                    !Guid.TryParse(emergencyIdProp.GetString(), out var emergencyId))
                {
                    consumer.Commit(result);
                    retryCount = 0;
                    continue;
                }

                if (!root.TryGetProperty("department_type", out var departmentTypeProp))
                {
                    consumer.Commit(result);
                    retryCount = 0;
                    continue;
                }

                var departmentType = departmentTypeProp.GetString() ?? string.Empty;
                cache.Add(emergencyId, departmentType);

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
}
