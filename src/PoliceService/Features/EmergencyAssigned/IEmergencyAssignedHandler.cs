namespace PoliceService.Features.EmergencyAssigned;

public interface IEmergencyAssignedHandler
{
    Task HandleAsync(string? json, CancellationToken ct);
}
