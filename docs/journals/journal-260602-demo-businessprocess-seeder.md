# P6 DemoBusinessProcessSeeder: Cross-Module Seeding Precedent

**Date**: 2026-06-02 14:30
**Severity**: Medium
**Component**: BusinessProcess.Infrastructure/Seeding (host-resident)
**Status**: Shipped (`f9e18323` feat + `fbe34b67` docs), v2.137p139, ahead=0

## What Happened

Shipped `DemoBusinessProcessSeeder` — a host-resident seeder that wires the P4 demo workflow definition + P5 demo intake form into a single Draft `BusinessProcessDefinition` per tenant (BTC+BNV). 1 root entity + 2 form maps (Draft state + Submit transition). Gated `BusinessProcess:DemoBusinessProcessSeed` OFF, idempotent on `(TenantId,Code="demo-business-process")`.

## The Brutal Truth

This was supposed to be modular. P4/P5 lived in module infrastructure. The natural instinct = extend IWorkflowModuleClient + IFormTemplateQueryClient with resolve-by-key methods, keep BP seeding inside `BusinessProcess.Infrastructure`. The architecture tests (NetArchTest `ModuleIsolationTests.NoModule_Should_DirectlyReference_AnotherModule`) shut that down hard. Direct module→module refs = illegal. RLS also forbids cross-schema SQL. So I was stuck: contracts expose only PublicId-keyed opaque lookup, not the natural-key (DefinitionKey, Code) or NAMED state/transition queries a seeder needs.

Spent 45 min hunting for a "right" module-based answer. There wasn't one. Advisor caught my false premise: I was worried a *host* seeder wouldn't satisfy `SeederMarkerContractTests`. Wrong — the contract anchors on `E2ETestDataSeeder.Assembly` (host IS scanned), and `E2ETestDataSeeder` is the living precedent (already seeds Workflow + Forms from host). User agreed: host wins.

## Technical Details

**Entities wired:**
- `BusinessProcessDefinition` root (PublicId + Code + TenantId, unique index on code).
- `BusinessProcessStateForm` binding Draft state → intake form.
- `BusinessProcessTransitionForm` binding Submit transition → intake form.

**Seeder facts:**
- Host-resident in `src/host/AqtCoreFW.Api` (mirroring E2ETestDataSeeder pattern).
- Reads workflow def + form template by natural key via direct entity access (host has full DbContext).
- Binds Draft and Submit by NAME, not opaque ID enum.
- 2-phase save (root → form maps).

**Tests:** 5/5 real-SQL:
- T1: Root + bindings created.
- T2: Form maps target correct Draft and Submit transitions.
- T3: Idempotent on code key.
- T4: Missing workflow throws + zero BP row persisted (non-vacuous).
- T5: Missing form throws + zero BP row persisted (non-vacuous).

## What We Tried

1. **Approach A (module-based):** Extend client contracts with resolve-by-key methods. REJECTED: widens cross-module seam for dev-only caller; forces all-states binding without names.
2. **Approach C (host-based):** Mirror E2ETestDataSeeder. ACCEPTED: zero contract change, full entity access, clean binding by NAMED states/transitions.

## Root Cause Analysis

Modular seeders work when their dependencies have microservice-extraction-friendly contracts (PublicId + opaque enums). They fail when the seeder needs semantic structure (named states, transitions, form field options) that the microservice boundary deliberately hides. The host-resident seeder isn't "wrong," it's the right tool for inherently cross-module concerns — root composition, not module internals.

The architecture tests exist to prevent illegal reaches. They forced the right answer instead of letting bad design compound.

## Lessons Learned

- **Check `ModuleIsolationTests` first**, not after sketching code. The scout's natural instinct (cross-module direct-ref) was illegal; the test caught it pre-code.
- **Fail-loud + non-vacuous tests** capture partial-failure windows. Code-review found T4/T5 needed both throws AND zero-row assertions (a plain restart could silently skip re-seeding if the root existed but bindings didn't). Wrapped both phases in `BeginTransactionAsync`/`CommitAsync`.
- **Advisor catches false premises.** I premised SeederMarker wouldn't accept host seeders — wrong by 10s of lines of reading the contract. That wrong premise would have pushed toward Approach A (worse).

## Next Steps

- `/cook P7` Niche Defs (Rules/Reporting/Integration, needs only P2) = final reseed phase.
- Filed `SEED-TXN-WRAP-P4-P5` follow-up (propagate transaction wrap to P4/P5 seeders for idempotency safety).
- Known gap `BP-FORM-OPTIONS-SNAPSHOT`: intake form EnumRef options don't snapshot into BP, render empty at BP-run until fixed (not P6 regression, carries from P5).
- Extended `WF-DEMO-SEED-SMOKE` to cover P6 (reset-all → 3 markers converge + activate BP + start run).

**File:** `/AqtCoreFW/src/host/AqtCoreFW.Api/DemoBusinessProcessSeeder.cs` (host-resident, 120 lines)
**Edits:** `Program.cs` (gated AddHostedService), `appsettings.Development.json`, `reset-all.ps1`.
**Tests:** `DemoBusinessProcessSeederTests.cs` (5/5 real-SQL, both tenants).
