using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bandroom.Api.Features.Auth;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace Bandroom.Api.Tests.Support;

internal static class TestJson
{
    /// <summary>Mirrors the server's json options (NodaTime + camelCase string enums).</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

internal static class TestApiExtensions
{
    /// <summary>Registers a fresh user and returns an authenticated client.</summary>
    public static async Task<HttpClient> RegisterUserAsync(this ApiFactory factory, string displayName)
    {
        var (client, _) = await factory.RegisterUserWithTokenAsync(displayName);
        return client;
    }

    public static async Task<(HttpClient Client, string AccessToken)> RegisterUserWithTokenAsync(
        this ApiFactory factory, string displayName)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/register", new RegisterRequest(
            $"user-{Guid.NewGuid():N}@example.dk", "correct horse battery", displayName));
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>(TestJson.Options);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens!.AccessToken);
        return (client, tokens.AccessToken);
    }

    public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<T>(TestJson.Options);
        return value!;
    }
}
