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

        return routes;
    }

    private static async Task<Ok<MyAvailabilityResponse>> MineAsync(
        BandContext bandContext,
        AppDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var membership = await db.Memberships.SingleAsync(m => m.Id == bandContext.MembershipId, ct);
        var band = await db.Bands.SingleAsync(b => b.Id == bandContext.BandId, ct);
        var today = BandTime.TodayIn(band, clock.GetCurrentInstant());

        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.MembershipId == membership.Id && e.To >= today)
            .OrderBy(e => e.From)
            .Select(e => new ExceptionResponse(e.Id, e.From, e.To, e.Note))
            .ToListAsync(ct);

        return TypedResults.Ok(new MyAvailabilityResponse(
            membership.WeeklyPattern,
            membership.PatternUpdatedAt,
            exceptions,
            $"/calendar/{membership.IcsToken}.ics"));
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

        return TypedResults.Ok(new MyAvailabilityResponse(
            membership.WeeklyPattern, membership.PatternUpdatedAt, [], $"/calendar/{membership.IcsToken}.ics"));
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

        var band = await db.Bands.SingleAsync(b => b.Id == bandContext.BandId, ct);
        var from = BandTime.TodayIn(band, clock.GetCurrentInstant());
        var to = from.PlusDays(days - 1);

        var members = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .OrderBy(x => x.m.JoinedAt)
            .Select(x => new { x.m.Id, x.u.DisplayName, x.m.WeeklyPattern, x.m.PatternUpdatedAt })
            .ToListAsync(ct);

        var exceptions = await db.AvailabilityExceptions
            .Where(e => e.To >= from && e.From <= to)
            .OrderBy(e => e.From)
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
                    .ToList()))
            .ToList());
        return TypedResults.Ok(response);
    }
}
