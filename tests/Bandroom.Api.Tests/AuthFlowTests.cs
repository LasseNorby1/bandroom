using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Bandroom.Api.Features.Auth;
using Bandroom.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class AuthFlowTests(ApiFactory factory)
{
    private static RegisterRequest NewUser() =>
        new($"user-{Guid.NewGuid():N}@example.dk", "correct horse battery", "Lasse");

    private async Task<(HttpClient Client, RegisterRequest User, TokenPairResponse Tokens)> RegisterAsync()
    {
        var client = factory.CreateClient();
        var user = NewUser();
        var response = await client.PostAsJsonAsync("/auth/register", user);
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>();
        Assert.NotNull(tokens);
        return (client, user, tokens);
    }

    [Fact]
    public async Task RegisterLoginAndMe_RoundTrips()
    {
        var (client, user, _) = await RegisterAsync();

        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, user.Password));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<TokenPairResponse>();
        Assert.NotNull(tokens);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await client.GetFromJsonAsync<MeResponse>("/auth/me");
        Assert.NotNull(me);
        Assert.Equal(user.Email, me.Email);
        Assert.Equal(user.DisplayName, me.DisplayName);
    }

    [Fact]
    public async Task Me_WithoutToken_Is401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheToken()
    {
        var (client, _, original) = await RegisterAsync();

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(original.RefreshToken));
        refresh.EnsureSuccessStatusCode();
        var rotated = await refresh.Content.ReadFromJsonAsync<TokenPairResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);
    }

    [Fact]
    public async Task ReusingARotatedToken_KillsTheWholeFamily()
    {
        var (client, _, original) = await RegisterAsync();

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(original.RefreshToken));
        refresh.EnsureSuccessStatusCode();
        var rotated = await refresh.Content.ReadFromJsonAsync<TokenPairResponse>();
        Assert.NotNull(rotated);

        // Replay of the already-used token → theft signal → 401 …
        var replay = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(original.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // … and the legitimate successor must be dead too.
        var successor = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheFamily()
    {
        var (client, _, tokens) = await RegisterAsync();

        var logout = await client.PostAsJsonAsync("/auth/logout", new RefreshRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Is401()
    {
        var (client, user, _) = await RegisterAsync();

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, "not the password"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_LocksOutAfterFiveFailures()
    {
        var (client, user, _) = await RegisterAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, "not the password"));
        }

        // Even the correct password is refused while locked out.
        var lockedOut = await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, user.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);
    }

    [Fact]
    public async Task Register_WithShortPassword_IsAValidationProblem()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/register", new RegisterRequest($"short-{Guid.NewGuid():N}@example.dk", "short", "Lasse"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_FlowsEndToEnd_AndKillsOldSessions()
    {
        var client = factory.CreateClient();
        var user = NewUser();
        var register = await client.PostAsJsonAsync("/auth/register", user);
        var tokens = await register.Content.ReadFromJsonAsync<TokenPairResponse>();

        var forgot = await client.PostAsJsonAsync("/auth/forgot-password", new ForgotPasswordRequest(user.Email));
        Assert.Equal(HttpStatusCode.NoContent, forgot.StatusCode);

        var recorder = factory.Services.GetRequiredService<RecordingEmailSender>();
        var mail = recorder.Sent.Last(m => m.To == user.Email && m.Subject.Contains("Reset"));
        var token = Uri.UnescapeDataString(Regex.Match(mail.Body, @"token=([^&\s]+)").Groups[1].Value);

        var reset = await client.PostAsJsonAsync(
            "/auth/reset-password", new ResetPasswordRequest(user.Email, token, "brand new passphrase"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Old password and old refresh token are both dead; the new password works.
        var oldLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, user.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var oldRefresh = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokens!.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        var newLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest(user.Email, "brand new passphrase"));
        newLogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_IsStill204()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/forgot-password", new ForgotPasswordRequest($"ghost-{Guid.NewGuid():N}@example.dk"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_IsAValidationProblem()
    {
        var (client, user, _) = await RegisterAsync();

        var duplicate = await client.PostAsJsonAsync(
            "/auth/register", new RegisterRequest(user.Email, "correct horse battery", "Impostor"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }
}
