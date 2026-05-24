namespace EmergencyService.Features.DepartmentCaseUpdated;

public interface IDepartmentCaseUpdatedHandler
{
    Task HandleAsync(string json, CancellationToken ct);
}
