namespace NotificationService.Services.Email;

public sealed class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string From { get; init; } = "noreply@emergencyhub.local";
}
