using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Bandroom.Api.Features.Availability;

public static class AvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder routes)
    {
        var availability = routes.MapGroup("/bands/{bandId:guid}/availability")
            .RequireAuthorization()
            .WithTags("availability")
            .AddEndpointFilter<BandMemberFilter>();

        availability.MapGet("/", OverviewAsync).WithSummary("Everyone's pattern + exceptions (the week grid)");
        availability.MapGet("/me", MineAsync).WithSummary("My pattern, exceptions and calendar feed");
        availability.MapPut("/me/pattern", UpdatePatternAsync).WithSummary("Replace my weekly pattern");
        availability.MapPost("/me/exceptions", CreateExceptionAsync).WithSummary("Add a blockout");
        availability.MapDelete("/me/exceptions/{exceptionId:guid}", DeleteExceptionAsync).WithSummary("Remove my blockout");
        availability.MapPut("/me/calendar", SetCalendarImportAsync)
            .WithSummary("Set (or clear, with an empty url) my private calendar feed for the busy overlay");

        return routes;
    }

    private static async Task<Ok<MyAvailabilityResponse>> MineAsync(
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var membership = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        var band = bandContext.Band;
        var today = BandTime.TodayIn(band, clock.GetCurrentInstant());

        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.MembershipId == membership.Id && e.To >= today)
            .OrderBy(e => e.From)
            .Select(e => new ExceptionResponse(e.Id, e.From, e.To, e.Note))
            .ToListAsync(ct);

        return TypedResults.Ok(await MineResponseAsync(db, membership, exceptions, ct));
    }

    private static async Task<MyAvailabilityResponse> MineResponseAsync(
        AppDbContext db, Membership membership, IReadOnlyList<ExceptionResponse> exceptions, CancellationToken ct)
    {
        var busy = await db.ImportedBusyDays
            .Where(b => b.MembershipId == membership.Id)
            .OrderBy(b => b.Date)
            .Select(b => new BusyDayResponse(b.Date, b.Slot, b.Summary))
            .ToListAsync(ct);
        return new MyAvailabilityResponse(
            membership.WeeklyPattern,
            membership.PatternUpdatedAt,
            exceptions,
            $"/calendar/{membership.IcsToken}.ics",
            new CalendarImportStatus(
                membership.CalendarImportUrl, membership.CalendarImportedAt, membership.CalendarImportError, busy.Count),
            busy);
    }

    private static async Task<Results<Ok<MyAvailabilityResponse>, ValidationProblem>> SetCalendarImportAsync(
        CalendarImportRequest request,
        BandContext bandContext,
        AppDbContext db,
        CalendarImporter importer,
        CancellationToken ct)
    {
        var url = request.Url?.Trim();
        var membership = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        if (string.IsNullOrEmpty(url))
        {
            membership.CalendarImportUrl = null;
            membership.CalendarImportedAt = null;
            membership.CalendarImportError = null;
            await db.ImportedBusyDays.Where(b => b.MembershipId == membership.Id).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(await MineResponseAsync(db, membership, [], ct));
        }

        if (url.Length > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "webcal"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["url"] = ["Paste the private ics address of your calendar (https:// or webcal://)."],
            });
        }

        membership.CalendarImportUrl = uri.Scheme == "webcal" ? "https" + url[6..] : url;
        var error = await importer.ImportAsync(membership, bandContext.Band, ct);
        if (error is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["url"] = [$"Couldn't read that calendar: {error}"],
            });
        }

        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.MembershipId == membership.Id)
            .OrderBy(e => e.From)
            .Select(e => new ExceptionResponse(e.Id, e.From, e.To, e.Note))
            .ToListAsync(ct);
        return TypedResults.Ok(await MineResponseAsync(db, membership, exceptions, ct));
    }

    private static async Task<Results<Ok<MyAvailabilityResponse>, ValidationProblem>> UpdatePatternAsync(
        UpdatePatternRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        if (request.OpenSlots is null || request.OpenSlots.Count > 14)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["openSlots"] = ["Open slots are required (at most 14 entries — 7 days × 2 slots)."],
            });
        }

        var membership = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        membership.WeeklyPattern = request.OpenSlots.Distinct().ToList();
        membership.PatternUpdatedAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await MineResponseAsync(db, membership, [], ct));
    }

    private static async Task<Results<Ok<ExceptionResponse>, ValidationProblem>> CreateExceptionAsync(
        CreateExceptionRequest request,
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.To < request.From)
        {
            errors["to"] = ["'to' must be on or after 'from'."];
        }
        else if (Period.Between(request.From, request.To, PeriodUnits.Days).Days > 366)
        {
            errors["to"] = ["A blockout can span at most a year."];
        }

        if (request.Note is { Length: > 200 })
        {
            errors["note"] = ["Note must be at most 200 characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var exception = new AvailabilityException
        {
            BandId = bandContext.BandId!.Value,
            MembershipId = bandContext.MembershipId!.Value,
            From = request.From,
            To = request.To,
            Note = request.Note?.Trim(),
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.AvailabilityExceptions.Add(exception);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ExceptionResponse(exception.Id, exception.From, exception.To, exception.Note));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteExceptionAsync(
        Guid exceptionId,
        BandContext bandContext,
        AppDbContext db,
        CancellationToken ct)
    {
        // Own blockouts only; the query filter additionally pins the band.
        var deleted = await db.AvailabilityExceptions
            .Where(e => e.Id == exceptionId && e.MembershipId == bandContext.MembershipId)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<BandAvailabilityResponse>, ValidationProblem>> OverviewAsync(
        AppDbContext db,
        IClock clock,
        BandContext bandContext,
        CancellationToken ct,
        int days = 28)
    {
        if (days is < 1 or > 90)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["days"] = ["Days must be between 1 and 90."],
            });
        }

        var band = bandContext.Band;
        var from = BandTime.TodayIn(band, clock.GetCurrentInstant());
        var to = from.PlusDays(days - 1);

        var members = await db.Memberships
            .Where(m => m.LeftAt == null)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .OrderBy(x => x.m.JoinedAt)
            .Select(x => new { x.m.Id, x.u.DisplayName, x.m.WeeklyPattern, x.m.PatternUpdatedAt })
            .ToListAsync(ct);

        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.To >= from && e.From <= to)
            .OrderBy(e => e.From)
            .ToListAsync(ct);
        var busy = await db.ImportedBusyDays
            .Where(b => b.Date >= from && b.Date <= to)
            .ToListAsync(ct);

        var response = new BandAvailabilityResponse(from, days, members
            .Select(member => new MemberAvailabilityResponse(
                member.Id,
                member.DisplayName,
                member.WeeklyPattern,
                member.PatternUpdatedAt,
                exceptions
                    .Where(e => e.MembershipId == member.Id)
                    .Select(e => new ExceptionResponse(e.Id, e.From, e.To, e.Note))
                    .ToList(),
                busy
                    .Where(b => b.MembershipId == member.Id)
                    .Select(b => new BusyDayResponse(b.Date, b.Slot, b.Summary))
                    .ToList()))
            .ToList());
        return TypedResults.Ok(response);
    }
}
