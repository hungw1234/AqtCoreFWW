# ADR-010: AuthServer Composition-Root Divergences (ValidateOnBuild + Startup Migration Scope)

**Date:** 2026-06-21
**Status:** Accepted
**Deciders:** AqtCoreFW Architecture Team

## Context

The solution has two composition roots: `AqtCoreFW.Api` (the main application host) and
`AqtCoreFW.AuthServer` (the OpenIddict identity/login host). They deliberately share a large
amount of infrastructure wiring — and an architecture/DI audit flagged two places where the
two hosts diverge, asking whether the divergence is a defect or intentional:

1. **`ValidateOnBuild`** — Api enables `ValidateOnBuild = true` (Development/Staging only) as a
   DI fail-fast net; AuthServer sets `ValidateOnBuild = false`.
2. **Startup migration scope** — AuthServer *registers* three DbContexts
   (`IdentityDbContext`, `AuditDbContext`, `SystemParamsDbContext`) but only *migrates* a
   subset at startup (`IdentityDbContext` + `MessagingDbContext`), whereas Api migrates the
   full set of ~19 contexts.

This ADR records that both divergences are intentional, so future audits do not "fix" them
into regressions.

## Decision

### 1. AuthServer keeps `ValidateOnBuild = false`

AuthServer registers the Identity.Application MediatR assembly to serve **consent handlers at
runtime**, but the broader Identity.Application handler set depends on full infrastructure
(Dapper repositories, read-model services) that is registered **only in `AqtCoreFW.Api`**.
Turning on `ValidateOnBuild` in AuthServer would fail the build graph validation on those
Api-only dependencies even though AuthServer never resolves those handlers.

Therefore AuthServer keeps `ValidateOnBuild = false` and relies on Api's `ValidateOnBuild`
pass for the shared graph. This is a documented, intentional asymmetry — per the project's
review/decision rule, a documented decision is not reversed on an abstract audit concern
without new evidence. It should only be flipped to `true` if the cross-module Identity
adapters are consolidated such that AuthServer can resolve its handler set standalone.

### 2. AuthServer migrates a strict subset; Api/Helm owns the full schema

AuthServer is **never the sole migrator** of any schema. The contexts it migrates at startup
(`IdentityDbContext`, `MessagingDbContext`) are a strict subset of Api's migrate set. The two
contexts it registers but does not migrate (`AuditDbContext`, `SystemParamsDbContext`) are
covered by:
- the Api host's startup migrations, and
- in Kubernetes, the Helm `db-migrate` Job (which runs on the Api image with an elevated login).

This keeps AuthServer's runtime DB login least-privileged (it need not hold schema-DDL rights
for Audit/SystemParams) and avoids duplicate migration ownership. The startup path also honors
`SKIP_STARTUP_MIGRATIONS` so the runtime login can drop `db_ddladmin` entirely (see
`docs/deployment-guide.md` §4).

## Consequences

### Positive
- Two intentional divergences are now documented; audits can cite this ADR instead of
  re-litigating them.
- AuthServer stays minimal (consent/login only) and least-privileged at runtime.
- Single migration ownership (Api/Helm) avoids split-brain schema drift.

### Negative
- AuthServer loses the `ValidateOnBuild` fail-fast net for its own (small) graph; mitigated by
  Api's pass over the shared graph and by AuthServer's narrow handler surface.
- The register-but-not-migrate asymmetry is non-obvious; mitigated by inline comments at the
  registration/migration sites and by this ADR.

## Invariant

AuthServer's startup-migrated DbContext set MUST remain a subset of Api's startup-migrated set.
If a future context is migrated *only* by AuthServer, that is a defect (AuthServer would become
the sole schema owner for a context whose login is intentionally least-privileged).

This invariant is **enforced by `MigrateSetSubsetGuardTests`** (architecture suite). Each host
declares its startup-migrate set as data — `AqtCoreFW.Api.StartupMigrationSets.Contexts` and
`AqtCoreFW.AuthServer.StartupMigrationSets.Contexts` — which the composition roots iterate to run
migrations AND the guard reads to assert the subset relationship (non-brittle: no text-scan of
Program.cs). The same declared set also backs `ModuleStartupWiringTests` (every module-registered
DbContext must appear in Api's migrate set).

## Related

- `src/host/AqtCoreFW.Api/Program.cs`, `src/host/AqtCoreFW.AuthServer/Program.cs`
- `docs/deployment-guide.md` §4 (Application DB Principal / least-privilege login)
- ADR-003 (SQL Server per-module schema)
