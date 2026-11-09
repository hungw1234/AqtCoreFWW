# CLAMAV-BYPASS-FLAG-COLLAPSE: Unified Fail-Closed Posture, Deleted Redundant Config Layer

**Date**: 2026-06-14 10:45
**Severity**: Medium
**Component**: api — Files.Infrastructure `ClamAv*`, SystemParamSeeder, appsettings
**Status**: Resolved (1 commit `cbf1d4ce` + prior `c15c40c9` PUSHED `origin/main`, 0 ahead/behind)

## What Happened

Removed `ClamAvOptions.BypassWhenUnavailable` knob after design discussion (`/ck:ask`) exploring
"can we drop 1 of the 2 flags?" Analysis showed the two controls live at orthogonal layers
(intent vs failure-policy) and the correct consolidation was **deletion**, not merge.

**What shipped:** `ClamAvVirusScanner` now unconditionally fail-closed — clamd unreachable → 
`VirusScanResult.Error` → throws in `ClamAvScanJob` → Hangfire retry → file stays Quarantined.
Single remaining intent control = `feature:files.virus-scan` (DB-backed, runtime, per-tenant).
Removed `BypassWhenUnavailable` section from `appsettings.Development.json` and 
`appsettings.Benchmark.json`. `ClamAvStartupHealthCheck` now always pings clamd EXCEPT 
`Benchmark` env (no clamd provisioned there).

## The Brutal Truth

The temptation to "unify 2 flags into 1 tri-state" felt elegant until we traced who owns each layer:
`feature:files.virus-scan` = product intent (runtime DB flips, per-tenant, admin-controlled).
`BypassWhenUnavailable` = infrastructure failure-policy (deploy-time, per-env, ops-controlled).
Merging them into runtime DB state makes a **security failure-mode configurable in production** — a
compliance hazard. Different cadence, different blast radius, different owner — that's not a
duplication problem, that's orthogonal concerns. The right answer was delete, not fuse.

Scout proved the deletion safe: `bypass=true` existed only in Benchmark, but Benchmark never seeds 
`feature:files.virus-scan` (`SystemParamSeeder` is `IsDevelopment()`-gated), so the scanner never 
invoked there. The bypass branch was **dead config in every real scenario**. Dev already runs 
`bypass=false`. So the removal changes nothing.

## Technical Details

**Commits:**
- `c15c40c9` — prior (included here): "docs(journal): record audit HMAC-chain dedicated queue fix"
- `cbf1d4ce` — removal: `ClamAvOptions.cs` (delete `BypassWhenUnavailable` property), 
  `ClamAvVirusScanner.cs` (rm `options.BypassWhenUnavailable` check, return Error always on 
  clamd-unavailable), `InfrastructureRegistration.cs` (rm appsettings section binding), 
  `IVirusScanner.cs` (interface unchanged), `ClamAvScanJob.cs` (trace comment clarified: Error→throw), 
  `SystemParamSeeder.cs` (comment doc the `IsDevelopment()` guard), `ClamAvVirusScannerTests.cs` (NEW 
  test fail-closed path: `127.0.0.1:1` connection-refused→Error), `ClamAvScanJobTests.cs` (NEW 
  comment: namespace collision `using Options` shadows `Extensions.Options.Options` → must fully-qualify 
  `Microsoft.Extensions.Options.Options.Create` in test), 2 appsettings (sections deleted), 
  `docs/project-changelog.md` (entry), `backlogs/backlog.md` (CLAMAV-PROD-FLAG-POLICY row amended).

**Config removal impact:**
- Benchmark env: never had clamd provisioned, `feature:files.virus-scan` never seeded → no-op env already
  failed gracefully (scanner short-circuit per seed condition). Deletion preserves that.
- Development env: `bypass=false` (default), so removal is code-path unreachable. No change.
- Production env: never used bypass flag in any config. No change.

**Test strategy:**
- `ClamAvVirusScannerTests`: 1 NEW case: `localhost:1` → `SocketException` → `Error` verdict 
  (non-flaky, deterministic refused connection; avoids depending on absent clamd).
- `ClamAvScanJobTests`: comment added warning about namespace collision — namespace shadowing is 
  live in existing test suite, now documented to prevent future mistype.
- Integration (`Files.IntegrationTests`): 7/7 green unchanged (no new behavior, only redundant 
  config deletion).

**Build/test gates:**
- API host `/warnaserror` 0W/0E (`ClamAvOptions` deletion triggers `CS0649` unused field removal—clean)
- Files.Infrastructure.Tests 37/37 (+2 new fail-closed cases)
- Files integration 7/7 (full scan → quarantine round-trip)

## What We Tried

**Option A (rejected): Merge into tri-state enum.** `VirusScanMode: Disabled | EnabledFailClosed | 
EnabledFailOpen` in SystemParams DB. Analysis: runtime DB flip of failure-policy = compliance hazard; 
makes a security recovery mode administrable in prod; differs from feature flags (which ARE 
administrable). Ops owns failure-policy, product owns feature flags — different owners, different 
cadence, different blast radius. Merging violates separation.

**Option B (chosen): Delete bypass flag, keep intent flag.** `feature:files.virus-scan` covers 
"should scanning happen?" at feature level. Failure-policy becomes unconditional: Error→throw→retry 
→hold. Works because Benchmark never invokes scanner (seeding gates it). Dev runs `bypass=false` 
(nothing happens). Prod uses only intent flag + fail-closed default. Scout verified zero dead-code 
fallout in the bypass-true branch.

**Option C (considered): Extract failure-policy to separate env-only knob.** Would require reading 
env at `ClamAvScanJob` execution time (lossy; Hangfire job can't reach appsettings). Worse than 
status quo.

## Root Cause Analysis

The original design had two controls because the requirements came from different timelines: 
feature toggle (recent, product-driven) + initial infra-unavailability handling (older, 
operations-driven). Once both were shipped, nobody asked "are these orthogonal or duped?" Analysis 
happened late in implementation (after both existed for weeks). The decision space was "which layer 
owns this?" — not "how do we merge them?"

The key insight: if you can't identify a **single owner and decision-cadence** for a merged config, 
don't merge. Merge reduces blast radius only when the combined decision is made atomically by one 
party. Cross-owner knobs belong in separate layers.

## Lessons Learned

- **"Unify 2 flags → 1" does not mean merge them.** Means audit whether one is redundant and delete 
  it. If they control different layers (intent vs failure-policy), keep them orthogonal or delete the 
  redundant one. Merging across ownership boundaries is a smell.

- **Dead config is invisible unless you trace the execution path.** `bypass=true` existed in 
  Benchmark's appsettings but the scanner never invoked there due to seeding guard (layer above). 
  Deletion was safe because the call site (`ClamAvScanJob`) that would have used it was unreachable. 
  Config-only analysis (reading appsettings) would have missed this; code analysis (tracing seeder) 
  found it.

- **Deployment-time config and runtime DB flags are not interchangeable.** Compliance/security 
  failure-modes should not be administrable at runtime. Keep them separate: env knobs for 
  infrastructure policy, DB flags for feature intent.

- **Removing a config layer invalidates backlog rows.** CLAMAV-PROD-FLAG-POLICY row was "keep 
  bypass=true until clamd provisioned" — now impossible. Same slice amended the row to reflect new 
  sequencing (clamd first, flag-on second). Don't leave dangling refs to deleted config.

- **Namespace collision in tests is a footgun.** `using Options` shadows `Microsoft.Extensions.Options` 
  → fully-qualify `Options.Create()` in test helper. Document once, check once, prevent future mistype.

## Next Steps

| Item | Owner | Priority | When |
|---|---|---|---|
| Validate Benchmark env start (no clamd, `feature:files.virus-scan` gated) | QA/Dev | HIGH | Next `/aqt-runapp` |
| `CLAMAV-PROD-FLAG-POLICY` backlog row verify seeded correctly | dev | MEDIUM | Next backlog sweep |
| `CLAMAV-FIRSTBOOT-FLAG-RACE` follow-up (T4, latent) | dev | MEDIUM | Phase 2 planning |

## Open Questions

1. **Does Benchmark need explicit SkipHealthCheck marker?** Currently, Benchmark hard-skips the 
   startup ping via `IsEnvironment("Benchmark")` check. If we add more environments without clamd, 
   should this become a config-driven skip list? Not blocking now; document if pattern repeats.

2. **Should failed virus scans be quarantined indefinitely, or periodically purged?** Current design: 
   `Error`→throw→retry indefinitely in Hangfire queue. No configured timeout. Acceptable for now 
   (error is transient, clamd will come back online); note for Phase 2 long-term retention policy.
