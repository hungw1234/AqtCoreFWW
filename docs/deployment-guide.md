# Deployment Guide

## Overview

AqtCoreFW ships as a containerized .NET 10 backend + separate AuthServer:
- **`aqtcorefw-api`** — REST API host (identity + cases + files + notifications + audit + masterdata + forms + ai + workflow + reporting modules)
- **`aqtcorefw-authserver`** — OpenIddict 7.4.0 OIDC/OAuth2 server (separate deployment, AuthorizationController with PKCE + ROPC + login page)
- **Nginx (ingress) + YARP** — Nginx at K8s ingress layer handles SPA + public API entry; YARP 2.3 active **inside** `AqtCoreFW.Api` for internal API versioning, module routing, and Scalar OpenAPI aggregation (see `Gateway/GatewayRegistration.cs`).

Deployments use GitHub Actions CI/CD → Docker Registry → Kubernetes 1.35+ via Helm charts. Secrets pulled from HashiCorp Vault at runtime.

**Production Readiness (P1):** Phase 11 complete — CI/CD, Vault integration, Docker hardening, all verified in test suite.

---

## 0. CI/CD Pipeline (GitHub Actions)

### Workflows

| Workflow | File | Jobs that run today |
|---|---|---|
| `CI` | `.github/workflows/ci.yml` | `unit-tests` (build with `/warnaserror`, unit and architecture tests, coverage), `smoke-tests` (startup smoke tests against SQL Server), `quality-gates` (vulnerable packages, secret pattern scan, destructive-migration gate, Semgrep) |
| `CI (frontend)` | `.github/workflows/ci-frontend.yml` | `frontend` (type check, Vitest, production build) |

`integration-tests`, `e2e`, `docker-build`, `docker-integration` and `perf-smoke` are defined but disabled with `if: false` until the Docker infrastructure issue (#21) is fixed. Nothing in these workflows pushes images or deploys.

### When CI runs

The repository is private and Actions minutes are a fixed free budget that will not be raised (owner decision 2026-11-03). One full run costs about 30 billed minutes (unit-tests 9-12, smoke-tests 9-10, frontend 6-9, quality-gates 5; each job is rounded up to the minute), so CI runs only where it gives evidence a local run cannot.

| Change | CI before merge | CI after merge on `main` |
|---|---|---|
| **A. High risk**: `*.csproj`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `NuGet.config`, `dotnet-tools.json`, `ci/**`, `**/Migrations/**`, `src/shared/**`, `src/host/**`, `src/modules/identity/**`, `src/modules/audit/**`, `.semgrep/**`, `tests/architecture/**`, `tests/quality-gates/**`, the workflow files; web `package.json`, `pnpm-lock.yaml`, `tsconfig*.json`, Vite, Vitest and Orval config, `web/src/core/**` | Yes, starts automatically on the PR | Yes |
| **B. Ordinary code**: other module code under `src/modules/**`, other tests, web features and UI | No. Run `scripts/check-before-merge.sh` and paste the result in the PR | Yes, once |
| **C. No build impact**: `docs/**`, `plans/**`, `*.md`, `scripts/**`, `infra/docker/**`, `src/http/**`, `web/e2e/**` | No | No |

The path lists in the two workflow files are the source of truth; the table mirrors them. Rules that sit on top of the table:

- Draft PRs never start CI. Push the branch once local checks pass, or keep the PR as a draft while iterating and mark it ready when done: every push to a ready PR in group A costs another run.
- A group B change that is risky in a way the paths do not capture (Linux-only behaviour such as path case sensitivity, Testcontainers, a cross-module contract change) gets CI on demand before merge: `gh workflow run CI --ref <branch>` or `gh workflow run "CI (frontend)" --ref <branch>`.
- `main` must be green for every workflow that ran on it before the change is proposed to a downstream repository (for example the Bitbucket `dev` PR of a project built on this base). A red `main` is fixed or reverted the same day.
- A job that fails in about 2 seconds without running a step was not started (spending limit reached), it did not fail. Report it as "CI did not run", fall back to `scripts/check-before-merge.sh`, and state that in the PR. A group A change merged that way is re-checked with a manual run once minutes are available.

`scripts/check-before-merge.sh [base-ref]` runs the backend checks (build with `/warnaserror`, `ci/unit.slnx` tests, high or critical vulnerable packages) when `src/`, `tests/`, `ci/`, `.semgrep/` or `dotnet-tools.json` changed, and the frontend checks when `web/` changed. It does not run the smoke tests, Semgrep or the destructive-migration gate, which is why those paths stay in group A.

### Dependency report (`.github/workflows/dependency-report.yml`)

Weekly (Monday 08:00 Asia/Ho_Chi_Minh) and on demand. Read-only: lists outdated and vulnerable NuGet, pnpm, SDK and tool versions, licence-changed packages, and pinned Docker images and actions in the pinned GitHub issue labelled `dependency-report`. It never edits files or opens pull requests. Runbook: [`docs/ops/dependency-report-runbook.md`](ops/dependency-report-runbook.md). One run takes about 4 minutes (run 41673735554), about 17 billed minutes per month of the fixed Actions budget.

### Environment Variables (Secrets)

Store in GitHub `Settings → Secrets → Actions`:

| Secret | Used For | Example |
|--------|----------|---------|
| `DOCKER_REGISTRY_URL` | Container registry | `docker.io` |
| `DOCKER_USERNAME` | Auth for push | `aequitas` |
| `DOCKER_PASSWORD` | Registry credentials | (GitHub-managed) |
| `SONARQUBE_TOKEN` | SAST scanning | (GH secret) |
| `K6_TEST_APIKEY` | Load test auth | (Seeded in dev env) |

**Important:** No secrets in code or config files. All secrets injected via CI/CD environment.

---

## 1. Container Build (Multi-stage Dockerfile)

```dockerfile
# ── Stage 1: Build ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/AqtCoreFW.slnx", "."]
COPY ["src/", "."]

# Full restore cache: RUN before COPY (enables layer reuse)
RUN dotnet restore AqtCoreFW.slnx

RUN dotnet publish src/host/AqtCoreFW.Api/AqtCoreFW.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# OCI Labels (metadata)
LABEL org.opencontainers.image.title="AqtCoreFW API" \
      org.opencontainers.image.version="1.0.0" \
      org.opencontainers.image.created="2026-03-17" \
      org.opencontainers.image.vendor="AEQUITAS"

# Non-root user (security hardening, CVE mitigation)
RUN addgroup --system --gid 1001 appgroup && \
    adduser --system --uid 1001 --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

# Health check (K8s readiness probe integration)
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/ready || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "AqtCoreFW.Api.dll"]
```

Build and push:
```bash
docker build -t nexus.internal/aqtcorefw-api:${VERSION} -f Dockerfile.Api .
docker push nexus.internal/aqtcorefw-api:${VERSION}
```

---

## 2. Kubernetes Deployment (Helm)

> **Target (user decision 2026-07-23): production deploys on Kubernetes** — closes the P1-audit §4-Q1 open question. Local rehearsal runs on a Docker Desktop kind cluster (1 node, containerd image store) per `plans/260723-1258-k8s-local-stable-monolith-deploy/`.
>
> ⚠️ **NetworkPolicy carve-out:** Docker Desktop's kind provisioner ships the default kindnet CNI, which does **not** enforce NetworkPolicy — `network-policy.yaml` applies "successfully" but is a silent no-op there. Never treat a local-kind run as netpol verification; enforcement is verified only on a Calico/Cilium cluster (standalone kind with `disableDefaultCNI` + Calico, or staging).
>
> ⚠️ **Vault carve-out (local rehearsal only):** since 2026-07-25 the kind cluster runs Vault on **file storage backed by a PVC**, so it boots **sealed** and an `unseal` sidecar (`infra/k8s-local/vault-unseal.sh`) initializes it on first boot and re-unseals it after every restart. Secrets now survive pod restart and host sleep/resume — the failure that killed the 2026-07-23 soak (dev-mode in-memory storage → every secret lost on restart → `ProductionSecretValidator` fail-fast → 146 CrashLoops; row `VAULT-DEVMODE-INMEMORY-KILLS-SOAK`). **Still NOT a production posture:** 1 key share / threshold 1, and the unseal key is stored unencrypted on the same volume as the data it unlocks — deliberately, so key and data are discarded together on a local reset. The Vault-operations items that remain untested and are prod-rollout work: a **real unseal ceremony with split key custody**, HA, k8s auth (AppRole/JWT), and audit devices. The P2 Production-posture rehearsal (2026-07-23) exercised the *app side* of the secret path (VaultConfigurationProvider mount fix, ProductionSecretValidator both branches, real random keys).
>
> ⚠️ **Local data tier is now persistent:** SQL Server and Vault use PVCs; Redis/RabbitMQ/MinIO/ClamAV remain `emptyDir`. `scripts/k8s-local-reseed.sh` **refuses to run while the data PVCs exist** — it mints new random encryption keys at step 5b, so re-running against a surviving DB would orphan the existing rows' key. Use `RESET_DATA=1` to wipe data and keys together.
>
> **Rehearsal-only config flags (NEVER set in production values):** `VneId:AllowStub=true` (boots Production mode with the VNeID stub while `VNeIdHttpConnector` does not exist — default false, fail-fast preserved), `Auth:AllowHttp=true` (AuthServer behind a plain-HTTP local ingress: disables the OpenIddict HTTPS transport requirement + redirect), `ForceHttps=false` (API HTTPS redirect off). All three live only in `infra/helm/*/values-local.yaml`.
>
> **AuthServer OpenIddict certificates (non-Development):** the AuthServer refuses to boot outside Development unless `Auth:SigningCertificate:{Path,Password}` and `Auth:EncryptionCertificate:{Path,Password}` point at readable PFX files (fail-fast — an OIDC server silently minting ephemeral-key tokens would invalidate all tokens per restart). Local kind mounts self-signed PFX from Secret `authserver-openiddict-certs` (minted by `scripts/k8s-local-generate-prod-secrets.sh`); production certificate issuance/custody/rotation is an open runbook item — backlog `AUTHSERVER-PROD-CERT-PROVISIONING-RUNBOOK`.
>
> **AuthServer SPA client redirect URIs (non-Development, REQUIRED):** the OpenIddict seeder runs in every environment and refuses to boot outside Development unless `Auth:SpaClient:RedirectUris` and `Auth:SpaClient:PostLogoutRedirectUris` list the deployment's absolute callback URLs (fail-fast — the alternative is a client seeded with localhost-only URIs, which fails every login as an opaque `redirect_uri` mismatch). Helm binds arrays from indexed env keys: `Auth__SpaClient__RedirectUris__0`, `__1`, … Each origin needs **both** `/callback` and `/silent-renew.html` (oidc-client-ts loads the latter in a hidden iframe on refresh-token renew; an unwhitelisted URI yields 400 invalid_request). The base `values.yaml` deliberately sets none — supply them per overlay; `values-local.yaml` has the `*.localtest.me` set as the worked example. Production overlay is an open item — backlog `AUTHSERVER-PROD-VALUES-SPA-REDIRECT-URIS`.

> **Introspection client secret (non-Development, REQUIRED):** `Auth__ResourceClientSecret` must be in Vault `secret/aqtcorefw` before Api or AuthServer rolls out in any environment other than Development/Testing/Benchmark; missing, or equal to the built-in dev value, fails startup (including the `db-migrate` Job, which builds the Api host). Fresh local cluster: `scripts/k8s-local-generate-prod-secrets.sh` seeds it. Running cluster (k8s-local, staging): add only this key, do not rerun that script (it rotates FieldKey/HMAC): `vault kv patch -mount=secret aqtcorefw Auth__ResourceClientSecret="$(openssl rand -base64 32)"`. Rollout order: AuthServer first (on boot it rotates the stored client secret to the configured value), then Api with the same value; between the two restarts introspection returns 401.
>
> ⚠️ **Post-logout URI ordering is load-bearing.** In `PostLogoutRedirectUris`, list each origin **slashless first**, then with a trailing slash (`https://app.example.gov.vn`, then `https://app.example.gov.vn/`). `new Uri("http://h:p")` equals `new Uri("http://h:p/")`, so the seeder's `ISet<Uri>` keeps whichever spelling came first; OpenIddict persists that one's `OriginalString` **without normalizing** and matches logout requests with `StringComparison.Ordinal`, while the SPA sends `window.location.origin` (always slashless). List the slash form first and every logout returns 400 invalid_request while login keeps working — a failure mode that looks nothing like its cause.

### Chart structure (V2)
```
helm/
├── aqtcorefw-api/
│   ├── Chart.yaml
│   ├── values.yaml
│   └── templates/
│       ├── deployment.yaml
│       ├── service.yaml
│       ├── ingress.yaml
│       ├── configmap.yaml
│       └── db-migration-job.yaml
└── aqtcorefw-authserver/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
        ├── deployment.yaml
        ├── service.yaml
        ├── ingress.yaml
        ├── configmap.yaml
        └── secret.yaml
```

**AuthServer Helm (NEW V2):**
Separate deployment for OpenIddict OIDC/OAuth2 server. Deploy with:
```bash
helm upgrade --install aqtcorefw-authserver ./helm/aqtcorefw-authserver \
  --namespace gov-apps \
  --set image.tag=${VERSION} \
  --set replicaCount=2 \
  --set ingress.host=auth.gov.internal \
  --values helm/aqtcorefw-authserver/values.prod.yaml \
  --wait --timeout 5m
```

**AuthServer Key Differences:**
- Image: `docker.io/aequitas/aqtcorefw-authserver:${VERSION}`
- Service port: 5000 (HTTPS in prod, HTTP in dev)
- Ingress: `auth.gov.internal` (separate host from API `api.gov.internal`)
- ConfigMap: OpenIddict client configurations, allowed redirect URIs, OIDC scopes
- Secret: JWT signing key (RS256 private key), database credentials

### Deploy
```bash
helm upgrade --install aqtcorefw-api ./helm/aqtcorefw-api \
  --namespace gov-apps \
  --set image.tag=${VERSION} \
  --set replicaCount=2 \
  --values helm/aqtcorefw-api/values.prod.yaml \
  --wait --timeout 5m
```

### Key values.yaml fields
```yaml
image:
  repository: nexus.internal/aqtcorefw-api
  tag: "1.0.0"
  pullPolicy: IfNotPresent

replicaCount: 2

resources:
  requests:
    cpu: "250m"
    memory: "512Mi"
  limits:
    cpu: "1000m"
    memory: "1Gi"

vault:
  address: "https://vault.internal:8200"
  role: "aqtcorefw-api"   # Vault Kubernetes auth role

ingress:
  enabled: true
  host: api.gov.internal
  tls: true
```

---

## 3. Environment Variables Reference

All secrets come from Vault. Non-secret config is in `ConfigMap`:

| Variable | Source | Description | Docker Dev |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | ConfigMap | `Production` | `Development` |
| `ASPNETCORE_URLS` | ConfigMap | HTTP binding | `http://+:5001` for API, `http://+:5000` for AuthServer |
| `Vault__Address` | ConfigMap | Vault server URL | Not needed in docker compose dev |
| `Vault__Role` | ConfigMap | Kubernetes auth role | Not needed in docker compose dev |
| `ConnectionStrings__Default` | Vault | SQL Server connection string (least-privilege runtime login) | `Server=sqlserver;Database=AqtCoreFW;...` |
| `ConnectionStrings__AuditAdmin` | Vault | SQL Server audit admin connection (db_owner login, for backfill + health check) | Falls back to `Default` if not set |
| `ConnectionStrings__Redis` | Vault | Redis connection string | `redis:6379` |
| `OpenIddict__Authority` | ConfigMap | AuthServer IssuerUri | `http://host.docker.internal:5000` (Docker), `http://localhost:5002` (dev) |
| `Auth__ResourceClientId` | ConfigMap | Introspection client ID (Api and AuthServer) | Defaults to `aqtcorefw-api-resource` |
| `Auth__ResourceClientSecret` | Vault | Introspection client secret, same value for Api and AuthServer. **Required outside Development/Testing/Benchmark**: missing or equal to the built-in dev value fails startup; AuthServer rotates the stored client secret to this value on boot | Dev value built in for local only |
| `Jwt__SigningKey` | Vault | RS256 private key (PEM) | Dev key only |
| `Minio__AccessKey` | Vault | MinIO access key | `minioadmin` (dev) |
| `Minio__SecretKey` | Vault | MinIO secret key | `minioadmin` (dev) |
| `RabbitMQ__Password` | Vault | RabbitMQ password | `guest` (dev) |

**Docker Note:** AuthServer uses HTTP (no HTTPS in dev); IssuerUri discovery uses `host.docker.internal:5000` from within containers.

### 3.1 Deferred-module flags (v1.0 launch)

5 modules (AI, Collaboration, BusinessProcess, Reporting, Rules) are deferred to v1.1 but shipped built-in, OFF at deploy time:

| Key | Default | Effect when `false` |
|---|---|---|
| `Features__Ai__Enabled` | `true` | AI module off — DI/controllers/migration/routes removed |
| `Features__Collaboration__Enabled` | `true` | Collaboration off — also unmaps ChatHub (`/hubs/chat`) |
| `Features__BusinessProcess__Enabled` | `true` | BusinessProcess off |
| `Features__Reporting__Enabled` | `true` | Reporting off — also removes the persisted `reporting.report-cleanup` Hangfire schedule on next boot |
| `Features__Rules__Enabled` | `true` | Rules off — also skips its `RulesEngineCacheSubscriber` hosted service |

A **missing key defaults to enabled** — dev/test keep today's everything-on behavior with zero config. Production sets the flags to `false` at deploy time (env var, ConfigMap, or Helm `values.yaml`) for any module not in launch scope; no rebuild needed to flip back on. `false` removes the module at 4 layers (DI registration, controller discovery, startup migration set, non-controller endpoint mapping) — a fresh database never receives an OFF module's schema. FE hides the module's routes/menus automatically by reading `GET /api/v1/system/module-flags` (fail-open on fetch error — the BE 404 on disabled endpoints is the actual gate). Detail: `docs/dependency-map.md` §5b.

### 3.2 Runtime feature flags (`SystemParameter`, not env vars)

Distinct from §3.1. The `Features__*__Enabled` flags above are **config** read at startup. The flags below are **rows in `config.SystemParameters`** with a `feature:` key prefix, cached in-process by `FeatureFlagService` (reloaded every 5 min, invalidated across pods over the Redis `ff-invalidate` channel). Setting an env var does nothing for these.

| Key | Prod default | Scope | Effect |
|---|---|---|---|
| `feature:files.virus-scan` | `false` | **Global only** | When `false`, uploads are marked Available **without being scanned** |

A **missing key reads as `false`** (`FeatureFlagService.IsEnabled` returns false for an absent key), so every flag here is seeded explicitly in all environments — an absent row and a deliberate opt-out would otherwise be indistinguishable.

#### Enabling upload virus scanning

The scanner is **fail-closed**: an unreachable clamd yields `Error`, and the file stays `Quarantined` rather than passing unscanned. That makes the order mandatory:

1. **Provision clamd first.** Deploy the daemon and point the API at it via the `ClamAV` config section — `ClamAV__Host` (default `localhost`), `ClamAV__Port` (default `3310`), `ClamAV__TimeoutMs` (default `30000`, per NF2). Confirm reachability before step 2.
2. **Then flip the flag**, via `PUT /api/v1/admin/feature-flags/files.virus-scan` with `{"isEnabled": true}`.

Flipping first strands **every** upload in `Quarantined` until clamd exists.

`feature:files.virus-scan` is **global-only** and the toggle requires **SystemAdmin** — malware scanning is deployment-wide posture, and a tenant must not be able to opt its own uploads out of it. Ordinary `feature:` flags remain per-tenant. Two independent upload paths gate on this one flag: the async Files pipeline (`ClamAvScanJob`) and the synchronous signature-image path (`UserSignatureImageStoreAdapter`).

> A per-tenant row for this flag would also be **invisible in production**: the flag cache is filled by a background service with no ambient tenant, so `SESSION_CONTEXT` is unset and the RLS filter on `config.SystemParameters` admits only NULL-tenant rows. The toggle would return `200` while the row was never read. This is why the endpoint writes the global row for posture flags.

While scanning is off, `/health/ready` reports `upload-virus-scan` as **Degraded** in Production (see §6) and every upload emits a `LogCritical` bypass line.

---

## 4. Database Migration Strategy

Uses **expand-contract** pattern for zero-downtime deployments.

### Pattern
1. **Expand** — add new columns as nullable, add new tables (backward compatible)
2. **Deploy** new application version (reads both old and new schema)
3. **Contract** — drop old columns/tables (in a follow-up release)

### Startup migration gate (`--migrate-only` / `RUN_MIGRATIONS_ONLY` / `SKIP_STARTUP_MIGRATIONS`)

Implemented in `Program.cs` (both hosts) via `StartupMigrationMode` — `src/shared/AqtCoreFW.Infrastructure/Configuration/StartupMigrationMode.cs`:

| Trigger | Effect | Used by |
|---|---|---|
| `--migrate-only` CLI arg **or** `RUN_MIGRATIONS_ONLY=true` (env/config) | Runs `MigrateAsync()` for every module context, then **exits** — no Kestrel, Hangfire, recurring jobs, or runtime self-checks. | The Helm `db-migrate` Job (`helm.sh/hook: pre-install,pre-upgrade`, `templates/db-migrate-job.yaml`) / a K8s init-container. Wire it to a **separate elevated DB login** when moving to the least-privilege steady state (below). |
| `SKIP_STARTUP_MIGRATIONS=true` (env/config) | The runtime host **skips** in-app `MigrateAsync()` entirely (schema must be migrated out-of-band). | The runtime Deployment, so the runtime DB login can drop `db_ddladmin`. **Off by default** in the chart (`values.yaml` ships `"false"`) — opt in via `values-prod.yaml` `extraEnv`, but only after the migrate Job has its own elevated login (see below). |
| *(neither set — default)* | Migrate on boot, then serve. Keeps dev, integration tests (Testcontainers), and single-container deployments — and the chart's default `helm install` — working with no extra wiring. | Dev / CI / single-container / chart default. |

`--migrate-only` **takes precedence** over `SKIP_STARTUP_MIGRATIONS` (so the migrate Job inheriting `SKIP_STARTUP_MIGRATIONS` from a shared ConfigMap still migrates). Scope is **schema migrations only** — the AuthServer data-plane upgraders (PII encryption, email-hash backfill) run unconditionally on every normal runtime boot and need only `db_datawriter`; they are *not* run by `--migrate-only`.

**To adopt the least-privilege steady state (drop `db_ddladmin` from the runtime login), do BOTH, in order:**
1. Provision a separate **elevated** migration login (member of `db_owner`, or `db_ddladmin` + `ALTER ANY SECURITY POLICY`); add it to the chart secret as the `db-migration-connection-string` key; uncomment the `ConnectionStrings__Default` override in `templates/db-migrate-job.yaml` (an `env` entry overrides the `envFrom` secret). **Also provision a separate `AuditAdmin` login** (member of `db_owner`) for the HMAC backfill job and health check to read cross-tenant audit tables; add it to the chart secret as `audit-admin-connection-string` key.
2. **Then** set `SKIP_STARTUP_MIGRATIONS=true` on the runtime Deployment (`values-prod.yaml` `extraEnv` — last duplicate env var wins) and drop `db_ddladmin` from the runtime login.
> Do **not** do step 2 before step 1: the `pre-upgrade` migrate Job would then run under the DDL-less runtime login and fail on any RLS / DDL migration.

**Step-by-step operator runbook for this rollout (DBA + ops):** [`docs/ops/rls-migration-login-runbook.md`](ops/rls-migration-login-runbook.md) — covers provisioning the elevated migration login, wiring the secret via External Secrets Operator, the chart edits, the staged rollout + verification, and the `db_ddladmin` revoke. Backlog item: `RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING` (P3, approach B — repo-side hooks shipped in PR #105 + this commit; activation blocked on per-environment DBA/ops steps in the runbook).

**Migrate-Job retry safety (idempotency audit, 2026-05-13).** Helm sets `backoffLimit: 3` on the migrate Job → a transient pod failure re-runs the entire `--migrate-only` invocation. All raw-SQL migrations (`migrationBuilder.Sql(...)`) in this tree are idempotent across re-runs because (a) EF Core wraps each migration's `Up()` in a single transaction (no `suppressTransaction: true` anywhere — verified) and SQL Server DDL is fully transactional, so a partial failure rolls back atomically; (b) the explicit raw SQL itself is idempotent — `CREATE OR ALTER FUNCTION`/`TRIGGER`, `DROP … IF EXISTS`, `INSERT … WHERE NOT EXISTS`, and `UPDATE … SET PublicId = NEWID() WHERE PublicId = '00000000-…'` (filter prunes already-set rows). The single non-idempotent statement (`CREATE SECURITY POLICY` in `RlsMigrationHelper.GenerateRlsPolicyStatements`) is covered by (a). Belt-and-suspenders.

> The chart ships **one** migrate Job (on the Api image). The AuthServer host migrates only `IdentityDbContext` + `MessagingDbContext`, both of which the Api migrate set already covers — so deploying Api + AuthServer together (Api chart's `pre-install` hook runs first) is sufficient. If AuthServer is deployed as a **standalone** Helm release without that Job, leave `SKIP_STARTUP_MIGRATIONS` unset for it (or run `infra/scripts/run-migrations.sh` first).
>
> ⚠️ Non-Helm deployments (docker-compose, bare-metal) that set `SKIP_STARTUP_MIGRATIONS=true` **must** run migrations out-of-band first (`dotnet AqtCoreFW.Api.dll --migrate-only`, or `infra/scripts/run-migrations.sh`) — otherwise the runtime host starts against an unmigrated DB and fails on the first query. Helm enforces the ordering via the `pre-install,pre-upgrade` hook; outside Helm it is a deployment contract.

### Application DB Principal (least privilege — required for Row-Level Security)

**Why this matters.** Every RLS predicate function (`RlsMigrationHelper` / `RlsMigrationHelper.NullableTenant`) carves out `IS_MEMBER('db_owner') = 1` so migration tooling and admin batch jobs can cross tenants. If the **runtime application** login is itself `db_owner`, `sysadmin`, or `db_securityadmin`, then **all 36 tenant-isolation security policies are bypassed** (or trivially disableable) and tenant data is unfiltered at the engine level — the RLS layer becomes theatre. EF global query filters still apply in-process, but any raw-SQL / Dapper path (and any code path that forgets the filter) is wide open.

**Hard requirement.** The login the application uses at runtime (`ConnectionStrings:Default`) MUST NOT be a member of:
- `sysadmin` (server role) — bypasses everything
- `db_owner` — satisfies the RLS carveout
- `db_securityadmin` — can grant itself `CONTROL` / `db_owner`

`db_ddladmin` is acceptable **only** if the runtime host runs startup migrations (i.e. `SKIP_STARTUP_MIGRATIONS` is **not** set — the chart's current default): it does not satisfy `IS_MEMBER('db_owner')`, so the tenant-isolation predicates still apply to its queries — but it *can* `ALTER`/disable a `SECURITY POLICY`, so it is a transitional grant, not a steady-state one. To reach the least-privilege steady state (set `SKIP_STARTUP_MIGRATIONS=true` and drop `db_ddladmin` from the runtime principal — only the separate migration login then needs DDL), follow the two-step sequence in §"Startup migration gate" above. (The startup self-check below detects *role membership* only — a direct `GRANT CONTROL ON DATABASE` or `GRANT ALTER ANY SECURITY POLICY` to the user, without a role, is not caught; provision the runtime login through roles, not ad-hoc grants.)

**Provision a least-privilege runtime login** (run as a DBA, once per environment):
```sql
-- Server-level login (SQL auth shown; prefer a managed identity / AAD login where available)
CREATE LOGIN [aqtcore_app] WITH PASSWORD = '<from-vault>', CHECK_POLICY = ON;

USE [AqtCoreFW];
CREATE USER [aqtcore_app] FOR LOGIN [aqtcore_app];

-- Data plane only
ALTER ROLE db_datareader ADD MEMBER [aqtcore_app];
ALTER ROLE db_datawriter ADD MEMBER [aqtcore_app];
-- EXECUTE on stored procs/functions the app calls (RLS TVFs are invoked implicitly by the engine — no grant needed)
GRANT EXECUTE TO [aqtcore_app];

-- Needed ONLY while the runtime host runs startup migrations (i.e. SKIP_STARTUP_MIGRATIONS is NOT set —
-- the chart's current default). After adopting the least-privilege steady state (db-migrate Job under an
-- elevated login + SKIP_STARTUP_MIGRATIONS=true — see the runbook), REVOKE this from [aqtcore_app]:
ALTER ROLE db_ddladmin ADD MEMBER [aqtcore_app];   -- transitional; drop in steady state

-- Do NOT: ALTER ROLE db_owner ADD MEMBER [aqtcore_app];   -- defeats RLS
-- Do NOT: ALTER SERVER ROLE sysadmin ADD MEMBER [aqtcore_app];
```

**Migrations** use a *separate* elevated login (member of `db_owner` or at least `db_ddladmin` + `ALTER ANY SECURITY POLICY`), wired only into the `--migrate-only` invocation (Helm `db-migrate` Job / init-container) — never into the runtime container's `ConnectionStrings:Default`.

**Verification query** (run *as the application login* — e.g. via `sqlcmd -U aqtcore_app` — all three must be `0`):
```sql
SELECT
    IS_SRVROLEMEMBER('sysadmin')    AS is_sysadmin,       -- must be 0
    IS_MEMBER('db_owner')           AS is_db_owner,       -- must be 0
    IS_MEMBER('db_securityadmin')   AS is_db_securityadmin,-- must be 0
    IS_MEMBER('db_ddladmin')        AS is_db_ddladmin,    -- 0 expected; 1 tolerated only if SKIP_STARTUP_MIGRATIONS is unset
    ORIGINAL_LOGIN()                AS login_name,
    DB_NAME()                       AS database_name;
```

**Startup self-check.** In `Production`, both hosts call `RlsPrincipalValidator.WarnIfDbOwnerInProduction()` (immediately after `ValidateProductionSecrets()`); it runs the equivalent of the query above on the app's own connection and logs `Critical` (it does **not** throw — a misconfigured principal must be visible in logs/alerts, not a crash-loop) with the offending login, database, and roles. Wire a log-based alert on that message. To upgrade it to fail-fast, see backlog `RlsPrincipal:FailFast`.

---

## 5. Secrets Management (HashiCorp Vault)

**Moved:** [`docs/ops/vault-secrets-management-guide.md`](./ops/vault-secrets-management-guide.md)

Covers Vault setup and architecture, the `Vault__*` configuration keys, secret paths, Kubernetes auth
setup, the audit HMAC key and per-tenant HMAC chain signing/recovery, dev re-seed after a container
restart, and the Helm values for Vault integration.

Split out on 2026-07-28: this section was 267 lines, about 30% of a file that had grown past the
800-line working limit. Section numbers below are unchanged so existing `deployment-guide.md §N`
references keep resolving.

---

## 6. Health Check Endpoints

| Endpoint | Description | Used by |
|---|---|---|
| `GET /health/live` | Liveness — is the process alive? No dependency checks. | K8s liveness **and startup** probes |
| `GET /health/ready` | Readiness — dependency + posture checks (table below) | K8s readiness probe |

> There is no `/health/startup` endpoint. `MapGovHealthChecks` maps exactly two paths (plus `/` → redirect to `/health/live`); the Helm `startupProbe` points at `/health/live` with a generous `failureThreshold` to cover slow first-boot migrations.

### Checks on `/health/ready`

| Check | Failure status | Meaning |
|---|---|---|
| `sqlserver` | Unhealthy | SQL Server unreachable — pod leaves rotation |
| `redis` | Degraded | Cache unreachable |
| `rabbitmq` | Degraded | Broker unreachable (registered only when `Bus:Transport=RabbitMQ`) |
| `qdrant`, `ollama` | Degraded | AI probes (registered only when `AI:Ollama:Enabled=true`) |
| `audit-read-isolation` | Degraded | `NonAudit` least-privilege login unprovisioned in Production |
| `upload-virus-scan` | Degraded | Uploads stored without scanning in Production (§3.2) |

**Degraded returns HTTP 200 and keeps the pod in rotation** — it is a signal, not an outage. The last two checks report *postures* that are legitimate-but-unwanted states rather than faults: failing readiness over them would take production down for a condition the deployment knowingly shipped with. Both are green outside Production, where the underlying provisioning is expected to be absent.

Because Degraded is HTTP 200, a K8s probe alone will never surface these. Alerting must read the per-check status from the response body.

K8s probe config:
```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 15

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 20
  periodSeconds: 10
```

---

## 7. Blue-Green Deployment

```bash
# Deploy new version as "green" alongside existing "blue"
helm upgrade --install aqtcorefw-api-green ./helm/aqtcorefw-api \
  --set image.tag=${NEW_VERSION} \
  --set ingress.enabled=false   # no traffic yet

# Run smoke tests against green
./scripts/smoke-test.sh https://green.api.gov.internal

# Switch traffic (update ingress to point to green service)
kubectl patch ingress aqtcorefw-api \
  -p '{"spec":{"rules":[{"host":"api.gov.internal","http":{"paths":[{"backend":{"service":{"name":"aqtcorefw-api-green"}}}]}}]}}'

# Confirm, then remove blue
helm uninstall aqtcorefw-api-blue
```

---

## 8. CI/CD Pipeline (Jenkins)

All deployments triggered via Jenkins declarative pipelines in `jenkinsfile/`.

### Pipeline Stages
1. **Build** — `dotnet build` + SAST scan (SonarQube)
2. **Test** — `dotnet test` (unit + integration + architecture)
3. **Security** — DAST (OWASP ZAP), SBOM generation (CycloneDX)
4. **Package** — Docker build + push to Nexus registry
5. **Deploy** — Helm upgrade to staging, then approval for production
6. **Verify** — k6 load tests, smoke tests, security checks

Jenkins credentials: Pull from Vault at pipeline runtime. No plaintext secrets in Jenkinsfile.

---

## 8.1 k6 Load Testing Configuration

### Test API Key Seeding

For dev/staging environments, seed a non-expiring test API key:

```bash
# Start API with seeding enabled
docker compose up -d
export SEED_TEST_APIKEY=true
export TEST_TENANT_ID="<tenant-id>"

dotnet run --project src/host/AqtCoreFW.Api
# Output logs will show:
# ApiKeyTestSeeder: k6 test key created. Store in CI secret K6_TEST_APIKEY: <key>
```

Store the plaintext key in CI/CD secrets:
- **GitHub:** `Settings → Secrets → Actions → K6_TEST_APIKEY`
- **Jenkins:** `Credentials → k6-test-apikey`

### Run k6 Tests

```bash
k6 run tests/performance/load-test.js \
  --env API_KEY="${K6_TEST_APIKEY}" \
  --vus 50 --duration 5m
```

Supported test types:
- `load-test.js` — steady-state (50 VUs, 5 min)
- `spike-test.js` — sudden burst (100→500 VUs)
- `soak-test.js` — endurance (10 VUs, 70 min)
- Module-specific: `audit-load-test.js`, `identity-load-test.js`, `files-load-test.js`, `notifications-load-test.js`

Key metrics to monitor:
- `http_req_duration` (p95) — target <500ms
- `http_req_failed` — target 0%
- `grpc_conn_errors` — indicates connection pool exhaustion

---

## 8.2 Running Integration Tests Locally

> Developing on macOS (Apple Silicon) or switching between two dev machines: see
> [guides/macos-dev-setup.md](guides/macos-dev-setup.md).

**MANDATORY:** CI does NOT run integration tests. Developers MUST run integration tests locally before opening a PR.

### Prerequisites

- **SQL Server 2022** via Docker Desktop container `sqlserver` (from `infra/docker/docker-compose.yml`, bound to `localhost,1433`); see ADR 006
- SQL Authentication enabled with user `sa` (default password `YourStrong!Passw0rd`)
- TCP port 1433 open and listening — stop any lingering native SQL service before Docker bind

### Environment Variables

```bash
# Required for local SQL path
export TEST_USE_LOCAL_SQL=true
export SA_PASSWORD=<your-sa-password>  # Fallback literal in code: YourStrong!Passw0rd
```

### Verify SQL Server Connection

```bash
sqlcmd -S "localhost,1433" -U sa -P "$SA_PASSWORD" -Q "SELECT @@VERSION"
```

### Test Databases

On first run, 4 persistent test databases are created:
- `AqtCoreFW_Test_Main` — primary integration tests
- `AqtCoreFW_Test_Messaging` — MassTransit/RabbitMQ tests
- `AqtCoreFW_Test_TenantIso` — tenant isolation tests
- `AqtCoreFW_Test_Concurrency` — concurrency/locking tests

### Run Integration Tests

```bash
# Set env vars first
export TEST_USE_LOCAL_SQL=true
export SA_PASSWORD=<your-sa-password>

# Run integration tests only
dotnet test src/AqtCoreFW.slnx -c Release --filter "FullyQualifiedName~Integration"
```

### Fallback to Testcontainers

If no local SQL Server is available, unset the environment variables:

```bash
unset TEST_USE_LOCAL_SQL
```

The test fixtures will automatically fall back to Testcontainers (requires Docker Desktop; same engine that runs the `sqlserver` dev container). This path is slower (~41s cold-start vs ~12s persistent local SQL) because each test collection spins up a fresh throwaway container.

### Performance Comparison

| Path | Fixture Startup | Notes |
|------|-----------------|-------|
| Local SQL (`TEST_USE_LOCAL_SQL=true`) | ~12s | 3.4× faster; persistent DBs with truncate/reseed |
| Testcontainers (default) | ~41s | Fresh container per collection; Docker required |

---

## 9. Observability

| Tool | URL | What to check |
|---|---|---|
| Grafana | https://grafana.internal | API latency P99, error rate, DB query time |
| Prometheus | https://prometheus.internal | Scrapes `/metrics` **and `/metrics-otel`** — see below |
| Seq | https://seq.internal | Structured logs — filter by `CorrelationId` |
| Hangfire | https://auth.gov.internal/hangfire | Background job status (AuthServer; SystemAdmin-only) |

Key dashboards: `AqtCoreFW API Overview`, `Database Performance`, `MassTransit Consumers`.

Alert thresholds: P99 > 2s, error rate > 1%, DB connection pool > 80%.

### Two scrape endpoints — both are required

The app serves two independent metric pipelines on separate paths, and scraping only one loses half
the metrics with no error anywhere:

- `/metrics` — prometheus-net. HTTP request metrics (`http_requests_received_total`,
  `http_request_duration_seconds_bucket`) that the SLO recording rules and Grafana dashboards query
  **by name**.
- `/metrics-otel` — OpenTelemetry. Every module counter and histogram (audit ingestion durability,
  signing allowlist denials, alert-job self-failures, workflow timings).

The chart's ServiceMonitor scrapes both. If a module counter is missing from Prometheus, check that
the second endpoint is being scraped before investigating the application.

### Alert rules — shipped but OFF everywhere, pending a first production deploy

The chart ships a `PrometheusRule` with four alerts (`AqtCoreFWDown`, `AqtCoreFWWatchdog`,
`AqtCoreFWAlertEvaluationFailing`, `AqtCoreFWAlertNotificationFailing`). They exist because the
in-app alert evaluator reaches its metrics *by querying Prometheus*, so it cannot report a Prometheus
outage — every rule's query fails at once, all read as non-breaching, and alerting goes silent with no
breach. These rules sit outside that query path.

`metrics.prometheusRule.enabled` is **`false` in every values file, production included.** There is
no deployed cluster yet, so none of this has run against a real Prometheus Operator. The rules are
behaviour-tested with promtool (see `infra/prometheus/alert-rules-test.yml`), but a passing unit test
is not a deployed alert.

**Enable procedure — run in this order at the first real deploy. Steps 1 and 2 are not optional; skip
them and the whole ruleset can sit inert with nothing reporting it.**

1. **Read the cluster's actual selector.** The Operator matches `PrometheusRule` objects by label via
   `ruleSelector`, a *different field* from `serviceMonitorSelector` — a working ServiceMonitor does
   **not** prove rules will load.

   ```bash
   kubectl get prometheus -A -o yaml | grep -A5 ruleSelector
   ```

2. **Set `metrics.prometheusRule.labels` to match it**, then flip `enabled: true`. The
   `release: prometheus` label used by the ServiceMonitor is the kube-prometheus-stack default *only*
   when that stack was installed under the Helm release name `prometheus`; confirm rather than copy.

3. **Verify the rules LOADED — against the Prometheus API, not the Kubernetes API.** On a label
   mismatch the object is created and `kubectl get prometheusrule` lists it happily, while Prometheus
   never loads it: no event, no error, no alerts.

   ```bash
   curl -s http://<prometheus>:9090/api/v1/rules | grep AqtCoreFWWatchdog
   ```

   No match means nothing in the ruleset is active.

4. **Route the watchdog to an external receiver, or it is decoration.** `AqtCoreFWWatchdog` fires
   continuously by design — its **absence** at the receiver is the signal. It needs Alertmanager to
   forward it to a dead-man's-switch endpoint (Dead Man's Snitch, Healthchecks.io, any heartbeat
   receiver that alarms on silence). It carries `severity: none` per kube-prometheus-stack
   convention, so a route matching only `critical|warning` silently drops it.

   Until this route exists, **a dead monitoring stack remains undetectable** — the app-side evaluator
   cannot report it either, for the same reason it cannot report a Prometheus outage.

5. **Tune thresholds after a week or two of real traffic**, not before. The seeded defaults are
   arithmetic estimates, not measurements.

Alertmanager configuration lives outside this repo. Tracked as `PROMETHEUS-DEADMAN-SWITCH-MISSING`,
which stays open until steps 3 and 4 are confirmed on a live cluster.
