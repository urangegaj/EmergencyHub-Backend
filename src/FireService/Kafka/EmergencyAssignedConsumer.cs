using Confluent.Kafka;
using FireService.Features.EmergencyAssigned;
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
            AllowAutoCreateTopics = false,
            TopicMetadataRefreshIntervalMs = 10000
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

                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IEmergencyAssignedHandler>();
                await handler.HandleAsync(result.Message.Value, stoppingToken);
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
}
