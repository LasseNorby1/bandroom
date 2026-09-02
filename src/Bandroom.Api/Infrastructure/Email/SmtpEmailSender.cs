using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Bandroom.Api.Infrastructure.Email;

public sealed record EmailOptions
{
    public string? SmtpHost { get; init; }

    public int SmtpPort { get; init; } = 587;

    public string? SmtpUser { get; init; }

    public string? SmtpPassword { get; init; }

    public string FromAddress { get; init; } = "no-reply@bandroom.local";

    public string FromName { get; init; } = "Bandroom";
}

/// <summary>
/// Plain smtp so any provider works (resend/postmark/etc. all speak it). A send
/// failure is logged and swallowed: these are nudges — one bad address must not
/// abort a digest run or double-send reminders later.
/// </summary>
public sealed class SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger) : IAppEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            // Registration is gated on SmtpHost being configured (Program.cs).
            var host = options.SmtpHost ?? throw new InvalidOperationException("Email:SmtpHost is not configured.");

            using var client = new SmtpClient();
            await client.ConnectAsync(host, options.SmtpPort, SecureSocketOptions.StartTlsWhenAvailable, ct);
            if (!string.IsNullOrEmpty(options.SmtpUser))
            {
                await client.AuthenticateAsync(options.SmtpUser, options.SmtpPassword ?? "", ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "smtp send to {To} failed ({Subject})", to, subject);
        }
    }
}
