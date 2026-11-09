# Journal — P6 module unit-test backfill (plan 260620-2053 complete)

**Date:** 2026-06-21 20:52 ICT
**Trigger:** `/cook P6 full` — final phase of the BE architecture/DI remediation plan
**Type:** test-only (zero production code change)

## What happened

`/cook P6 full` asked to execute Phase 6 (backfill unit tests for MasterData / Organization /
SystemParams, plus optional Domain/Infra tests). The phase row claimed those three modules **had
no unit-test project**.

Per INVARIANT 260522-A, I probed ground-truth before opening the row — and the probe **falsified the
primary premise**. `find tests -name '*.csproj'` showed `AqtCoreFW.MasterData.Tests`,
`AqtCoreFW.Organization.Tests`, and `AqtCoreFW.SystemParams.Tests` already exist with substantial
behavior coverage (created March 2026, commit `f1bbdd7c`). The service logic the phase named —
`FeatureFlagService`, `SystemParamService` cache, `OrgUnitService`, `OrgTreeSeeder` — is already
covered. The plan snapshot had simply decayed ~3 months relative to the repo.

So "P6 full" honestly meant: close the *genuine residual* gaps, not recreate existing projects.

## Real gaps closed

1. **MasterData `AdministrativeUnitService`** — the one service the phase named that had **zero**
   coverage. It's the Singleton in-memory cache powering VN admin-reform code remapping (merged/
   dissolved units → current active code). Added `Services/AdministrativeUnitServiceTests.cs` (7
   tests) to the existing project, mirroring the `SystemParamServiceCacheTests` InMemory-EF +
   Singleton-lifetime pattern. Pinned: multi-hop successor-chain walk, cycle-guard termination,
   `GetByLevel` level/parent filter, `GetRegionCodes` historical grouping, pre-warm null-cache
   degradation.
2. **Notifications.Domain** — no `.Domain.Tests` project existed (10 sibling modules have one).
   Created `AqtCoreFW.Notifications.Domain.Tests` (14 cases): `Notification` status state machine +
   `MarkAsRead` idempotency guard + creation-event contract (event carries PublicId not internal Id;
   MessageId from the random generator), `NotificationChannel.From` parse-with-validation + value
   equality.

## Intentionally skipped (documented YAGNI)

- **Ai.Domain `AiErrors`** — static error-string factory; a test would assert string literals back to
  themselves (tautology, which the phase itself forbids). Entities are anemic.
- **Collaboration / Integration Infrastructure** — EF-repository / adapter tier
  (`ConversationRepository`, `MessageRepository`, `PresenceCacheService`). Better exercised by the
  integration suite with a real DB than unit-faked, per `development-rules.md` (no mocks to satisfy a
  check). Deferred to the integration tier rather than faked green.

## Anti-false-green discipline

Red-before-green proven 2×:
- Breaking the multi-hop walk (`while(true)`→`while(false)`) turned
  `BuildCacheAsync_ResolvesMultiHopSuccessorChain` RED while 6 siblings stayed green.
- Removing `if (IsRead) return` turned `MarkAsRead_CalledTwice_IsIdempotent` RED (ReadAt re-stamped
  on a different tick — the same-tick flakiness risk I flagged did not materialize).
Both production breaks reverted; `git diff` on `src/` is empty.

Code-review verdict: **APPROVE**. The one applied fix was LOW-1 — the cyclic test originally asserted
the *exact* one-hop landing node (`Be("B")`), an artifact of where the loop `break` lands; loosened to
`BeOneOf("A","B")` so it pins the real contract (terminates with a stable in-cycle value) and survives
a behavior-preserving refactor. The reviewer also confirmed zero tautologies — materially better than
the pre-existing weak `CreateDictionaryCommandHandlerTests` (which only asserts `NotBeNull()`).

## Outcome

- Plan `260620-2053-be-architecture-di-remediation` now **6/6 complete**.
- 21 new tests; MasterData 144/144, Notifications.Domain 14/14.
- `src/AqtCoreFW.slnx` +1 project line; no production source touched.

## Lessons

- **A plan's "missing X" claim is the most decay-prone kind of row** — capability gaps get filled by
  unrelated work between plan-authoring and plan-execution. Probe `find`/file-exists on the *specific*
  artifact the row names before opening; the 30-second probe saved recreating three existing projects.
- **"Full scope" against a falsified premise ≠ blind completion.** The disciplined reading was to close
  the genuine residue and document the non-gaps with rationale, not to manufacture tautological test
  projects (Ai.Domain) to check a box — which the phase's own "tránh test tautogen" rule forbids.
- **A pre-existing weak test (`NotBeNull()`) is a latent false-green** worth a follow-up, but not in
  scope to rewrite here.

## Follow-ups (not filed as blockers)

- `CreateDictionaryCommandHandlerTests` / `Handler_HasCorrectDependencies` in MasterData.Tests are
  structural `NotBeNull()` smoke tests that prove no behavior — candidate to upgrade to real
  InMemory-EF handler tests (same pattern now demonstrated by `AdministrativeUnitServiceTests`).
- Collaboration `PresenceCacheService` has cache logic that *could* warrant a focused unit test if it
  grows beyond a thin Redis wrapper — revisit if it accretes branching.
