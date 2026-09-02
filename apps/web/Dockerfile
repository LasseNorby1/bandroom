# Build from the REPO ROOT: docker build -f apps/web/Dockerfile .
FROM node:22-alpine AS deps
RUN corepack enable
WORKDIR /repo
COPY pnpm-workspace.yaml package.json pnpm-lock.yaml ./
COPY apps/web/package.json apps/web/
COPY packages/client/package.json packages/client/
RUN pnpm install --frozen-lockfile --filter web --filter @bandroom/client

FROM deps AS build
COPY packages/client packages/client
COPY apps/web apps/web
# Public env is baked at build time; same-origin defaults for traefik routing.
ARG NEXT_PUBLIC_API_URL=/api
ARG NEXT_PUBLIC_HUB_URL=/hubs/band
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL \
    NEXT_PUBLIC_HUB_URL=$NEXT_PUBLIC_HUB_URL \
    NEXT_TELEMETRY_DISABLED=1
RUN pnpm --filter web build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0
COPY --from=build /repo/apps/web/.next/standalone ./
COPY --from=build /repo/apps/web/.next/static ./apps/web/.next/static
COPY --from=build /repo/apps/web/public ./apps/web/public
EXPOSE 3000
CMD ["node", "apps/web/server.js"]
