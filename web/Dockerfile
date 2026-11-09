# Stage 1 — build
FROM node:22-alpine AS build

WORKDIR /app

# corepack provisions the pnpm version pinned in package.json "packageManager"
RUN corepack enable

# BUILD CONTEXT = REPO ROOT (not web/): vite copies the VGCA SignService plugin from
# ../other/vgca_usb_token (vite.config.ts:29), which a web/-scoped context cannot reach.
# Build with:  docker build -f web/Dockerfile <repo-root>

# Install deps first (layer cache)
COPY web/package.json web/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

# Copy source and build.
# Vite bakes VITE_* at build time. Defaults preserve today's compose behavior
# (same-origin API via the nginx proxy; OIDC authority on host-published ports).
# k8s builds pass real ingress hosts, e.g.:
#   --build-arg VITE_OIDC_AUTHORITY=http://auth.aqt.localtest.me
# Defaults match compose behavior: empty API base = same-origin '/api/v1' (api-client.ts:19
# treats '' and unset identically); OIDC default = the compose-published authserver port
# (admin-api.ts:20 fallback). An empty-string OIDC default would slip past the `??`
# fallbacks and break auth, so the real URL is the default instead.
ARG VITE_API_BASE_URL=""
ARG VITE_OIDC_AUTHORITY="http://localhost:5000"
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_OIDC_AUTHORITY=$VITE_OIDC_AUTHORITY
COPY web/ .
# vite.config.ts resolves ../other/vgca_usb_token from /app → /other
COPY other/vgca_usb_token /other/vgca_usb_token
RUN pnpm run build

# Stage 2 — serve with nginx
FROM nginx:1.27-alpine AS serve

# Copy built assets
COPY --from=build /app/dist /usr/share/nginx/html

# Copy GOV nginx config
COPY web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1/index.html || exit 1

CMD ["nginx", "-g", "daemon off;"]
