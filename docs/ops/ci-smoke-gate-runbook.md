# CI Smoke Gate Runbook

The `smoke-tests` job in `.github/workflows/ci.yml` gates the **fresh-deploy path** on pushes to `main` that touch
backend code and on PRs that touch the high-risk paths (see `docs/deployment-guide.md` section 0,
"When CI runs"): application startup, DI resolution, database seeding, RLS wiring, Hangfire, and
outbox-schema allowlist. It replaces — for CI purposes — the fresh-deploy coverage that the full
integration suite (disabled since 2026-04-18) used to provide.

## What it runs

`dotnet test` on `tests/integration/AqtCoreFW.Startup.SmokeTests` filtered to `Category=Smoke`
(7 curated classes): API/AuthServer startup, controller DI resolution, Hangfire dashboard route,
outbox schema allowlist, menu-seeder purge. All share `[Collection("StartupSmoke")]` and run
serialized (`xunit.runner.json` sets `parallelizeTestCollections: false`).

## Fixture split (why the job needs BOTH a service-container AND Docker)

| Fixture | SQL source | Smoke classes | Honors `TEST_USE_LOCAL_SQL`? |
|---------|-----------|---------------|------------------------------|
| `SqlServerFixture` (via `SeederSmokeFixture`) | the `sql` **service container** at `localhost,1433` | 2 | Yes |
| `ApiHostFixture` / `AuthServerHostFixture` | **self-spun Testcontainers** (`mcr.microsoft.com/mssql/server:2022-latest`) | 5 | No — ignore the env var |

So the CI job provides **both**: a SQL `services:` container (for the 2 shared-fixture classes)
**and** working Docker (present on GH `ubuntu-latest`, for the 5 Testcontainers classes).

## Env-var contract

- `TEST_USE_LOCAL_SQL=true` — enables the local-SQL path in `LocalSqlOptions.FromEnvironment()`.
- `SA_PASSWORD` — must **equal** the service container's `MSSQL_SA_PASSWORD` (`LocalSqlOptions`
  logs in as `sa`). Both are set to `Gov@Test1234!` in the workflow.
- `Server` is not env-overridable — `LocalSqlOptions` hardcodes `localhost,1433`, so the service
  container is mapped `1433:1433`.

**Credential note:** the SA password is an ephemeral, disposable per-run CI SQL instance — never a
real or production credential. A GitHub secret would add ceremony with no security gain (the value
is inherently throwaway), so it is an intentional plaintext literal in the workflow.

## Scope boundaries

- The full **197-test integration suite** stays local-dev-only (`integration-tests` job remains
  `if: false`). Developers run it locally via `TEST_USE_LOCAL_SQL=true` before opening a PR.
- This job does **not** flip any other CI gate (`SECURITY_SCAN_ENFORCE`,
  `BLOCK_DESTRUCTIVE_MIGRATIONS` stay advisory; e2e/docker/perf jobs stay disabled).
- Smoke tests run as `sa` (db_owner) → RLS FILTER/BLOCK predicates are bypassed at the engine
  level, so this gate validates seeding/DI/startup wiring, **not** tenant read-isolation under a
  least-privilege login. That remains a separate concern.

## Branch protection (manual, out-of-band)

Branch protection is not available on this private repository under the Free plan, and with
path-filtered triggers a required check would block every PR that does not start CI. Keep this
section for a future paid or public setup. Adding `smoke-tests` to the required status checks is a
GitHub **repo-settings** change (not in `ci.yml`). Do it in Settings → Branches → branch-protection rule for `main` once the job is proven
green.

## Rollback

Revert the `ci.yml` change — removes the job, restores prior CI exactly. Zero application-code impact.
If the smoke suite turns flaky and blocks PRs, downgrade to advisory (`continue-on-error: true`)
while investigating rather than deleting the job.
