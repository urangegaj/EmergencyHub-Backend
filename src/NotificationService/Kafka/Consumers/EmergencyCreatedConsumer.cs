using Microsoft.Extensions.Options;
using NotificationService.Features.EmergencyCreatedNotification;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class EmergencyCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyCreatedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.EmergencyCreated;

    protected override async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IEmergencyCreatedNotificationHandler>();
        await handler.HandleMessageAsync(json, ct);
    }
}
