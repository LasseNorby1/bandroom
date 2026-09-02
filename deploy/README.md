# Deploying Bandroom

Target: a small VPS of its own (e.g. Hetzner CX22) running its own Coolify —
**never the Clayton Power box**. Three containers plus Traefik routing.

## Containers

| service  | image source            | port | notes                                        |
| -------- | ----------------------- | ---- | -------------------------------------------- |
| api      | `Dockerfile` (root)     | 8080 | ASP.NET Core                                 |
| web      | `apps/web/Dockerfile`   | 3000 | build context = repo root                    |
| postgres | `postgres:17-alpine`    | —    | named volume; not exposed publicly           |

## Routing (same origin — no CORS in production)

One domain, e.g. `bandroom.dk`:

- `/api/*` → **api** container, with the `/api` prefix stripped
- `/hubs/*` → **api** (SignalR websockets — enable websocket upgrade)
- `/calendar/*` → **api** (ics feeds)
- `/hangfire` → **api** (dashboard is dev-only by default; leave unrouted in prod)
- everything else → **web**

In Coolify this is per-service domain + path config; under the hood it emits the
Traefik rules. Strip-prefix only applies to `/api`.

## Environment

api:
```
ConnectionStrings__Database=Host=postgres;Port=5432;Database=bandroom;Username=bandroom;Password=<strong>
Auth__JwtSigningKey=<64 random chars — startup refuses under 32>
Auth__WebBaseUrl=https://bandroom.dk
Email__SmtpHost=smtp.resend.com        # or postmark etc — plain smtp
Email__SmtpPort=587
Email__SmtpUser=resend
Email__SmtpPassword=<api key>
Email__FromAddress=no-reply@bandroom.dk
ASPNETCORE_ENVIRONMENT=Production
```
Leave `Cors__AllowedOrigins` unset in production (same-origin makes it moot).

web (build args, not runtime): `NEXT_PUBLIC_API_URL=/api`,
`NEXT_PUBLIC_HUB_URL=/hubs/band` (the Dockerfile defaults). Runtime:
`API_URL=http://<api-internal>:8080` for the session BFF routes.

## Migrations

Applied explicitly, never on app start. From your machine through an ssh tunnel:

```bash
ssh -L 55432:localhost:5432 root@<vps>
dotnet ef database update --connection "Host=localhost;Port=55432;Database=bandroom;Username=bandroom;Password=<pw>" --project src/Bandroom.Api --startup-project src/Bandroom.Api
```

## Backups

Nightly dump, kept 14 days, on the vps (adjust paths):

```
0 3 * * * docker exec bandroom-postgres pg_dump -U bandroom bandroom | gzip > /var/backups/bandroom/$(date +\%F).sql.gz && find /var/backups/bandroom -mtime +14 -delete
```

Ship them off-box (rclone to any object storage owned by the Bandroom project —
not Clayton Power's buckets).

## Checklist

- [ ] domain + dns → vps
- [ ] coolify installed, this repo connected (github)
- [ ] postgres service + volume + strong password
- [ ] api service (root Dockerfile) + env above + healthcheck `/health/ready`
- [ ] web service (apps/web/Dockerfile, context = repo root) + `API_URL`
- [ ] routing rules incl. websocket upgrade on `/hubs`
- [ ] run migrations
- [ ] smtp creds live → register, check the welcome mail arrives
- [ ] backup cron + one restore rehearsal
