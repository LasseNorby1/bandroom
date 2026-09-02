namespace Bandroom.Api.Features.Auth;

public sealed record AuthOptions
{
    public string Issuer { get; init; } = "bandroom";

    public string Audience { get; init; } = "bandroom";

    /// <summary>HMAC-SHA256 key; startup refuses anything under 32 characters.</summary>
    public string JwtSigningKey { get; init; } = "";

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 30;

    /// <summary>Where password-reset links point (the web app's public origin).</summary>
    public string WebBaseUrl { get; init; } = "http://localhost:3000";
}
