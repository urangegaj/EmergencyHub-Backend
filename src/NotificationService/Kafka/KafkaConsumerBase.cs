using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace NotificationService.Kafka;

public abstract class KafkaConsumerBase(
    IOptions<KafkaSettings> settings,
    ILogger logger) : BackgroundService
{
    protected abstract string Topic { get; }

    protected abstract Task HandleMessageAsync(string? json, CancellationToken ct);

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
        consumer.Subscribe(Topic);
        logger.LogInformation("Subscribed to {Topic} (group {GroupId})", Topic, settings.Value.GroupId);

        int retryCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);

                if (result?.Message is null)
                    continue;

                await HandleMessageAsync(result.Message.Value, stoppingToken);
                consumer.Commit(result);
                retryCount = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                retryCount++;
                logger.LogError(ex, "Error consuming {Topic} (attempt {Attempt})", Topic, retryCount);
                if (retryCount >= 5)
                {
                    logger.LogError("Skipping message on {Topic} after {MaxRetries} consecutive failures", Topic, retryCount);
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
        logger.LogInformation("Stopped consumer for {Topic}", Topic);
    }
}
