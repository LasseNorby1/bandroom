# Bandroom

One room for the band: a calendar that finds practice days by itself, the demos you're working on, and the talk around them. Web + iOS/Android, one backend.

**Spec (source of truth):** https://claude.ai/code/artifact/4eb5ea40-4094-499c-859b-544c3acb5d2a

## Stack

ASP.NET Core (.NET 10 LTS) modular monolith · EF Core + Postgres 17 · NodaTime for all scheduling math · SignalR for live updates · Hangfire for jobs · Cloudflare R2 for audio (presigned, never proxied) · Next.js web app and Expo mobile app speak the same OpenAPI contract.

## Layout

- `src/Bandroom.Domain` — pure domain logic, zero I/O. The practice finder lives here; it is the one piece of real IP and the most tested code in the repo.
- `src/Bandroom.Api` — the ASP.NET Core API. Feature modules as folders; EF Core entities and infrastructure live here so the domain stays pure.
- `tests/Bandroom.Domain.Tests` — table-driven finder tests, straight from fig 1 of the spec.

## Run it

```bash
docker compose up -d          # postgres 17 on localhost:5432
dotnet run --project src/Bandroom.Api
```

- API: http://localhost:5180 · OpenAPI: `/openapi/v1.json`
- Health: `/health/live` (process), `/health/ready` (includes database)

```bash
dotnet test                   # domain tests, no database needed
```

## Foundation rules

- **Tenancy at the data layer:** every band-scoped table gets a `BandId` and one EF global query filter; the cross-tenant leak test is sacred and runs on every commit.
- **Time via NodaTime everywhere:** UTC in Postgres, an IANA timezone per band, all slot math in band-local `LocalDate`/`LocalTime`. DST-week fixtures stay in the test suite permanently.
- **The finder stays pure** — no clock, no I/O, explainable answers ("4/4 free, soonest").
- **IDs are UUIDv7** (`Guid.CreateVersion7()`). Migrations are committed and applied as an explicit deploy step, never on app start against prod.
- **AI never sits in the request path** — Hangfire jobs write derived data (transcripts, bpm/key, embeddings) to their own tables, keyed by source + model version.

## Build order

- [x] 1 · Skeleton + pipeline — solution, CI, container, health endpoints, practice finder domain + tests
- [ ] 2 · Identity + JWT/refresh — register, login, refresh rotation, email plumbing
- [ ] 3 · Bands + memberships + invite links — BandContext, global query filter, the leak test
- [ ] 4 · Availability — weekly pattern (jsonb) + exceptions endpoints
- [ ] 5 · Practice finder wired to real data — proposals
- [ ] 6 · Events + RSVP + proposal flow — SignalR notifications
- [ ] 7 · ICS feed (Ical.Net) + Hangfire digests/reminders — first real "thursday works for everyone" push
