namespace FireService.Features.EmergencyAssigned;

public interface IEmergencyAssignedHandler
{
    Task HandleAsync(string? json, CancellationToken ct);
}
