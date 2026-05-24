using Microsoft.Extensions.Options;
using NotificationService.Features.EmergencyStatusUpdatedNotification;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class EmergencyStatusUpdatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyStatusUpdatedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.EmergencyStatusUpdated;

    protected override async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IEmergencyStatusUpdatedNotificationHandler>();
        await handler.HandleMessageAsync(json, ct);
    }
}
