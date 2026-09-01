using System.Security.Claims;
using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Bandroom.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/auth").WithTags("auth");

        auth.MapPost("/register", RegisterAsync).WithSummary("Create an account and sign in");
        auth.MapPost("/login", LoginAsync).WithSummary("Sign in with email + password");
        auth.MapPost("/refresh", RefreshAsync).WithSummary("Rotate a refresh token");
        auth.MapPost("/logout", LogoutAsync).WithSummary("Revoke a refresh token family");
        auth.MapGet("/me", MeAsync).RequireAuthorization().WithSummary("The signed-in user");

        return routes;
    }

    private static async Task<Results<Ok<TokenPairResponse>, ValidationProblem>> RegisterAsync(
        RegisterRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        IAppEmailSender email,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["displayName"] = ["Display name is required."],
            });
        }

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName.Trim(),
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(result.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }

        await email.SendAsync(user.Email, "Welcome to Bandroom", $"Hi {user.DisplayName} — your account is ready.", ct);

        return TypedResults.Ok(ToResponse(await tokens.IssueAsync(user, ct)));
    }

    private static async Task<Results<Ok<TokenPairResponse>, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<AppUser> userManager,
        TokenService tokens,
        CancellationToken ct)
    {
        // Every failure path is a bare 401 — no user enumeration.
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return TypedResults.Unauthorized();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return TypedResults.Unauthorized();
        }

        if (user.AccessFailedCount > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        return TypedResults.Ok(ToResponse(await tokens.IssueAsync(user, ct)));
    }

    private static async Task<Results<Ok<TokenPairResponse>, UnauthorizedHttpResult>> RefreshAsync(
        RefreshRequest request,
        TokenService tokens,
        CancellationToken ct)
    {
        var pair = await tokens.RotateAsync(request.RefreshToken, ct);
        return pair is null ? TypedResults.Unauthorized() : TypedResults.Ok(ToResponse(pair));
    }

    private static async Task<NoContent> LogoutAsync(
        RefreshRequest request,
        TokenService tokens,
        CancellationToken ct)
    {
        await tokens.RevokeAsync(request.RefreshToken, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> MeAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var user = subject is null ? null : await userManager.FindByIdAsync(subject);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new MeResponse(user.Id, user.Email!, user.DisplayName));
    }

    private static TokenPairResponse ToResponse(TokenPair pair) =>
        new(pair.AccessToken, pair.ExpiresInSeconds, pair.RefreshToken);
}
