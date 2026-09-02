using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Bandroom.Api.Data;
using Bandroom.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace Bandroom.Api.Features.Auth;

public sealed record TokenPair(string AccessToken, int ExpiresInSeconds, string RefreshToken);

public sealed class TokenService(AppDbContext db, AuthOptions options, IClock clock)
{
    public Task<TokenPair> IssueAsync(AppUser user, CancellationToken ct) =>
        IssueAsync(user, Guid.CreateVersion7(), ct);

    /// <summary>
    /// Rotate a refresh token: the presented token is atomically claimed, a fresh
    /// one is issued in the same family. Presenting a token that was already used
    /// or revoked is treated as theft — the whole family dies (returns null).
    /// </summary>
    public async Task<TokenPair?> RotateAsync(string rawRefreshToken, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var hash = TokenHashing.Sha256Hex(rawRefreshToken);

        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null)
        {
            return null;
        }

        if (token.UsedAt is not null || token.RevokedAt is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, now, ct);
            return null;
        }

        if (token.ExpiresAt <= now)
        {
            return null;
        }

        // Conditional update, not a read-then-write: two concurrent refreshes with
        // the same token race here, and exactly one may win.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.UsedAt, now), ct);
        if (claimed == 0)
        {
            await RevokeFamilyAsync(token.FamilyId, now, ct);
            return null;
        }

        var user = await db.Users.SingleAsync(u => u.Id == token.UserId, ct);
        return await IssueAsync(user, token.FamilyId, ct);
    }

    /// <summary>Password reset hygiene: every session for the user dies.</summary>
    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);
    }

    /// <summary>Logout: revoke the presented token's whole family. Idempotent.</summary>
    public async Task RevokeAsync(string rawRefreshToken, CancellationToken ct)
    {
        var hash = TokenHashing.Sha256Hex(rawRefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, clock.GetCurrentInstant(), ct);
        }
    }

    private async Task<TokenPair> IssueAsync(AppUser user, Guid familyId, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var rawRefreshToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenHashing.Sha256Hex(rawRefreshToken),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.Plus(Duration.FromDays(options.RefreshTokenDays)),
        });
        await db.SaveChangesAsync(ct);

        return new TokenPair(CreateAccessToken(user, now), options.AccessTokenMinutes * 60, rawRefreshToken);
    }

    private Task RevokeFamilyAsync(Guid familyId, Instant now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);

    private string CreateAccessToken(AppUser user, Instant now)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.ToDateTimeUtc(),
            NotBefore = now.ToDateTimeUtc(),
            Expires = now.Plus(Duration.FromMinutes(options.AccessTokenMinutes)).ToDateTimeUtc(),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email!,
                ["name"] = user.DisplayName,
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
