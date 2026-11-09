# ADR-008: Ingress Same-Origin Reverse Proxy for Multi-Tenant FE Subdomains

**Date:** 2026-05-09
**Status:** Proposed (deferred — triggered by multi-tenant FE rollout)
**Deciders:** AqtCoreFW Backend + DevOps + FE leads

## Context

Current dev/staging/prod topology splits FE and API onto separate hosts:

```
FE   → https://{tenant}-fe-{env}.aequitas.dev   (static, served by FE service)
API  → https://api-{env}.aequitas.dev           (ASP.NET, served by api service)
Auth → https://auth-{env}.aequitas.dev          (OpenIddict AuthServer)
```

The FE issues cross-origin XHRs to `/health/ready`, `/api/v1/...`, `/openapi/v1.json`, and SignalR `/hubs/*` on the API host. This requires:

1. **Absolute URLs in FE code** — every `axios.get`/`fetch` must prepend `VITE_API_BASE_URL`. Relative paths only work in local dev because Vite proxy intercepts them. Drift caught the dashboard health widget on dev domain `v26grdnb63-fe-dev.aequitas.dev` (incident 2026-05-09, fixed in [project-changelog.md](../project-changelog.md) v2.137-PB).
2. **CORS allowlist on API** — every tenant FE host must be added to `Cors:AllowedOrigins`. With multi-tenant subdomain pattern `{tenant}-fe-{env}.aequitas.dev`, the list grows linearly and must be updated on each tenant onboarding.
3. **Cookie SameSite friction** — cross-site cookies for OIDC + session require `SameSite=None; Secure`, which the current AuthServer setup intentionally avoids.

This ADR records an alternative deferred until tenant count justifies the refactor.

## Decision (Conditional)

**When ≥3 active tenant FE subdomains share a common API**, switch the Ingress topology so each FE host **reverse-proxies API traffic to the API service** under specific path prefixes. The FE app then talks to the API via *same-origin relative paths* — no CORS, no per-tenant allowlist updates, no `VITE_API_BASE_URL` env injection.

### Topology

```
Single FE host (v26grdnb63-fe-dev.aequitas.dev) routes by path:
  /api/*       → api service       (REST)
  /health/*    → api service       (middleware health endpoints)
  /openapi/*   → api service       (OpenAPI spec)
  /hubs/*      → api service       (SignalR WebSocket — needs upgrade headers)
  /            → fe service        (static SPA — catch-all last)
```

NGINX matches **most-specific-prefix-first**, so `/` doesn't shadow `/api/*`.

### Helm template snippet

Per-tenant FE host block in `values-staging.yaml` / `values-prod.yaml`:

```yaml
ingress:
  hosts:
    - host: v26grdnb63-fe-dev.aequitas.dev
      paths:
        - path: /api
          pathType: Prefix
          service: aqtcorefw-api          # ← targets API service
        - path: /health
          pathType: Prefix
          service: aqtcorefw-api
        - path: /openapi
          pathType: Prefix
          service: aqtcorefw-api
        - path: /hubs
          pathType: Prefix
          service: aqtcorefw-api
        - path: /
          pathType: Prefix
          service: aqtcorefw-fe           # ← static SPA fallback
```

The `templates/ingress.yaml` template must be extended to honor a per-path `service:` override (current template hard-codes `{{ include "aqtcorefw.fullname" $ }}`).

For SignalR `/hubs/*`, NGINX needs WebSocket annotations:

```yaml
ingress:
  annotations:
    nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "3600"
    nginx.ingress.kubernetes.io/upstream-hash-by: "$request_uri"
```

### FE code revert

Once same-origin is active, the absolute-URL pattern in FE becomes inert (relative paths work because Vite proxy mirrors prod ingress). Revert:

- `web/src/features/dashboard-overview/admin-dashboard-api.ts` — drop `${apiBase}` prefix on `/health/ready`.
- `web/src/features/admin/admin-api.ts` — drop `${API_BASE}` prefix on `/api/v1/...` and `/openapi/v1.json`. AuthServer URL stays absolute (different origin).
- `web/src/core/api/api-client.ts` — `VITE_API_BASE_URL` becomes optional; fallback `''` already correct.

### BE config revert

- Remove `extraEnv: Cors__AllowedOrigins__N` blocks from `values-staging.yaml` / `values-prod.yaml`. CORS list returns to localhost-only.
- AuthServer remains its own host (different origin) — its CORS handling unchanged.

## Consequences

### Positive

- **0 CORS surface for API REST/health/openapi/hubs.** Same-origin → no preflight, no allowlist maintenance.
- **0 per-tenant config when adding a new tenant.** New FE host = new ingress block; no API redeploy / config bump needed.
- **No `VITE_API_BASE_URL` divergence.** FE bundles ship identical for every tenant.
- **Cookie SameSite stays Strict.** Session/OIDC cookies stay first-party; no `SameSite=None` regression risk.
- **One TLS cert per tenant covers everything.** No separate `api-{env}` cert if it's not exposed publicly.

### Negative

- **One-time Helm template work.** `templates/ingress.yaml` must support per-path service backend (currently hard-coded). ~30 LoC change + values shape migration.
- **Ingress controller WebSocket tuning.** `/hubs/*` SignalR needs explicit timeout/upgrade annotations; misconfiguration causes silent disconnects.
- **API host disappears from public routing.** External integrations that hit `api-{env}.aequitas.dev` directly (e.g. partner webhooks, monitoring probes) must move to one of the FE hosts or get a dedicated `api-public-*` host added back. Inventory before ship.
- **Path-prefix collisions.** Any future FE route like `/api/dashboard` conflicts with backend `/api/*`. SPA must avoid these prefixes (already true today).
- **Loss of `/openapi` exposure on prod is desired** — keep behind dev-only flag rather than expose under tenant FE hosts.

## Trigger Conditions (when to revisit)

Pick this option when **any** holds:

1. **Tenant count ≥ 3** with active FE subdomains under same API. CORS allowlist maintenance becomes per-onboarding toil.
2. **Tenant churn** — tenants come and go monthly, allowlist drift starts causing dashboard regressions.
3. **Cookie/SameSite blocker** — a feature requires first-party cookies that cross-site can't carry (e.g. SameSite=Strict CSRF defense, partition-safe storage).
4. **DevOps complains about CORS preflight latency budget** under load (preflight adds RTT per origin per cache window).

Until then, **stay on Plan A** (current state, ADR-implicit): split FE/API hosts + absolute URLs + CORS allowlist.

## Alternatives Considered

| Option | Why rejected (today) |
|--------|----------------------|
| Wildcard CORS `*.aequitas.dev` | ASP.NET CORS regex matching is fragile + grants origin to any future subdomain. Security regression. |
| FE bundles tenant-specific via `VITE_TENANT_API` per build | N tenants × M builds = build matrix explosion. Rebuild on every onboarding. |
| Move FE under API host as a single static folder served by ASP.NET | Couples FE deploy cadence to API deploy. Different teams + different release SLOs. |
| Per-tenant API hostnames (`api-{tenant}-{env}`) | Same CORS allowlist problem just moved. No win. |
| Service Worker proxy on FE rewriting request URLs | Adds offline complexity for a dev-domain bug. Out of scope. |

## Migration Plan (when triggered)

1. **Audit external API callers.** List every system hitting `api-{env}.aequitas.dev` directly (webhooks, monitoring, partner integrations). Decide: move to FE host vs keep dedicated `api-public-{env}` host.
2. **Extend `templates/ingress.yaml`** to support per-path service backend override:
   ```yaml
   {{- if .service }}
   service:
     name: {{ .service }}
     port: ...
   {{- else }}
   service:
     name: {{ include "aqtcorefw.fullname" $ }}
     port: ...
   {{- end }}
   ```
3. **Add WebSocket annotations** for `/hubs/*` path block.
4. **Convert one tenant first** (canary). Verify dashboard, REST, SignalR, OpenAPI all green from FE host root.
5. **Revert FE absolute-URL patches** (see "FE code revert" above).
6. **Remove `extraEnv: Cors__AllowedOrigins__N`** from values-{env}.yaml.
7. **Roll remaining tenants.** Each = one ingress block, no app rebuild.

## Implementation Details

- **Same-origin probes:** `/health/ready`, `/health/live` already exposed by ASP.NET middleware at root (no `/api/v1` prefix). Confirmed by `Program.cs` `MapHealthChecks("/health")`.
- **Path-prefix priority:** NGINX Ingress sorts paths by length descending — most specific wins. `/api` (4 chars) beats `/` (1 char).
- **TLS termination:** at ingress controller. Backend services receive HTTP. Unchanged.
- **AuthServer stays separate.** OIDC flows benefit from a stable issuer URL distinct from FE; keeping `auth-{env}.aequitas.dev` is correct.

## Relates To

- **ADR-001** (modular monolith) — same-origin works because API is one service; would not apply if API split into microservices.
- **ADR-002** (OpenIddict) — AuthServer remains its own host; this ADR scope is API only.
- **memory/feedback-fe-dto-must-match-be-controller.md** — same-origin removes a class of FE-BE drift (URL composition), but DTO contract drift remains the bigger risk.
