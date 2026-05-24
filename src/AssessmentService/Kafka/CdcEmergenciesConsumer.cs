using AssessmentService.Features.EmergencyCompletion;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace AssessmentService.Kafka;

public sealed class CdcEmergenciesConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<CdcEmergenciesConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = settings.Value.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            TopicMetadataRefreshIntervalMs = 10000
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(Shared.Kafka.Topics.CdcEmergencies);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IEmergencyCompletionHandler>();
                await handler.HandleAsync(result.Message.Value, stoppingToken);

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
}
