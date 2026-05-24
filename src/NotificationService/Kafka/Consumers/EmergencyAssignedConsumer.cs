using Microsoft.Extensions.Options;
using NotificationService.Features.EmergencyAssignedNotification;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class EmergencyAssignedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<EmergencyAssignedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.EmergencyAssigned;

    protected override async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IEmergencyAssignedNotificationHandler>();
        await handler.HandleMessageAsync(json, ct);
    }
}
