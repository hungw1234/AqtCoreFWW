# RLS Runtime Login — Least-Privilege Migration Runbook

**Purpose:** Operational procedure for moving the AqtCoreFW **runtime** database login to least privilege (drop `db_ddladmin`) by running schema migrations from a separate, time-bounded elevated login. Closes backlog `RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING` (P3, approach **B** = secret-injected via External Secrets Operator → K8s Secret → migrate-Job env).

**Why this matters:** every Row-Level Security predicate carves out `IS_MEMBER('db_owner') = 1`. The runtime login must not be `db_owner`/`sysadmin`/`db_securityadmin` (else RLS is theatre) — the startup self-check `RlsPrincipalValidator.WarnIfDbOwnerInProduction()` already alerts on that. `db_ddladmin` is one notch down: it does *not* satisfy the carveout, but it *can* `ALTER`/disable a `SECURITY POLICY`, so it is a **transitional** grant the app needs only while it runs `MigrateAsync()` on startup. This runbook removes that need. See `docs/deployment-guide.md` §4.

**Audience:** prod DBA + ops/platform engineer. Application-side prerequisites (the `--migrate-only` / `SKIP_STARTUP_MIGRATIONS` gate, the commented-out chart hooks) already shipped — see PR #105.

**Template-repo note:** AqtCoreFW is a template repository with no live production environment. This runbook is the pattern downstream projects implement. It assumes K8s + Helm + External Secrets Operator (ESO) + a production Vault. If the cluster uses Sealed Secrets or a CD pipeline instead of ESO, step 2 maps to "add a key to whatever creates the `aqtcorefw-secrets` Secret" — everything else is unchanged.

---

## 0. Preconditions (verify before starting)

- App images already contain the `StartupMigrationMode` gate (PR #105 / `v2.137p10`): `dotnet AqtCoreFW.Api.dll --migrate-only` runs schema migrations then exits; `SKIP_STARTUP_MIGRATIONS=true` makes the runtime host skip in-app migrations.
- `infra/helm/aqtcorefw/templates/db-migrate-job.yaml` exists with `command: ["dotnet","AqtCoreFW.Api.dll","--migrate-only"]`, `env: RUN_MIGRATIONS_ONLY=true`, and a **commented-out** `ConnectionStrings__Default` override block.
- `infra/helm/aqtcorefw/values.yaml` ships `SKIP_STARTUP_MIGRATIONS="false"`; `values-prod.yaml` has a **commented-out** `extraEnv: SKIP_STARTUP_MIGRATIONS=true` block.
- The runtime login (e.g. `[aqtcore_app]`) currently has `db_datareader` + `db_datawriter` + `GRANT EXECUTE` + (transitionally) `db_ddladmin`. Confirm with the verification query in §6.
- You have: a DBA account with `sysadmin` (or `db_owner` + the ability to create logins) on the prod SQL Server; write access to the Vault path AqtCoreFW reads from; permission to edit the ESO `ExternalSecret` for `aqtcorefw-secrets` and to `helm upgrade` the chart.

> **Do the steps in order.** Specifically: do **not** revoke `db_ddladmin` from the runtime login (step 5) before the migrate-Job is verified running under the elevated login (step 4). Otherwise the next `helm upgrade`'s `pre-upgrade` Job runs under the DDL-less runtime login and fails on any RLS/DDL migration.

---

## 1. DBA — provision the elevated migration login

Run as a DBA (once per environment):

```sql
-- Server-level login for migrations only. SQL auth shown; prefer a managed/AAD identity if available.
CREATE LOGIN [aqtcore_migrate] WITH PASSWORD = '<strong-random; store in Vault>', CHECK_POLICY = ON;

USE [AqtCoreFW];
CREATE USER [aqtcore_migrate] FOR LOGIN [aqtcore_migrate];

-- Minimum sufficient for EF migrations incl. CREATE/ALTER SECURITY POLICY:
ALTER ROLE db_ddladmin ADD MEMBER [aqtcore_migrate];
GRANT ALTER ANY SECURITY POLICY TO [aqtcore_migrate];
ALTER ROLE db_datareader ADD MEMBER [aqtcore_migrate];   -- some migrations read existing data
ALTER ROLE db_datawriter ADD MEMBER [aqtcore_migrate];   -- and seed/backfill rows
-- (db_owner also works and is simpler, but is broader than necessary — pick per your policy.)

-- Do NOT add this login to the runtime app's connection string. It is used ONLY by the migrate Job.
```

Hand the connection string to ops (or put it directly in Vault — step 2). Format:
`Server=<prod-sql-host>,1433;Database=AqtCoreFW;User Id=aqtcore_migrate;Password=<...>;Encrypt=True;TrustServerCertificate=False;`

---

## 2. Ops — store the migration connection string in Vault + expose it via ESO

1. Write the connection string to the same Vault KV path AqtCoreFW's ESO `SecretStore` reads from (the one that already backs `db-connection-string`), under a new property — e.g. `db-migration-connection-string`.
2. Add a `data:` entry to the `ExternalSecret` that produces the `aqtcorefw-secrets` K8s Secret:
   ```yaml
   # ExternalSecret for aqtcorefw-secrets (lives in the infra repo / cluster bootstrap, not this repo)
   spec:
     data:
       - secretKey: db-connection-string            # existing — runtime login
         remoteRef: { key: secret/data/aqtcorefw/<env>, property: db-connection-string }
       - secretKey: db-migration-connection-string  # NEW — elevated migration login
         remoteRef: { key: secret/data/aqtcorefw/<env>, property: db-migration-connection-string }
   ```
3. Confirm ESO synced: `kubectl get secret aqtcorefw-secrets -o jsonpath='{.data.db-migration-connection-string}' | base64 -d` returns the expected string.

> If the cluster does **not** use ESO: add `db-migration-connection-string` to whatever creates `aqtcorefw-secrets` (Sealed Secret, `kubectl create secret` in a CD step, etc.). The chart only references `secretKeyRef: name: <release>-secret, key: db-migration-connection-string`.

---

## 3. Repo (dev, ~15 min) — uncomment the prepared hooks

On a feature branch:

1. `infra/helm/aqtcorefw/templates/db-migrate-job.yaml` — uncomment the `ConnectionStrings__Default` override under the Job's `env:` (it points at `secretKeyRef … key: db-migration-connection-string`). An `env` entry overrides the same key carried by `envFrom`, so the Job now connects as `aqtcore_migrate` while still inheriting the rest of the config/secret.
2. `infra/helm/aqtcorefw/values-prod.yaml` — uncomment the `extraEnv:` block with `SKIP_STARTUP_MIGRATIONS: "true"` (appended after `values.yaml`'s `env` entry, so the `"true"` wins — last duplicate env var takes precedence).
3. (Optional cleanup, low priority) add a one-line note to `infra/helm/authserver/values.yaml` that AuthServer keeps `SKIP_STARTUP_MIGRATIONS` unset (no migrate Job in its chart → it keeps `db_ddladmin`; accepted residual — blast radius is "can disable a SECURITY POLICY", not "read cross-tenant"); and `grep -rn "migrationBuilder.Sql(" src/**/Migrations/` to confirm no raw-SQL migration is non-idempotent across a Job retry (EF wraps each migration in a transaction, so this is belt-and-suspenders).
4. Bump `docs/project-changelog.md`; flip the backlog item to DONE. Open a PR; merge after review.

---

## 4. Ops — staged rollout + verification

1. **Staging first.** `helm upgrade <release> infra/helm/aqtcorefw -f infra/helm/aqtcorefw/values-staging.yaml ...` (staging values may keep `SKIP_STARTUP_MIGRATIONS` unset — your call; the point of the staging run is to exercise the new migrate-Job override).
2. Watch the `pre-upgrade` Job: `kubectl get jobs -l app.kubernetes.io/component=db-migrate -w`; `kubectl logs job/<release>-db-migrate`. Expect: connects, applies pending migrations (or "no pending"), exits `0`. If it `backoffLimit`-exhausts (3 failures), the helm hook fails and the upgrade aborts — that is the intended fail-fast; do **not** proceed.
3. Bring the prod values online (`values-prod.yaml` now has `SKIP_STARTUP_MIGRATIONS=true`). On the prod `helm upgrade`:
   - `pre-upgrade` Job runs under `aqtcore_migrate` (verify in its logs / `ORIGINAL_LOGIN()` if you add a diagnostic) — applies migrations — exits 0.
   - Runtime pods start with `SKIP_STARTUP_MIGRATIONS=true` → they log `"Startup migrations skipped (SKIP_STARTUP_MIGRATIONS) — DB schema must be migrated out-of-band …"` and serve traffic. They do **not** run `MigrateAsync()`.
4. Confirm: app health endpoints green; `RlsPrincipalValidator` logs **no** `Critical` (it now also won't see `db_ddladmin` once step 5 is done — but `db_ddladmin` was never a Critical, only `db_owner`/`sysadmin`/`db_securityadmin` are; the validator's job here is mainly to confirm the runtime login isn't accidentally over-privileged).

---

## 5. DBA — revoke `db_ddladmin` from the runtime login

Only after step 4 confirms the migrate Job ran under `aqtcore_migrate` and the runtime pods are healthy with `SKIP_STARTUP_MIGRATIONS=true`:

```sql
USE [AqtCoreFW];
ALTER ROLE db_ddladmin DROP MEMBER [aqtcore_app];   -- runtime login is now data-plane only
```

Update `docs/deployment-guide.md` §4's provisioning SQL comment if it still shows `db_ddladmin` as granted for new environments (for fresh environments, just never grant it once this pattern is in place).

---

## 6. Verification query (run as the **runtime** login)

```sql
-- e.g. sqlcmd -S <prod-sql-host> -U aqtcore_app -P <...> -d AqtCoreFW
SELECT
    IS_SRVROLEMEMBER('sysadmin')    AS is_sysadmin,        -- must be 0
    IS_MEMBER('db_owner')           AS is_db_owner,        -- must be 0
    IS_MEMBER('db_securityadmin')   AS is_db_securityadmin,-- must be 0
    IS_MEMBER('db_ddladmin')        AS is_db_ddladmin,     -- must be 0 in steady state (1 only if SKIP_STARTUP_MIGRATIONS is unset)
    IS_MEMBER('db_datareader')      AS is_db_datareader,   -- 1
    IS_MEMBER('db_datawriter')      AS is_db_datawriter,   -- 1
    ORIGINAL_LOGIN()                AS login_name,
    DB_NAME()                       AS database_name;
```

Also run it as `aqtcore_migrate` to confirm *that* login has `db_ddladmin` (and `ALTER ANY SECURITY POLICY` — `SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY SECURITY POLICY');`).

---

## Rollback / failure handling

- **Migrate Job fails on prod upgrade:** the `pre-upgrade` hook fails → Helm aborts the upgrade → old runtime pods keep running on the old schema (untouched). Fix the migration / the `aqtcore_migrate` grants, re-run `helm upgrade`. No data loss; expand-contract guarantees the old app version still works against whatever schema state exists.
- **Need to roll the app back (`helm rollback`):** the migrate Job is `pre-install,pre-upgrade` only — it does **not** run on rollback, so the schema stays at the newer version. This is safe **iff** the expand-contract discipline is followed (no "contract"/destructive migration ships in the same release that stopped using the column). `infra/scripts/check-destructive-migrations.sh` guards this in CI.
- **You revoked `db_ddladmin` from the runtime login too early** (before the Job override was live) and a later upgrade's Job fails: temporarily re-grant `db_ddladmin` to the runtime login OR run `infra/scripts/run-migrations.sh` (with an elevated login) out-of-band, then complete step 3's chart change.

---

## AuthServer note

The Helm chart `infra/helm/authserver/` has **no** migrate Job. AuthServer migrates only `IdentityDbContext` + `MessagingDbContext`, both of which the Api migrate Job already covers — so when Api and AuthServer deploy together (Api chart's `pre-install` hook runs first), AuthServer finds nothing pending. But Helm does not enforce ordering across separate releases, so the conservative default is: **leave `SKIP_STARTUP_MIGRATIONS` unset for AuthServer** → it migrates on boot → it keeps `db_ddladmin`. Accepted residual (it cannot read cross-tenant; it can only `ALTER` a policy). If you want AuthServer's runtime login DDL-free too, add a `db-migrate` Job to `infra/helm/authserver/` mirroring the Api chart's (same `aqtcore_migrate` login) and set `SKIP_STARTUP_MIGRATIONS=true` for it — out of scope for the initial rollout.
