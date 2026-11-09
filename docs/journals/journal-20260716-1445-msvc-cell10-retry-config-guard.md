# Cell #10 Retry-Config Guard — and the Build-Now Code Track Runs Dry

**Date**: 2026-07-16 14:45
**Severity**: Low (test-only T4 arch-guard, no incident) — but a strategic inflection
**Component**: `MessagingRegistration` MassTransit transport config (shared Infrastructure); `AqtCoreFW.Architecture.Tests`
**Status**: Resolved — `MSVC-CELL10-RETRY-CONFIG-GUARD` shipped, commit pending

## What Happened

Cooked the last cheap unblocked cell #10 item: a static-source architecture guard (`MessageRetryConfigGuardTests`) asserting that both MassTransit transports in `MessagingRegistration.cs` (`UsingInMemory` + `UsingRabbitMq`) keep `UseMessageRetry`. A `[Theory]` with 2 cases reads the committed source via `FindRepoRoot()`, extracts each transport lambda body brace-depth-aware, and asserts the call is present. Mirrors the existing `MarsConnectionStringGuardTests`.

With it shipped, **the completeness-matrix "build-now" CODE track is exhausted** — no unblocked microservices code items remain (cells #5/#6 extraction-gated, #7 follow-ups compliance/auditor-blocked, #3 provider-half extraction-gated, cell #10 now fully monolith-covered). The sole remaining microservices action is **FU-A** (the GOV auditor question) — non-code, user-owned.

## The Brutal Truth

This slice is a T4 guard with near-zero runtime value *today* — `UseMessageRetry` is already present on both transports. Its worth is entirely preventive: it stops a future refactor from silently deleting retry config and converting every throws-to-retry consumer to first-fault dead-letter (permanent message loss) with no test to catch it. That's a real, cheap insurance policy — but calling it "progress on microservices" would be dishonest. The honest read is the opposite: shipping it is the signal that the code track has bottomed out. Everything left needs either a physical service (extraction) or an auditor's answer. Continuing to mine the monolith for microservices code would be manufacturing work.

## Technical Details

- **Why static-source, not behavioural:** RabbitMQ is prod-only (`Bus:Transport` defaults InMemory; no test broker). A MassTransit harness test can only exercise the InMemory retry path, leaving the RabbitMQ transport's config unguarded. Only a source-text assertion covers both. This is the same reasoning as `MarsConnectionStringGuardTests` and the Helm-policy static-source guard ([[feedback-no-helm-conftest-use-static-source-policy-test]]).
- **Parser:** `ExtractLambdaBlock` finds the marker, then brace-depth-counts from the first `{` to the matching `}` — so the RabbitMQ block's nested `cfg.Host(..., h => { ... })` lambda is consumed within the outer body, not mistaken for its end. Markers verified unique (`UsingInMemory`/`UsingRabbitMq` each appear once; no collision with `UseInMemoryOutbox`/`ConfigureRabbitMq`).

## What We Tried

- Mutation-proof: deleting the `UseMessageRetry` statement from ONLY the InMemory block flipped the `UsingInMemory` case RED while `UsingRabbitMq` stayed GREEN (Failed:1/Passed:1). One mutation proved both non-vacuity AND that the parser discriminates the two blocks (a conflating parser would have failed both or the wrong one). Reverted via Edit; prod diff empty.
- Full `Architecture.Tests` 180/180 (no regression). Build 0W/0E.

## Root Cause Analysis

N/A (no defect). The guard exists because the retry-config is load-bearing but had no test — a coverage gap slice-2 identified and deferred rather than silently leaving open.

## Lessons Learned

- **A prod-only transport (RabbitMQ) can only be config-guarded by static source parsing** — behavioural harness tests cover the in-process transport only. When a config invariant spans a transport you can't spin up in tests, assert on the registration source text, nesting-aware, and mutation-prove it.
- **Shipping the last cheap item is a signal, not a milestone.** When the only remaining code work is preventive T4 guards, the real bottleneck is elsewhere (here: FU-A, an auditor question). Recognise track-exhaustion and surface it rather than inventing more monolith-side code.

## Next Steps

- Commit (1 test + 4 docs), `Closes: MSVC-CELL10-RETRY-CONFIG-GUARD`.
- **FU-A is now the whole game for microservices** — put the drafted GOV auditor question (`docs/microservices-compliance-isolation-auditor-questions.md`) to the auditor. Its answer either reopens physical extraction (S1/S2) or confirms the monolith-with-schema-GRANT posture is compliance-sufficient. No code can advance the transformation past this point.
- Feature-dev/polish is the only other code track if the user wants to stay in code while FU-A is pending.
