# Journal — ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK (make the opt-in isolation boundary observable)

**Date:** 2026-07-14 (evening) · **Branch:** main · **Row:** ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK (T4, 🔵 Low)
**Follows:** ISO-DAPPER-KEYED-DI-INCREMENT Phase 3 (journal-20260714-1507) — this was the T4 follow-up filed there.

## What shipped (API host only, 4 changed + 2 new files, uncommitted at session end)
`NonAuditConnectionString.Get()` has been `NonAudit ?? Default` since the EF-first isolation increment — deliberately
opt-in so a prod deploy that never provisioned the least-privilege `NonAudit` login still boots. The gap: it booted
**silently**. Isolation OFF, non-audit module reads running on the broad `Default` login (which can reach `[audit]`),
and nothing told an operator. This slice closes that visibility gap without touching the opt-in behavior itself:

1. `NonAuditConnectionString.IsExplicitlyConfigured(IConfiguration)` — new helper distinguishing "NonAudit key present"
   from "fell back to Default".
2. `NonAuditIsolationHealthCheck : IHealthCheck` (new, `src/shared/AqtCoreFW.Infrastructure/Configuration/`) —
   `Production && NonAudit unset` → **Degraded** (deliberately never Unhealthy — keeps the pod in rotation at HTTP 200,
   preserves the opt-in contract); Healthy everywhere else (dev/test/CI expected-unset stays clean, no readiness
   noise).
3. Registered in `AddGovHealthChecks` (`HealthCheckConfiguration.cs`), tagged `audit-isolation` + `ready`.
4. Warning-only startup log line in `AqtCoreFW.Api/Program.cs` — **not** fail-fast, same reasoning as the health
   check: don't break the opt-in, just stop being quiet about it. This is where the operator-actionable detail
   (script name, mechanism) actually lives.
5. Tests: `NonAuditConnectionStringTests` +3 (helper truth table), new `NonAuditIsolationHealthCheckTests` (3 cases:
   Production+unset=Degraded, Production+set=Healthy, non-Production+unset=Healthy).

## The one design decision worth remembering
`/health/ready` is **unauthenticated** and echoes each check's `Description` verbatim. First-draft description named
the schema and the provisioning script path — a fine operator hint, terrible thing to hand an anonymous caller (info
leak: confirms `[audit]` exists, confirms isolation posture, hints at internal script layout). Code review flagged it
(L3), fixed same-slice: `/health/ready` descriptions are now generic ("Audit read-isolation dormant in Production —
NonAudit login not configured; see startup log."), and the specific script/mechanism detail moved to the startup log,
which is not internet-facing. Any future security-posture health check should default to this split — terse public
description, verbose private log — rather than discovering it via review.

## Verify
Full build 0W/0E. Unit 9/9 (new + extended). Arch-guards re-run for regression: `AuditBoundaryEfWiringGuardTests` +
`AuditKeyedReaderAllowlistTests` 6/6 unchanged. Code review: DONE_WITH_CONCERNS, 0 Crit/High/Med. Applied in-slice:
L3 (info-leak, above) and L6 (a stale comment referencing the pre-fix description wording). Left non-blocking and
documented rather than gold-plated: L1 (an edge divergence between `Get()`'s empty-string handling and the new
helper — same input space, worth a follow-up test but not a behavior bug today), L2 (prod rollout ordering — see
below), L5, L7.

## Scope call that halved the work
Scouted `AuthServer`'s `Program.cs` before planning and found it skips `AddInfrastructure()` (Program.cs:229) —
`NonAudit`/isolation wiring never touches that host. So this is API-host-only: one `Program.cs`, one health-check
registration, no speculative AuthServer edits. Cheap scout, avoided doing the same fix twice for no reason.

## Operational carry-forward (not a code task, but blocking full effect)
This ship means the **next** prod deploy will show `/health/ready` as Degraded — on purpose — until
`scripts/260714-nonaudit-login-provisioning.sql` is run and Vault `ConnectionStrings__NonAudit` is published. Whoever
owns the next prod rollout needs to either (a) sequence provisioning ahead of/with the deploy, or (b) make sure
on-call monitors don't page on Degraded before that happens. This is exactly the ops-activation gap the ISO-DAPPER
Phase 3 journal called out as "code-complete, OPS-ACTIVATION-PENDING" — this health check is what makes that
pending state visible instead of invisible, but it doesn't close it by itself.

## Lessons reinforced
- Terse-public / verbose-private description split for any health check exposing security posture — new pattern,
  worth keeping for the next isolation or credential-boundary signal.
- Degraded (not Unhealthy) is the correct severity when the underlying behavior is intentionally opt-in and fail-open
  by design; don't let a health check silently upgrade a documented soft boundary into a hard outage.
- [[feedback-red-team-before-implement]] — scouting `AuthServer`'s Program.cs before touching two hosts avoided a
  wasted edit.
- [[feedback-verify-subagent-reports]] — re-ran arch-guards + full build after the health-check + Program.cs changes
  rather than trusting the implementer's "0W/0E" claim at face value.
