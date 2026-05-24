using Microsoft.Extensions.Options;
using NotificationService.Features.DepartmentCaseUpdatedNotification;
using Shared.Kafka;

namespace NotificationService.Kafka.Consumers;

public sealed class DepartmentCaseUpdatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> settings,
    ILogger<DepartmentCaseUpdatedConsumer> logger)
    : KafkaConsumerBase(settings, logger)
{
    protected override string Topic => Topics.DepartmentCaseUpdated;

    protected override async Task HandleMessageAsync(string? json, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IDepartmentCaseUpdatedNotificationHandler>();
        await handler.HandleMessageAsync(json, ct);
    }
}
