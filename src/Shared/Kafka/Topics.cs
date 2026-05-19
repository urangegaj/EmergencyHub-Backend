namespace Shared.Kafka;

public static class Topics
{
    public const string EmergencyCreated = "emergency.created";
    public const string EmergencyAssigned = "emergency.assigned";
    public const string EmergencyStatusUpdated = "emergency.status.updated";
    public const string DepartmentCaseUpdated = "department.case.updated";
    public const string NotificationSend = "notification.send";
    public const string CdcEmergencies = "cdc.emergencies";
}
