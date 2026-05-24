using Confluent.Kafka;
using EmergencyService.Features.DepartmentCaseUpdated;
using Microsoft.Extensions.Options;

namespace EmergencyService.Kafka;

public sealed class DepartmentCaseUpdatedConsumer(
    IServiceScopeFactory scopeFactory,
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
            EnableAutoCommit = false,
            AllowAutoCreateTopics = false,
            TopicMetadataRefreshIntervalMs = 10000
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Shared.Kafka.Topics.DepartmentCaseUpdated);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IDepartmentCaseUpdatedHandler>();
                await handler.HandleAsync(result.Message.Value, stoppingToken);
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
}
