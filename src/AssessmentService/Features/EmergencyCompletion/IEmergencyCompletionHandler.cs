namespace AssessmentService.Features.EmergencyCompletion;

public interface IEmergencyCompletionHandler
{
    Task HandleAsync(string? json, CancellationToken ct);
}
