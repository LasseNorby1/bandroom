#!/usr/bin/env bash
# Deploy bandroom on the server:  bash /opt/bandroom/deploy/deploy.sh [tag]
# Pulls the images at TAG (a git sha from ci, or `latest`), runs migrations as
# an explicit step, then recreates the containers. Idempotent; safe to re-run.
set -euo pipefail

# /opt/bandroom is a git checkout of the repo; this script, the compose file
# and .env live in its deploy/ folder.
DEPLOY_DIR="${DEPLOY_DIR:-/opt/bandroom}"
cd "${DEPLOY_DIR}/deploy"
export COMPOSE_FILE=docker-compose.prod.yml

if [ ! -f .env ]; then
  cp .env.example .env
fi

# First run: mint the secrets that nobody needs to know.
fill() { grep -q "^$1=.\+" .env || sed -i "s|^$1=.*|$1=$2|" .env; }
fill DB_PASSWORD "$(openssl rand -hex 24)"
fill JWT_SIGNING_KEY "$(openssl rand -hex 48)"
fill S3_SECRET_KEY "$(openssl rand -hex 24)"

if [ "${1:-}" != "" ]; then
  sed -i "s|^TAG=.*|TAG=$1|" .env
fi
set -a; source .env; set +a
echo "==> deploying tag ${TAG}"

docker compose pull --quiet api web worker
docker compose up -d postgres minio
echo "==> migrations"
docker compose run --rm migrate
echo "==> starting"
docker compose up -d --remove-orphans

echo "==> waiting for api readiness"
for i in $(seq 1 30); do
  if docker compose exec -T api curl -fsS http://localhost:8080/health/ready >/dev/null 2>&1; then
    echo "api ready"; docker compose ps --format 'table {{.Service}}\t{{.Status}}'; exit 0
  fi
  sleep 3
done
echo "!! api never became ready"; docker compose logs --tail 50 api; exit 1
