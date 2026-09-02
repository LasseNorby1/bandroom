using System.Collections.Concurrent;
using Bandroom.Api.Infrastructure.Email;

namespace Bandroom.Api.Tests.Support;

public sealed record RecordedEmail(string To, string Subject, string Body);

/// <summary>Captures outbound mail; tests filter by unique band/event names.</summary>
public sealed class RecordingEmailSender : IAppEmailSender
{
    private readonly ConcurrentQueue<RecordedEmail> _sent = new();

    public IReadOnlyList<RecordedEmail> Sent => [.. _sent];

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _sent.Enqueue(new RecordedEmail(to, subject, body));
        return Task.CompletedTask;
    }
}
