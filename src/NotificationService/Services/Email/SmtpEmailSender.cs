using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace NotificationService.Services.Email;

public sealed class SmtpEmailSender(
    IOptions<SmtpSettings> settings,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var smtp = settings.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        try
        {
            var socketOptions = smtp.Port switch
            {
                465 => SecureSocketOptions.SslOnConnect,
                587 => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None
            };
            await client.ConnectAsync(smtp.Host, smtp.Port, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("Email sent to {Recipient} with subject {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Recipient}", to);
            throw;
        }
    }
}
