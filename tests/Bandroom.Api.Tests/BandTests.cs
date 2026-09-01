using System.Net;
using System.Net.Http.Json;
using Bandroom.Api.Data;
using Bandroom.Api.Features.Bands;
using Bandroom.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bandroom.Api.Tests;

[Collection("api")]
public class BandTests(ApiFactory factory)
{
    private async Task<(HttpClient Client, BandDetailResponse Band)> CreateBandAsync(
        string displayName, string bandName)
    {
        var client = await factory.RegisterUserAsync(displayName);
        var response = await client.PostAsJsonAsync("/bands", new CreateBandRequest(bandName, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (client, await response.ReadAsAsync<BandDetailResponse>());
    }

    private async Task<string> CreateInviteTokenAsync(HttpClient admin, Guid bandId, int? maxUses = null)
    {
        var response = await admin.PostAsJsonAsync(
            $"/bands/{bandId}/invites", new CreateInviteRequest(maxUses, null), TestJson.Options);
        var invite = await response.ReadAsAsync<InviteCreatedResponse>();
        return invite.Token;
    }

    // ------------------------------------------------------------------
    // The sacred one. If this test ever fails, stop everything.
    // ------------------------------------------------------------------
    [Fact]
    public async Task CrossTenant_AnotherBandDoesNotExistForOutsiders()
    {
        var (_, bandA) = await CreateBandAsync("Lasse", "Band A");
        var (outsider, bandB) = await CreateBandAsync("Mikkel", "Band B");

        // Read, update and invite-creation on band A are all 404 for B's user —
        // not 403: for outsiders the band does not exist, ids can't be probed.
        var read = await outsider.GetAsync($"/bands/{bandA.Band.Id}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var update = await outsider.PatchAsJsonAsync(
            $"/bands/{bandA.Band.Id}", new UpdateBandRequest("Hijacked", null, null, null, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var invite = await outsider.PostAsJsonAsync(
            $"/bands/{bandA.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, invite.StatusCode);

        // And the outsider's own list contains only their band.
        var mine = await (await outsider.GetAsync("/bands")).ReadAsAsync<List<BandSummaryResponse>>();
        var only = Assert.Single(mine);
        Assert.Equal(bandB.Band.Id, only.Id);
    }

    [Fact]
    public void EveryBandScopedEntityHasAQueryFilter()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var missing = db.Model.GetEntityTypes()
            .Where(entity => typeof(IBandScoped).IsAssignableFrom(entity.ClrType))
            .Where(entity => entity.GetDeclaredQueryFilters().Count == 0)
            .Select(entity => entity.ClrType.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public async Task CreateBand_MakesTheCreatorAdmin()
    {
        var (client, created) = await CreateBandAsync("Lasse", "Øvelokalet");

        Assert.Equal("Øvelokalet", created.Band.Name);
        Assert.Equal("Europe/Copenhagen", created.Band.TimeZone);
        var creator = Assert.Single(created.Members);
        Assert.Equal(BandRole.Admin, creator.Role);

        var detail = await (await client.GetAsync($"/bands/{created.Band.Id}")).ReadAsAsync<BandDetailResponse>();
        Assert.Equal(BandRole.Admin, Assert.Single(detail.Members).Role);
    }

    [Fact]
    public async Task InviteFlow_JoinsTheBand_AndIsIdempotent()
    {
        var (admin, band) = await CreateBandAsync("Lasse", "Band A");
        var token = await CreateInviteTokenAsync(admin, band.Band.Id);

        var joiner = await factory.RegisterUserAsync("Sofie");
        var accept = await (await joiner.PostAsync($"/invites/{token}/accept", null))
            .ReadAsAsync<AcceptInviteResponse>();
        Assert.Equal(band.Band.Id, accept.BandId);

        // The band now exists for the joiner, with two members.
        var detail = await (await joiner.GetAsync($"/bands/{band.Band.Id}")).ReadAsAsync<BandDetailResponse>();
        Assert.Equal(2, detail.Members.Count);

        // Accepting again returns the same membership, no duplicate.
        var again = await (await joiner.PostAsync($"/invites/{token}/accept", null))
            .ReadAsAsync<AcceptInviteResponse>();
        Assert.Equal(accept.MembershipId, again.MembershipId);
    }

    [Fact]
    public async Task Invites_AreAdminOnly()
    {
        var (admin, band) = await CreateBandAsync("Lasse", "Band A");
        var token = await CreateInviteTokenAsync(admin, band.Band.Id);

        var member = await factory.RegisterUserAsync("Sofie");
        await member.PostAsync($"/invites/{token}/accept", null);

        // A plain member is inside the band (so not 404) but not admin → 403.
        var attempt = await member.PostAsJsonAsync(
            $"/bands/{band.Band.Id}/invites", new CreateInviteRequest(null, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }

    [Fact]
    public async Task RevokedInvite_CannotBeUsed()
    {
        var (admin, band) = await CreateBandAsync("Lasse", "Band A");
        var token = await CreateInviteTokenAsync(admin, band.Band.Id);

        var invites = await (await admin.GetAsync($"/bands/{band.Band.Id}/invites"))
            .ReadAsAsync<List<InviteResponse>>();
        var revoke = await admin.DeleteAsync($"/bands/{band.Band.Id}/invites/{Assert.Single(invites).Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var joiner = await factory.RegisterUserAsync("Sofie");
        var accept = await joiner.PostAsync($"/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact]
    public async Task ExhaustedInvite_CannotBeUsed()
    {
        var (admin, band) = await CreateBandAsync("Lasse", "Band A");
        var token = await CreateInviteTokenAsync(admin, band.Band.Id, maxUses: 1);

        var first = await factory.RegisterUserAsync("Sofie");
        var ok = await first.PostAsync($"/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var second = await factory.RegisterUserAsync("Jonas");
        var exhausted = await second.PostAsync($"/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
    }

    [Fact]
    public async Task UnknownInviteToken_Is404()
    {
        var client = await factory.RegisterUserAsync("Lasse");
        var response = await client.PostAsync("/invites/not-a-real-token/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBand_AppliesSettings_AndValidates()
    {
        var (admin, band) = await CreateBandAsync("Lasse", "Band A");

        var updated = await (await admin.PatchAsJsonAsync(
                $"/bands/{band.Band.Id}",
                new UpdateBandRequest("Silverline", null, 3, null, "Møllegade 3"), TestJson.Options))
            .ReadAsAsync<BandResponse>();
        Assert.Equal("Silverline", updated.Name);
        Assert.Equal(3, updated.Quorum);
        Assert.Equal("Møllegade 3", updated.RehearsalSpace);

        var badTimeZone = await admin.PatchAsJsonAsync(
            $"/bands/{band.Band.Id}",
            new UpdateBandRequest(null, "Mars/Olympus_Mons", null, null, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, badTimeZone.StatusCode);
    }

    [Fact]
    public async Task UpdateBand_MemberIsForbidden()
    {
        var (admin, band) = await CreateBandAsync("Lasse", "Band A");
        var token = await CreateInviteTokenAsync(admin, band.Band.Id);
        var member = await factory.RegisterUserAsync("Sofie");
        await member.PostAsync($"/invites/{token}/accept", null);

        var attempt = await member.PatchAsJsonAsync(
            $"/bands/{band.Band.Id}", new UpdateBandRequest("Nope", null, null, null, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }
}
