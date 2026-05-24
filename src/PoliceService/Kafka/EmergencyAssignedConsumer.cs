using Confluent.Kafka;
using Microsoft.Extensions.Options;
using PoliceService.Features.EmergencyAssigned;
using Shared.Kafka;

namespace PoliceService.Kafka;

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
            AllowAutoCreateTopics = false,
            TopicMetadataRefreshIntervalMs = 10000
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

                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IEmergencyAssignedHandler>();
                await handler.HandleAsync(result.Message.Value, stoppingToken);

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
