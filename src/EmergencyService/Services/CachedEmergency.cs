namespace EmergencyService.Services;

record CachedAssignment(string Id, string DepartmentType, string AssignedAt, string? ClosedAt);

record CachedEmergency(
    string Id,
    string CityId,
    string ReportedByUserId,
    string EmergencyTypeId,
    string EmergencyTypeName,
    string Description,
    string Address,
    string Status,
    int Version,
    string CreatedAt,
    string UpdatedAt,
    List<CachedAssignment> Assignments);
