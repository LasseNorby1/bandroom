using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NodaTime;

namespace Bandroom.Api.Features.Bands;

public static class BandEndpoints
{
    public static IEndpointRouteBuilder MapBandEndpoints(this IEndpointRouteBuilder routes)
    {
        var bands = routes.MapGroup("/bands").RequireAuthorization().WithTags("bands");

        bands.MapPost("/", CreateAsync).WithSummary("Create a band (creator becomes admin)");
        bands.MapGet("/", ListMineAsync).WithSummary("The caller's bands");

        // Everything under /bands/{bandId} runs behind the membership gate.
        var band = bands.MapGroup("/{bandId:guid}").AddEndpointFilter<BandMemberFilter>();
        band.MapGet("/", DetailAsync).WithSummary("Band detail + members");
        band.MapPatch("/", UpdateAsync)
            .AddEndpointFilter<RequireBandAdminFilter>()
            .WithSummary("Update band settings (admin)");
        band.MapPut("/notice", UpdateNoticeAsync)
            .AddEndpointFilter<RequireBandAdminFilter>()
            .WithSummary("Replace the pinned notice (admin); empty clears it");

        return routes;
    }

    private static async Task<Results<Created<BandDetailResponse>, ValidationProblem, UnauthorizedHttpResult>> CreateAsync(
        CreateBandRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            errors["name"] = ["Band name is required (max 100 characters)."];
        }

        var timeZone = request.TimeZone ?? "Europe/Copenhagen";
        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone) is null)
        {
            errors["timeZone"] = [$"Unknown IANA timezone '{timeZone}'."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = clock.GetCurrentInstant();
        var band = new Band { Name = name!, TimeZone = timeZone, CreatedAt = now };
        var membership = new Membership
        {
            BandId = band.Id,
            UserId = userId,
            Role = BandRole.Admin,
            JoinedAt = now,
            IcsToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24)),
        };

        db.Bands.Add(band);
        db.Memberships.Add(membership);
        db.Channels.Add(new Channel
        {
            BandId = band.Id,
            Name = "general",
            IsDefault = true,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        var creator = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var response = new BandDetailResponse(
            ToBandResponse(band, now, entitlements),
            [new MemberResponse(membership.Id, userId, creator.DisplayName, BandRole.Admin, null, now, now)]);
        return TypedResults.Created($"/bands/{band.Id}", response);
    }

    private static async Task<Results<Ok<List<BandSummaryResponse>>, UnauthorizedHttpResult>> ListMineAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        // Deliberately cross-band: my memberships across all bands.
        var summaries = await db.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.LeftAt == null)
            .Join(db.Bands, m => m.BandId, b => b.Id, (m, b) => new { m, b })
            .OrderBy(x => x.b.Name)
            .Select(x => new BandSummaryResponse(x.b.Id, x.b.Name, x.m.Role))
            .ToListAsync(ct);
        return TypedResults.Ok(summaries);
    }

    private static async Task<Ok<BandDetailResponse>> DetailAsync(
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var band = bandContext.Band;

        // No explicit BandId clause — the global query filter supplies it.
        var members = await db.Memberships
            .Where(m => m.LeftAt == null)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .OrderBy(x => x.m.JoinedAt)
            .Select(x => new MemberResponse(x.m.Id, x.u.Id, x.u.DisplayName, x.m.Role, x.m.Instrument, x.m.JoinedAt, x.m.LastActiveAt))
            .ToListAsync(ct);

        return TypedResults.Ok(new BandDetailResponse(
            ToBandResponse(band, clock.GetCurrentInstant(), entitlements), members));
    }

    private static async Task<Results<Ok<BandResponse>, ValidationProblem>> UpdateAsync(
        UpdateBandRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var name = request.Name?.Trim();
        if (name is not null && (name.Length == 0 || name.Length > 100))
        {
            errors["name"] = ["Band name must be 1–100 characters."];
        }

        if (request.TimeZone is not null && DateTimeZoneProviders.Tzdb.GetZoneOrNull(request.TimeZone) is null)
        {
            errors["timeZone"] = [$"Unknown IANA timezone '{request.TimeZone}'."];
        }

        if (request.Quorum is < 1 or > 50)
        {
            errors["quorum"] = ["Quorum must be between 1 and 50."];
        }

        if (request.RehearsalSpace is { Length: > 200 })
        {
            errors["rehearsalSpace"] = ["Rehearsal space must be at most 200 characters."];
        }

        if (request.ReminderHour is < 0 or > 23)
        {
            errors["reminderHour"] = ["Reminder hour must be between 0 and 23."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var band = bandContext.Band;
        if (name is not null)
        {
            band.Name = name;
        }

        if (request.TimeZone is not null)
        {
            band.TimeZone = request.TimeZone;
        }

        if (request.Quorum is { } quorum)
        {
            band.Quorum = quorum;
        }

        if (request.DefaultPracticeSlot is { } slot)
        {
            band.DefaultPracticeSlot = slot;
        }

        if (request.RehearsalSpace is not null)
        {
            band.RehearsalSpace = request.RehearsalSpace.Trim() is { Length: 0 } ? null : request.RehearsalSpace.Trim();
        }

        if (request.ReminderHour is { } reminderHour)
        {
            band.ReminderHour = reminderHour;
        }

        if (request.AiEnabled is { } aiEnabled)
        {
            band.AiEnabled = aiEnabled;
        }

        if (request.LyricsLanguage is not null)
        {
            var language = request.LyricsLanguage.Trim().ToLowerInvariant();
            band.LyricsLanguage = language.Length is 0 ? null : language.Length <= 8 ? language : language[..8];
        }

        if (request.ClearRecurringPracticeDay)
        {
            band.RecurringPracticeDay = null;
        }
        else if (request.RecurringPracticeDay is { } day && day != IsoDayOfWeek.None)
        {
            band.RecurringPracticeDay = day;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToBandResponse(band, clock.GetCurrentInstant(), entitlements));
    }

    private static async Task<Results<Ok<BandResponse>, ValidationProblem>> UpdateNoticeAsync(
        UpdateNoticeRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        EntitlementOptions entitlements,
        CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (text is { Length: > 2000 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["text"] = ["The notice must be at most 2000 characters."],
            });
        }

        var band = bandContext.Band;
        var now = clock.GetCurrentInstant();
        band.Notice = string.IsNullOrEmpty(text) ? null : text;
        band.NoticeUpdatedAt = band.Notice is null ? null : now;
        band.NoticeByMembershipId = band.Notice is null ? null : bandContext.MembershipId;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToBandResponse(band, now, entitlements));
    }

    /// <summary>Set once at startup from AiOptions; a static because ToBandResponse is called from several handlers.</summary>
    public static bool AiAvailable { get; set; }

    private static BandResponse ToBandResponse(Band band, Instant now, EntitlementOptions entitlements) =>
        new(band.Id, band.Name, band.TimeZone, band.Quorum, band.DefaultPracticeSlot, band.RehearsalSpace,
            band.ReminderHour, band.RecurringPracticeDay, band.Notice, band.NoticeUpdatedAt,
            band.AiEnabled, AiAvailable, band.LyricsLanguage,
            PlanLimits.IsPro(band, now, entitlements) ? BandPlan.Pro : BandPlan.Free,
            band.StorageUsedBytes,
            PlanLimits.StorageQuota(band, now, entitlements));
}
