# Bandroom

One room for the band: a calendar that finds practice days by itself, the demos you're working on, and the talk around them. Web + iOS/Android, one backend.

**Spec (source of truth):** https://claude.ai/code/artifact/4eb5ea40-4094-499c-859b-544c3acb5d2a

## Stack

ASP.NET Core (.NET 10 LTS) modular monolith · EF Core + Postgres 17 · NodaTime for all scheduling math · SignalR for live updates · Hangfire for jobs · Cloudflare R2 for audio (presigned, never proxied) · Next.js web app and Expo mobile app speak the same OpenAPI contract.

## Layout

- `src/Bandroom.Domain` — pure domain logic, zero I/O. The practice finder lives here; it is the one piece of real IP and the most tested code in the repo.
- `src/Bandroom.Api` — the ASP.NET Core API. Feature modules as folders; EF Core entities and infrastructure live here so the domain stays pure.
- `tests/Bandroom.Domain.Tests` — table-driven finder tests, straight from fig 1 of the spec.
- `tests/Bandroom.Api.Tests` — integration tests through the real HTTP surface against a Testcontainers Postgres (needs Docker).

## Run it

```bash
docker compose up -d          # postgres 17 on localhost:5433 (5432 is taken on this machine)
dotnet ef database update --connection "Host=localhost;Port=5433;Database=bandroom;Username=bandroom;Password=bandroom_dev" --project src/Bandroom.Api
dotnet run --project src/Bandroom.Api
```

- API: http://localhost:5180 · OpenAPI: `/openapi/v1.json`
- Health: `/health/live` (process), `/health/ready` (includes database)
- Auth: `POST /auth/register|login|refresh|logout`, `GET /auth/me` (bearer)
- Bands: `POST|GET /bands`, `GET|PATCH /bands/{id}`, invites: `POST|GET /bands/{id}/invites`, `DELETE /bands/{id}/invites/{inviteId}`, `POST /invites/{token}/accept`
- Availability: `GET /bands/{id}/availability` (+`/me`), `PUT .../me/pattern`, `POST|DELETE .../me/exceptions`
- Finder: `GET /bands/{id}/practice-finder?days=21&slot=evening`
- Events: `POST|GET /bands/{id}/events`, `GET .../events/{eventId}`, `POST .../rsvp|confirm|cancel`
- Live updates: SignalR hub `/hubs/band` (`JoinBand`, server pushes `eventChanged`)
- Calendar: `GET /calendar/{icsToken}.ics` (per-member capability url) · Hangfire dashboard at `/hangfire` (dev-only)

```bash
dotnet test                   # domain tests run bare; api tests need docker
```

Network note: on connections where CloudFront is unreachable (docker hub + ecr blob pulls EOF), pull images via Google's mirror (`docker pull mirror.gcr.io/library/postgres:17-alpine` + `docker tag`) and run tests with `TESTCONTAINERS_RYUK_DISABLED=true`.

## Foundation rules

- **Tenancy at the data layer:** every band-scoped table gets a `BandId` and one EF global query filter; the cross-tenant leak test is sacred and runs on every commit.
- **Time via NodaTime everywhere:** UTC in Postgres, an IANA timezone per band, all slot math in band-local `LocalDate`/`LocalTime`. DST-week fixtures stay in the test suite permanently.
- **The finder stays pure** — no clock, no I/O, explainable answers ("4/4 free, soonest").
- **IDs are UUIDv7** (`Guid.CreateVersion7()`). Migrations are committed and applied as an explicit deploy step, never on app start against prod.
- **AI never sits in the request path** — Hangfire jobs write derived data (transcripts, bpm/key, embeddings) to their own tables, keyed by source + model version.

## Build order

- [x] 1 · Skeleton + pipeline — solution, CI, container, health endpoints, practice finder domain + tests
- [x] 2 · Identity + JWT/refresh — register, login (lockout), rotating refresh tokens with family reuse-detection, logout, `/auth/me`, first migration, integration tests
- [x] 3 · Bands + memberships + invite links — BandContext filter (404 for outsiders), fail-closed EF global query filters, model-completeness test, the cross-tenant leak test
- [x] 4 · Availability — jsonb weekly patterns, blockout exceptions, band overview (the week grid)
- [x] 5 · Practice finder wired to real data — ranked candidates with who's-free, quorum clamped, spacing vs last practice
- [x] 6 · Events + RSVP + quorum auto-confirm — SignalR `eventChanged` per band group
- [x] 7 · ICS feeds (Ical.Net, per-member capability urls) + Hangfire day-before reminders and weekly digests

Phase 1 backend complete. The web app is under way:

## Web app (apps/web)

pnpm workspace: `apps/web` (Next.js) + `packages/client` (types generated from the api's OpenAPI document — `pnpm gen:api` re-exports and regenerates, so contract drift is impossible to miss).

```bash
pnpm install
pnpm dev:web        # http://localhost:3000, expects the api on :5180
```

Auth (spec §6.2): the refresh token lives in an httpOnly cookie managed by the `/session/*` BFF routes; the access token lives in memory only with single-flight refresh (two concurrent refreshes would trip the api's reuse detection). Dev is cross-origin via CORS for localhost:3000; production runs same-origin behind traefik (`/api` + `/hubs`).

- [x] web 1 · scaffold, auth BFF, login/register, home with band list + create
- [x] web 2 · invites: create/QR in settings, `/invite/[token]` accept page (works logged-out via ?next=)
- [x] web 3 · availability: pattern editor (7×2 tap grid), blockouts, the band week grid
- [x] web 4 · finder → one-tap propose
- [x] web 5 · agenda home, event detail, rsvp, SignalR live invalidation (verified: an api-side rsvp flipped the open page to confirmed)
- [x] web 6 · calendar-feed section + band settings
- [x] web 7 · empty states, PWA manifest + icon, dark mode

Phase 1 (spec §8) is dogfood-ready end to end: pattern → finder → propose → rsvp → auto-confirm → live update → ics. Next: deploy to coolify, then the Expo app (phase 2).
