#!/usr/bin/env bash
# Nightly pg_dump, kept 14 days. Installed by deploy README as a root cron.
set -euo pipefail
cd /opt/bandroom/deploy
export COMPOSE_FILE=docker-compose.prod.yml
mkdir -p /var/backups/bandroom
docker compose exec -T postgres pg_dump -U bandroom bandroom | gzip > "/var/backups/bandroom/$(date +%F).sql.gz"
find /var/backups/bandroom -name '*.sql.gz' -mtime +14 -delete
