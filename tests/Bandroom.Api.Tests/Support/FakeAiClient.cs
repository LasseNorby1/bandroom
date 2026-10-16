using System.Text.Json;
using Bandroom.Api.Features.Ai;

namespace Bandroom.Api.Tests.Support;

/// <summary>Canned answers; tests set <see cref="NextJson"/> / <see cref="NextText"/> before calling.</summary>
public sealed class FakeAiClient : IAiClient
{
    public bool IsConfigured { get; set; } = true;

    public string NextJson { get; set; } = "{}";

    public string NextText { get; set; } = "";

    public string? LastSystem { get; private set; }

    public string? LastUser { get; private set; }

    public Task<JsonElement> ExtractAsync(string system, string user, JsonElement schema, CancellationToken ct)
    {
        LastSystem = system;
        LastUser = user;
        using var document = JsonDocument.Parse(NextJson);
        return Task.FromResult(document.RootElement.Clone());
    }

    public Task<string> WriteAsync(string system, string user, CancellationToken ct)
    {
        LastSystem = system;
        LastUser = user;
        return Task.FromResult(NextText);
    }
}
