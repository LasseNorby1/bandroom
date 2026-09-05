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
docker compose up -d          # postgres (5433), minio (9000), audio worker (8090)
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
- Demos: `POST|GET /bands/{id}/song-ideas`, versions via presigned two-phase upload (`.../versions/uploads` → PUT → `.../versions/{vid}/confirm`), `GET .../versions/{vid}/stream`, stems the same way, `POST .../song-ideas/{ideaId}/polish` (ai mix & master via the worker)
- Comments: `GET|POST /bands/{id}/comments` (events + demo versions, `atSeconds` pins on the waveform)
- Songs: `POST|GET|PATCH /bands/{id}/songs`
- Chat: `GET|POST /bands/{id}/channels`, `GET|POST .../channels/{cid}/messages` (cursor-paged)
- Live updates: SignalR hub `/hubs/band` (`JoinBand`; server pushes `eventChanged`, `demoChanged`, `messageAdded`)
- Calendar: `GET /calendar/{icsToken}.ics` (per-member capability url) · Hangfire dashboard at `/hangfire` (dev-only)

```bash
dotnet test                   # domain tests run bare; api tests need docker
```

No admin rights? Everything runs user-local: .NET via `dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet`, Node 22 from the official tarball with `corepack enable` for pnpm, and Docker as a Lima VM (`limactl start template://docker` + the static docker cli, `DOCKER_HOST=unix://~/.lima/docker/sock/docker.sock`, `TESTCONTAINERS_RYUK_DISABLED=true`). Start the VM again after a reboot with `limactl start docker`.

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

Phase 1 (spec §8) is dogfood-ready end to end: pattern → finder → propose → rsvp → auto-confirm → live update → ics.

Phase 2/3 features (built pre-deploy, verified locally):

- [x] demos — song ideas → versions, presigned uploads to s3-compatible storage (minio dev / r2 prod), playback with a real decoded waveform, timestamped comments, storage accounting + per-band quota (free 1 GiB / pro 25 GiB)
- [x] comments on events and demo versions (one polymorphic table)
- [x] songs — the repertoire, linkable from demo ideas
- [x] group chat — #general per band, channels, cursor-paged history, live via the hub
- [x] ai demo polish — labelled stems in, the python worker gain-stages/pans/glues and masters, output lands as an `aiMix` demo version (pro-gated; `Entitlements:EveryBandPro` covers dev/dogfood)
- [x] reference mastering — a band-level reference library (`/bands/{id}/references`, uploads like versions) or any earlier take; matchering shapes eq/loudness/width toward the reference, standard −14 LUFS master otherwise
- [x] reference mixing — the mix stage can chase a reference of its own: per-stem gains matched to its band balance (±6 dB around the by-ear staging), pan spread to its stereo width, glue to its density; mix and master can chase different tracks
- [x] billing skeleton — band plan + quota gates; checkout via a merchant of record comes at open-up (spec p4)

Waveforms: peaks are computed once and stored on the version (`DemoVersions.Peaks`, migration `VersionPeaks`) — the uploader's browser decodes the file it already has and sends them at confirm; the worker returns them for ai mixes. The player never downloads a take just to draw it; versions without stored peaks decode lazily on first play.

UI kit: shadcn/ui is configured (`apps/web/components.json`, tokens bridged in `globals.css`) with `cn` re-exported from `@/lib/utils`. Add components with `pnpm dlx shadcn@latest add <name>` from `apps/web`; they land in `src/components/ui` already on the Bandroom palette.

Dev storage/audio notes: presigned urls are audience-aware (`Storage:WorkerEndpoint=http://minio:9000` for the worker container; browser + api use localhost). Next: pick a host (NOT the Clayton Power coolify box — own VPS or a PaaS, decision pending), then the Expo app.
