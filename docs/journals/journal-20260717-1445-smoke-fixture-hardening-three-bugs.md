# Smoke Suite Green: One Symptom, Three Bugs (Fixture Race, Hangfire Static, AuthServer DI Gap)

**Date**: 2026-07-17 14:45
**Severity**: High
**Component**: CI smoke-gate (`Category=Smoke`), test fixtures, AuthServer DI, Hangfire boot registration
**Status**: Resolved (local-verified, not yet committed)

## What Happened

Asked "how do I verify the CI smoke-gate green locally since GitHub Actions is disabled" — solo dev, private repo, Free-tier Actions minutes not worth spending. That question turned into `/ak-cook SMOKE-FIXTURE-ENV-VAR-RACE-HARDENING` and a multi-hour root-cause hunt. Symptom: `SqlException: connection refused` on 6/9 startup smoke host-boot tests when run as a full suite, but green when run isolated — classic shared-state flake fingerprint. Turned out to be THREE unrelated bugs stacked behind the same error message.

## The Brutal Truth

Fixed the "obvious" root cause (env-var race) exactly as a subagent (kongming) prescribed, source citations and all — suite still red. That's the gut-punch moment: confident, well-cited advice that's still wrong because it was never actually run against the suite. Burned a full investigation cycle before the repo's own code disagreed with the advice loudly enough to force a second look. The upside: this same red gate, if it had been silently skipped or left `if:false` longer, would have shipped a DI-crash-on-login regression to prod. The gate did its job — just not the job we thought it was doing.

## Technical Details

1. **Fixture env-var race** — 3 smoke host fixtures mutated process-global `ConnectionStrings__Default` in Init/Dispose. Overlapping `IClassFixture` lifecycles across parallel test classes raced the single env-var slot. Fix: new `UseTestConnectionStrings` extension calling `builder.UseSetting` (per-host config, not process-global); deleted all env-var mutation from fixtures.
2. **Hangfire static frozen (the real suite-killer)** — static `RecurringJob.AddOrUpdate` binds `JobStorage.Current` ONCE per process, onto whichever host boots first. Every subsequent host in the multi-host smoke suite registered its recurring jobs against the FIRST host's (by-then-disposed) Testcontainer — same "connection refused" fingerprint as bug #1, which is exactly why it hid behind the first fix. Fix: gated the boot-path registration in `Api/Program.cs` behind `Hangfire:RegisterRecurringJobs` (default true, prod byte-unchanged); smoke fixtures set it false.
3. **AuthServer DI regression (real prod bug, highest-value find)** — `AuditService` gained a dependency on `AuditEntryPersister` in commit `fa0faff2` (2026-07-13/14 microservices arc). AuthServer's manual audit DI registration was never updated. Result: DI activation failure on first audited login in AuthServer. Masked until now because the smoke gate had never once gone green to catch it. 4th occurrence of "AuthServer manual-DI missing transitive dep" (prior: `IMessageBus`, `IGuidGenerator` x2). Fix: `AddScoped<AuditEntryPersister>()`.

New regression coverage: `FixtureConnectionStringIsolationTests.cs` — hostile-env invariant test + static-source guard.

## What We Tried

- kongming v1 (env-var race only, `UseSetting` swap) — implemented in full, suite still failed. The repo's own `WebAppFixture:196` comment plus a new hostile-env test proved v1 incomplete, not wrong — just partial.
- Re-consulted kongming with the counter-evidence (failing test + comment). v2 (Hangfire static `JobStorage.Current`) was correct, but only provable because we actually ran the full suite against it instead of trusting the citation.
- kongming's Hangfire fix proposal was heavy: convert ~21 `RecurringJob` static call-sites to DI across 9 files. Rejected for KISS — one config-flag gate in `Program.cs` plus 3 fixture-line changes covers the same test scenario at a fraction of the blast radius.

## Root Cause Analysis

Not one bug wearing a disguise — three independent bugs sharing one SqlException fingerprint, layered so that fixing the outer one exposed the next. The fixture race was real but not sufficient. The Hangfire static-singleton-per-process assumption is a landmine for any multi-host-per-process test topology (which the smoke suite is, by design, to keep runtime sane). The AuthServer DI gap is the oldest and most boring category of bug: an app-layer dependency added during a refactor without touching the *other* app's manual DI registration for the same service — 4th time this exact shape has bitten this codebase.

## Lessons Learned

- Source-cited advisory output (kongming or otherwise) still needs an empirical gate. "Here's the file and line" is not "I ran it and it passed." Implement, then run the actual suite, before calling a fix done.
- One error message can be N bugs. Don't stop investigating at the first plausible cause if the fix doesn't turn the light green — diff the *remaining* failures, don't just re-read the first stack trace.
- A long-red gate isn't just noise to silence — it can be hiding a real regression underneath the infra flake. The value of finally getting it green was catching bug #3, not just satisfying the CI checkbox.
- Prefer the smallest fix that covers the tested scenario. kongming's 21-site DI refactor was over-engineering for what a 4-line flag gate solved.
- AuthServer's manual DI registration is a recurring footgun (4/4 now). Worth a standing check: any new dependency added to a service consumed by AuthServer's manual container needs a grep of AuthServer's registration file in the same commit.

## Next Steps

- Commit is pending (git-manager) on branch `feat/p1-stable-backup-and-ci-smoke` — not yet done as of this entry, code-review already APPROVE (0 Critical / 0 High, 2 Low non-blocking).
- Closure criterion for this row is local-verify (11/11 green x3 consecutive runs, no flake observed), not CI-observed green — GitHub Actions stays disabled by user decision (Free-tier cost, solo dev, private repo).
- Owner: solo dev (user). No further action beyond the pending commit; consider a lightweight lint/grep check for "AuthServer manual DI missing transitive dep" pattern if it recurs a 5th time.

**Status: DONE**
