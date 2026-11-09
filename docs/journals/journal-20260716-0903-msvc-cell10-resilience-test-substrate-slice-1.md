# Journal — MSVC Cell #10 Resilience Test-Substrate Slice-1 (verify + finalize)

**Date:** 2026-07-16 · **Type:** test-only, microservices completeness-matrix cell #10 slice-1 · **Plan:** `plans/260715-1738-msvc-cell-10-resilience-test-substrate-slice-1/`

## What shipped

Carry-over from 2026-07-15 evening: cell #10 slice-1 code was written but never verified/committed. This session verified it, a code-review caught a real regression, fixed it, then finalized.

- **Part A — hosted-service boot-guard.** `HostedServiceTestHelper.DriveHostedServiceAsync<T>` drives a real DI-resolved `IHostedService` through Start/Stop so a boot-time regression surfaces in a test, not prod startup. Strip predicate DRY-extracted to `HostedServiceStripFilter.ShouldRetain` + `WebAppFixture.HostedServicesToRetain` opt-in.
- **Part B — fault-dimension #1 (duplication).** Same event published twice through the real MassTransit harness → assert effect dispatched exactly once via claim-first `IProcessedMessageStore`. Non-vacuous (dedup-off mutation → RED, restore → GREEN).

## The regression the initial verify missed (and review caught)

My verification was thorough on the surface — build 0W/0E, 4 tests green, a mutation check, a prior-green blast-radius test — but **the blast-radius check was structurally blind to the actual regression.**

The first strip predicate retained ANY factory-lambda registration (`implType null → ImplementationFactory != null → retain`). I found 2 app services newly retained (`FeatureFlagSubscriber`, `FormMaterializationService`), verified both swallow/well-behaved, and called it "test-time cost only." **I missed that the same over-broad branch also retained Hangfire's `BackgroundJobServerHostedService`** (factory-registered, `ImplementationType==null`) — which the old predicate stripped via `Method.ReturnType`. A running Hangfire server polls SQL and *executes enqueued jobs mid-test*, racing assertions across ~150 fixture hosts (worse under `SeederSmokeFixture`'s Development env, which also runs `RegisterRecurringJobs`).

Why my blast-radius test (1 prior-green class) couldn't catch it: **a started hosted service does not hang boot** — `StartAsync` returns immediately, host boots "clean." The regression manifests only as flaky job-execution in tests that enqueue jobs, which needs either a full-suite run or a targeted assertion. "Host boots" was the wrong oracle.

## Fix

Restored the `ImplementationFactory.Method.ReturnType` resolution; fail-safe-retain ONLY the interface-pinned-collapse shape (return type == `IHostedService`, unrecoverable concrete type). Concrete factory types (Hangfire, FeatureFlagSubscriber, FormMaterialization) strip precisely as before. Added **proof #4** — a strip-branch regression guard asserting a Hangfire-shaped concrete factory non-seeder → `ShouldRetain == false` (would fail against the over-broad predicate). Also fixed 2 MEDIUM: a misleading "byte-identical" comment + repo-rule violations (plan-labels / memory-slug in code comments — the `[Trait("TestCase", ...)]` codes are an established 83-occurrence repo convention → kept).

## Verify (post-fix)

Build 0W/0E ×3. Part A 4/4 (host boot 55s→44s after re-stripping Hangfire + FeatureFlagSubscriber — a nice confirmation the fix took effect). Part B 1/1. Blast-radius prior-green 3/3.

## Lessons

1. **A fixture strip-predicate change needs a strip-branch assertion, not a boot-succeeds smoke.** A wrongly-retained hosted service boots silently; "host boots clean" cannot detect over-retention. The unit-level `ShouldRetain` predicate test (both retain AND strip branches) is the real oracle, cheaper and more decisive than a full-suite flakiness hunt.
2. **Independent code-review earns its keep even after thorough self-verification.** I enumerated the retained set from a grep of app-owned registrations and stopped there; the reviewer widened the census to third-party (Hangfire) factory registrations. When a predicate governs a whole class of registrations, enumerate the *framework/third-party* members too, not just first-party.
3. Reinforces `feedback-red-team-before-implement` + the census-completeness angle of `feedback-isolation-seam-must-cover-all-invariant-paths` (enumerate every member the predicate touches).

## Unresolved

- None blocking. Remaining cell #10 fault dimensions (partition / reordering / timeout / chaos + cross-service dedup) are later slices, some extraction-dependent.
