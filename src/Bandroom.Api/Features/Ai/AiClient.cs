using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace Bandroom.Api.Features.Ai;

public sealed record AiOptions
{
    /// <summary>Unset = every ai feature answers "not available on this server".</summary>
    public string? ApiKey { get; init; }

    public string Model { get; init; } = "claude-opus-5-5";
}

/// <summary>
/// The one seam to the model. Two shapes cover every feature: "fill this
/// json schema from this text" (extraction) and "write this" (prose). Kept
/// deliberately thin so tests swap in canned answers and nothing else here
/// knows which model is behind it.
/// </summary>
public interface IAiClient
{
    bool IsConfigured { get; }

    Task<JsonElement> ExtractAsync(string system, string user, JsonElement schema, CancellationToken ct);

    Task<string> WriteAsync(string system, string user, CancellationToken ct);
}

public sealed class AiRefusedException(string? category) : Exception($"The model declined ({category ?? "policy"}).")
{
    public string? Category { get; } = category;
}

public sealed class AnthropicAiClient(AiOptions options) : IAiClient
{
    private readonly AnthropicClient? _client = string.IsNullOrEmpty(options.ApiKey)
        ? null
        : new AnthropicClient { ApiKey = options.ApiKey };

    public bool IsConfigured => _client is not null;

    public async Task<JsonElement> ExtractAsync(string system, string user, JsonElement schema, CancellationToken ct)
    {
        var text = await CallAsync(system, user, schema, ct);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public Task<string> WriteAsync(string system, string user, CancellationToken ct) => CallAsync(system, user, null, ct);

    private async Task<string> CallAsync(string system, string user, JsonElement? schema, CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("Ai is not configured.");
        // Extraction and short prose: no need for deep thinking, keep it fast and cheap.
        // With a schema, structured outputs guarantee the json shape (no prefill —
        // not supported on this model family).
        var outputConfig = new OutputConfig { Effort = Effort.Low };
        if (schema is { } s)
        {
            outputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat
                {
                    Schema = s.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                },
            };
        }

        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = 4096,
            System = system,
            Messages = [new() { Role = Role.User, Content = user }],
            OutputConfig = outputConfig,
        };

        var response = await client.Messages.Create(parameters, cancellationToken: ct);
        if (response.StopReason == "refusal")
        {
            throw new AiRefusedException(response.StopDetails?.Category?.ToString());
        }

        return string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }
}
