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
- [ ] 4 · Availability — weekly pattern (jsonb) + exceptions endpoints
- [ ] 5 · Practice finder wired to real data — proposals
- [ ] 6 · Events + RSVP + proposal flow — SignalR notifications
- [ ] 7 · ICS feed (Ical.Net) + Hangfire digests/reminders — first real "thursday works for everyone" push
