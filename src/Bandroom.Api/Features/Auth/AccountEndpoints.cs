using System.Security.Claims;
using System.Security.Cryptography;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NodaTime;

namespace Bandroom.Api.Features.Auth;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ChangeEmailRequest(string Password, string NewEmail);

public sealed record DeleteAccountRequest(string Password);

public sealed record ExportMembership(
    Guid BandId,
    string BandName,
    BandRole Role,
    string? Instrument,
    Instant JoinedAt,
    Instant? LeftAt,
    List<WeeklySlot> WeeklyPattern,
    IReadOnlyList<object> Blockouts,
    IReadOnlyList<object> Rsvps,
    IReadOnlyList<object> Comments,
    IReadOnlyList<object> Messages,
    IReadOnlyList<object> Uploads);

public sealed record AccountExport(
    Instant ExportedAt,
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<ExportMembership> Bands);

/// <summary>
/// The signed-in user's own account: credentials, a copy of their data and
/// the way out (spec §9, gdpr). Deleting anonymises rather than erases: the
/// band keeps the takes and comments, the person stops being identifiable.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var account = routes.MapGroup("/auth").WithTags("auth").RequireAuthorization().RequireRateLimiting("auth");

        account.MapPost("/change-password", ChangePasswordAsync)
            .WithSummary("Change password (signs out every session)");
        account.MapPost("/change-email", ChangeEmailAsync).WithSummary("Change email");
        account.MapGet("/me/export", ExportAsync).WithSummary("Everything stored about me, as json");
        account.MapDelete("/me", DeleteAsync).WithSummary("Delete my account (anonymised; bands keep their history)");

        return routes;
    }

    private static async Task<AppUser?> CurrentUserAsync(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return subject is null ? null : await users.FindByIdAsync(subject);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        TokenService tokens,
        CancellationToken ct)
    {
        var user = await CurrentUserAsync(principal, users);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var wrongCurrent = result.Errors.Any(e => e.Code == "PasswordMismatch");
            return TypedResults.ValidationProblem(wrongCurrent
                ? new Dictionary<string, string[]> { ["currentPassword"] = ["That isn't your current password."] }
                : new Dictionary<string, string[]> { ["newPassword"] = result.Errors.Select(e => e.Description).ToArray() });
        }

        // Same hygiene as a reset: every session, this one included, is signed out.
        await tokens.RevokeAllForUserAsync(user.Id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> ChangeEmailAsync(
        ChangeEmailRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users)
    {
        var user = await CurrentUserAsync(principal, users);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Wrong password."],
            });
        }

        var email = request.NewEmail?.Trim() ?? "";
        if (!email.Contains('@') || email.Length > 256)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["newEmail"] = ["Enter a valid email."] });
        }

        var taken = await users.FindByEmailAsync(email);
        if (taken is not null && taken.Id != user.Id)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["newEmail"] = ["That email is already in use."] });
        }

        var setEmail = await users.SetEmailAsync(user, email);
        var setName = await users.SetUserNameAsync(user, email);
        if (!setEmail.Succeeded || !setName.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newEmail"] = setEmail.Errors.Concat(setName.Errors).Select(e => e.Description).ToArray(),
            });
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AccountExport>, UnauthorizedHttpResult>> ExportAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var user = await CurrentUserAsync(principal, users);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var memberships = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.UserId == user.Id)
            .Join(db.Bands, m => m.BandId, b => b.Id, (m, b) => new { m, BandName = b.Name })
            .ToListAsync(ct);

        var bands = new List<ExportMembership>();
        foreach (var x in memberships)
        {
            var id = x.m.Id;
            bands.Add(new ExportMembership(
                x.m.BandId, x.BandName, x.m.Role, x.m.Instrument, x.m.JoinedAt, x.m.LeftAt, x.m.WeeklyPattern,
                await db.AvailabilityExceptions.IgnoreQueryFilters().Where(e => e.MembershipId == id)
                    .Select(e => (object)new { e.From, e.To, e.Note }).ToListAsync(ct),
                await db.Rsvps.IgnoreQueryFilters().Where(r => r.MembershipId == id)
                    .Join(db.Events.IgnoreQueryFilters(), r => r.EventId, e => e.Id, (r, e) => (object)new { e.Date, e.Type, e.Title, r.Status, r.Note, r.UpdatedAt })
                    .ToListAsync(ct),
                await db.Comments.IgnoreQueryFilters().Where(c => c.AuthorMembershipId == id)
                    .Select(c => (object)new { c.TargetType, c.TargetId, c.Body, c.AtSeconds, c.CreatedAt }).ToListAsync(ct),
                await db.Messages.IgnoreQueryFilters().Where(m => m.AuthorMembershipId == id)
                    .Select(m => (object)new { m.ChannelId, m.Body, m.CreatedAt }).ToListAsync(ct),
                await db.DemoVersions.IgnoreQueryFilters().Where(v => v.UploadedByMembershipId == id)
                    .Select(v => (object)new { v.SongIdeaId, v.Number, v.FileName, v.SizeBytes, v.DurationSeconds, v.CreatedAt }).ToListAsync(ct)));
        }

        return TypedResults.Ok(new AccountExport(clock.GetCurrentInstant(), user.Id, user.Email!, user.DisplayName, bands));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> DeleteAsync(
        [FromBody] DeleteAccountRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        AppDbContext db,
        TokenService tokens,
        IClock clock,
        CancellationToken ct)
    {
        var user = await CurrentUserAsync(principal, users);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Wrong password."] });
        }

        var now = clock.GetCurrentInstant();
        var memberships = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.UserId == user.Id && m.LeftAt == null)
            .ToListAsync(ct);
        foreach (var membership in memberships)
        {
            await MembershipLifecycle.LeaveAsync(db, membership, now, ct);
        }

        await db.SaveChangesAsync(ct);

        // Anonymise: the row stays (foreign keys from history point at it) but
        // nothing identifying does, and nobody can ever sign in as it again.
        var tombstone = $"deleted-{user.Id:N}@bandroom.invalid";
        user.DisplayName = "Deleted member";
        await users.SetEmailAsync(user, tombstone);
        await users.SetUserNameAsync(user, tombstone);
        await users.RemovePasswordAsync(user);
        await users.AddPasswordAsync(user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await tokens.RevokeAllForUserAsync(user.Id, ct);

        return TypedResults.NoContent();
    }
}
