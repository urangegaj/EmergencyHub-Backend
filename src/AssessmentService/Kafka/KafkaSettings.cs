namespace AssessmentService.Kafka;

public sealed class KafkaSettings
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = string.Empty;
    public string GroupId { get; init; } = "assessment-service";
}
