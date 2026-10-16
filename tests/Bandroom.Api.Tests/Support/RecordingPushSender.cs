using System.Collections.Concurrent;
using Bandroom.Api.Features.Push;

namespace Bandroom.Api.Tests.Support;

public sealed record SentPush(Guid UserId, PushPayload Payload);

public sealed class RecordingPushSender : IPushSender
{
    private readonly ConcurrentQueue<SentPush> _sent = new();

    public IReadOnlyList<SentPush> Sent => [.. _sent];

    public void Send(Guid userId, PushPayload payload) => _sent.Enqueue(new SentPush(userId, payload));
}
