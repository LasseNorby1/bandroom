using System.Net;
using System.Text.Json;

namespace Bandroom.Api.Tests.Support;

/// <summary>Stands in for the audio worker: /sheet answers with two lines of words and a chord timeline.</summary>
public sealed class FakeWorkerHandler : HttpMessageHandler
{
    public int SheetRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/sheet"))
        {
            SheetRequests++;
            var body = new
            {
                language = "en",
                language_probability = 0.97,
                model = "fake-whisper",
                segments = new object[]
                {
                    new { start = 0.0, end = 2.0, words = new object[] { W(0.0, 0.4, "Hold"), W(0.4, 0.8, "the"), W(0.8, 1.4, "line") } },
                    new { start = 8.0, end = 10.0, words = new object[] { W(8.0, 8.5, "Till"), W(8.5, 8.8, "the"), W(8.8, 9.3, "morning"), W(9.3, 9.9, "comes") } },
                },
                chords = new object[]
                {
                    new { start = 0.0, end = 0.7, chord = "Am" }, new { start = 0.7, end = 3.0, chord = "F" },
                    new { start = 7.5, end = 9.0, chord = "C" }, new { start = 9.0, end = 11.0, chord = "G" },
                },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static object W(double start, double end, string text) => new { start, end, text };
}
