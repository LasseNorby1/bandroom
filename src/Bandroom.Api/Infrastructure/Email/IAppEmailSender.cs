namespace Bandroom.Api.Infrastructure.Email;

public interface IAppEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

/// <summary>Dev sink — real SMTP lands with the digest work (build order step 7).</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IAppEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("email (dev sink) to {To}: {Subject} — {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
