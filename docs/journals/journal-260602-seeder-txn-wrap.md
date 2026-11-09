# Journal — 2026-06-02 · Seeder Transaction Wrapping (P4+P5)

**Commits:** `f058dd9b` fix + `fd6cac58` docs (v2.137p143), pushed origin + bitbucket, ahead=0.

## What

Hardened `DemoWorkflowSeeder` (P4) and `DemoFormTemplateSeeder` (P5) against partial-failure idempotency loss via explicit transaction wrapping, mirroring the already-shipped P6 `DemoBusinessProcessSeeder` pattern. Both seeders execute multi-SaveChanges sequences (P4: 3-phase def→states→transitions→assignments, P5: 2-phase template+fields+options→publish+snapshot). Without transaction wrapping, a phase-2 fault left a committed root entity with missing children; plain restart would skip the entire seeder (idempotency probe on root exists). reset-all self-heals; a live-DB crash without reset would not.

## The Brutal Truth

This is exactly the kind of defensive measure that feels paranoid until it costs 3 hours on a Sunday debugging a half-seeded demo tenant. Code-review M1 on P6 surfaced it; we filed the backlog row immediately rather than defer. P4/P5 were the high-cadence shipping of the day — easy to rationalize skipping the txn-wrap now and "coming back to it." Advisor flat-out said: this pattern repeats P6, test it the same way, merge it same-day. Zero scope creep, zero complexity added, identical to proven pattern. The friction of doing it right here is less than the friction of firefighting later.

## Technical Details

**P4 DemoWorkflowSeeder (3-phase):**
- Phase 1: `def + states` → SaveChanges → state Ids back-filled.
- Phase 2: `transitions` → SaveChanges → transition Ids back-filled.
- Phase 3: `ByRole assignments` (keyed by transition.Id) → SaveChanges.
- Wrapped: `await using var tx = await ctx.Database.BeginTransactionAsync(ct)` BEFORE phase 1 (AFTER idempotency probe early-return), `await tx.CommitAsync(ct)` after phase 3.

**P5 DemoFormTemplateSeeder (2-phase):**
- Phase 1: `template + fields + options (cascade-insert)` → one SaveChanges.
- Phase 2: `PublishAndSnapshot + version` → SaveChanges.
- Wrapped identically.

**Idempotency probe isolation:** `(TenantId, DefinitionKey)` for P4 and `(TenantId, Code)` for P5 probes run OUTSIDE the transaction (early return if exists). Per-tenant scope inside seed method — one tenant's rollback doesn't affect another.

## What We Tried

1. **Skip wrapping for now (REJECTED)** — queued as future work. Advisor: P6 proved the pattern; ship same-day. Cost of deferral > cost of doing now.
2. **Retry-on-failure strategy as mitigation (RULED OUT)** — grepped entire repo for `EnableRetryOnFailure`: found only in 6 unit tests, ZERO in prod/fixture. Code-review independently verified prod registrations (`WorkflowInfrastructureModule.cs:39` + `FormsInfrastructureModule.cs:26`) configure only `MigrationsHistoryTable`, no strategy. Calling `BeginTransactionAsync` within a strategy context throws; direct repo evidence beats theoretical concern.

## Root Cause of the Gap

P6 code-review caught the partial-failure hole. P4/P5 were already built with same pattern (multi-SaveChanges, no wrapping) before the M1 feedback existed. Rather than ignore M1 or defer, we fixed the root pattern upstream (P4/P5) same-day to close the family-wide vulnerability.

## Lessons

1. **Advisor green-light beats fatigue-delay.** This was ship #25 of a 24+ ship day. Fatigue says "defer, that's future work." Advisor says "proven pattern, zero scope creep, tests are already there, ship now." We shipped.
2. **One subtlety: EF IDENTITY back-fill within transaction.** P4's `foreach (var transition in def.Transitions)` loop (phase 3) relies on phase-2 SaveChanges to assign `Transition.Id` via EF IDENTITY. Wrapping in a tx doesn't change that — SaveChanges still flushes + assigns within the open tx. Advisor confirmed before impl.
3. **Tests ARE the gate, not build.** A retry-strategy nesting throw is invisible to C#; real-SQL suites prove the new tx path actually works. Both P4 and P5 tests exercise the wrapped path (SqlServerCollection/WebAppFixture).
4. **Mechanism equivalence as proof.** P6 already did this. No new research, no new assumptions — exact mirror.

## Verification

Build `src/AqtCoreFW.slnx` 0W/0E · `DemoWorkflowSeederTests` 4/4 real-SQL (all phases in tx) · `DemoFormTemplateSeederTests` 4/4 real-SQL (both phases in tx) · code-review APPROVE (0 Crit/0 High/0 Med/0 Low). No migration / no entity / no signature change / no call-site churn. Diffs: +16 lines total (await using, BeginTransactionAsync, CommitAsync).

## Carry-forward

- **Manual smoke owed via `WF-DEMO-SEED-SMOKE`** — reset-all → activate → instance → inbox for P4, plus intake form fill for P5 (independent of txn-wrap, but bundled in that row).
- **Residual (accepted, same as P6):** txn-rollback path + StartAsync boot path NOT fault-injection-tested. Gated OFF, dev-only. Production would need explicit chaos-probe if ever enabled.
- **P4+P5 now txn-wrap parity with P6/7c/7a/7b.** Family-wide idempotency hardened.
