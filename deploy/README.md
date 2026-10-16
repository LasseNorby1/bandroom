# Deploying Bandroom

Lives on `tolk-1` (Hetzner CX32, Helsinki, 4 vCPU / 8 GB) next to the Tolk
demo. Coolify runs that box; its traefik (`coolify-proxy`) is the edge and
routes any container on the `coolify` docker network with traefik labels, so
bandroom is a plain compose stack: `/opt/bandroom` is a git checkout of this
repo and everything runs from its `deploy/` folder — nothing is clicked in the
Coolify ui. **Never the Clayton Power box.**

| service  | image (ghcr.io/lassenorby1/bandroom/…) | host                 |
| -------- | -------------------------------------- | -------------------- |
| web      | `web`                                  | bandroom.io (+www)   |
| api      | `api`                                  | api.bandroom.io      |
| minio    | minio/minio                            | files.bandroom.io    |
| worker   | `worker`                               | internal only        |
| postgres | postgres:17-alpine                     | internal only        |

Web and api are different origins; the api allows `https://bandroom.io` via
CORS and the session cookie lives on the web origin (the BFF). MinIO is public
because browsers upload and stream straight to presigned urls. R2 later is a
config change (`Storage__*`).

## Pipeline

Push to `main` → `.github/workflows/deploy.yml` builds api, web and worker and
pushes them to ghcr at `:latest` and `:<sha>`. With `DEPLOY_SSH_KEY` and
`DEPLOY_HOST` set as repo secrets it then ssh's in and runs `deploy.sh <sha>`.
Without them, deploy by hand:

```bash
ssh -i ~/.ssh/tolk_hetzner root@62.238.53.162 'cd /opt/bandroom && git pull --ff-only && bash deploy/deploy.sh <sha>'
```

`deploy.sh` pulls the pinned tag, starts postgres + minio, runs migrations as a
one-shot container (`dotnet Bandroom.Api.dll --migrate`), then recreates the
stack and waits for `/health/ready`. Secrets are minted into `.env` on first run;
the Resend key is the one thing a human pastes in (`SMTP_PASSWORD`).

## First-time server setup (done 2026-10-16)

```bash
git clone https://github.com/LasseNorby1/bandroom.git /opt/bandroom
bash /opt/bandroom/deploy/deploy.sh            # mints .env secrets, pulls :latest, migrates, starts
(crontab -l 2>/dev/null; echo '0 3 * * * bash /opt/bandroom/deploy/backup.sh') | crontab -
```

DNS: A records for `bandroom.io`, `www`, `api`, `files` → 62.238.53.162.
Traefik gets Let's Encrypt certs on first request once DNS resolves.

## Rollback

```bash
bash /opt/bandroom/deploy/deploy.sh <previous sha>
```

Images reverse; migrations don't. Backups: nightly dump in
`/var/backups/bandroom`, 14 days (`backup.sh`, root cron 03:00). Restore rehearsal: pipe a dump into
`docker compose exec -T postgres psql -U bandroom bandroom`.
