using System.Collections.Concurrent;
using System.Net;

namespace Bandroom.Api.Tests.Support;

/// <summary>Stands in for the browsers' push services: 201 for everything, 410 for endpoints containing "gone".</summary>
public sealed class FakePushServiceHandler : HttpMessageHandler
{
    public sealed record Delivery(Uri Endpoint, string? Authorization, string? Encoding, string? Topic, int BodyLength);

    private readonly ConcurrentQueue<Delivery> _deliveries = new();

    public IReadOnlyList<Delivery> Deliveries => [.. _deliveries];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        _deliveries.Enqueue(new Delivery(
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentEncoding.FirstOrDefault(),
            request.Headers.TryGetValues("Topic", out var topics) ? topics.FirstOrDefault() : null,
            body.Length));
        return request.RequestUri!.AbsolutePath.Contains("gone")
            ? new HttpResponseMessage(HttpStatusCode.Gone)
            : new HttpResponseMessage(HttpStatusCode.Created);
    }
}
