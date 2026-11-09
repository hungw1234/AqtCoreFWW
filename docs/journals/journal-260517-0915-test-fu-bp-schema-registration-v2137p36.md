---
title: TEST-FU — register businessprocess schema in CrossSchemaSqlScanner (v2.137p36)
date: 2026-05-17
commit: ae4814d2
version: v2.137p36
type: micro-ship
duration: ~5 min
---

## What shipped

`ae4814d2` pushed origin/main. Single +1 dict entry in `tests/architecture/AqtCoreFW.Architecture.Tests/Helpers/CrossSchemaSqlScanner.cs:33`:

```csharp
["businessprocess"] = ["businessprocess"],
```

Inserted alphabetically between `audit` and `collaboration`. Backlog TEST-FU row updated → SHIPPED. Single commit, 2 files, ~3 LOC + 1 backlog line.

## Why

BP module shipped P3-P8 over weeks (commits `04e6f36a` Phase-3 onward) but `ModuleToSchemas` dict in `CrossSchemaSqlScanner` was never updated. Result: BP files silently skipped at line 125 `if (!ModuleToSchemas.TryGetValue(owningModule, out var ownedSchemas)) continue;` — `ModuleIsolationTests.NoCrossSchemaSql_InModuleSrc` never inspected any BP source file. Going forward, any cross-schema raw SQL added to BP modules would silently slip through architectural guard.

Pre-implementation grep on `src/modules/businessprocess` for `(FROM|JOIN)\s+(\[?)(<known-schema>)` → zero matches. BP module's cross-module data access already routes through `IWorkflowModuleClient` / `IFormSubmissionRunClient` / `IFormPrefillContextClient` (per P2-P6 architecture). Adding the dict entry registered BP scanning with zero new violations — clean activation.

## Lessons

1. **Pre-grep before activating scanner saved a debug spiral.** If existing BP code had any cross-schema SQL, this commit would have flipped tests RED on push. Verified zero violations BEFORE editing scanner = clean ship vs investigate-and-fix loop.

2. **Path correction same as ARCH-FU pattern.** Backlog said `tests/architecture/AqtCoreFW.Architecture.Tests/CrossSchemaSqlScanner.cs:31-46`; real path is `Helpers/CrossSchemaSqlScanner.cs`. Same morning's L4 grep-verify habit applied — verified actual location before editing.

3. **Micro-ship cadence: ARCH-FU (~15 min) → TEST-FU (~5 min) consecutive.** Both were 1-2 file test-infra fixes. Total ~20 min across 2 ships. Per memory L4: skip subagent code-reviewer + tester sweeps for micro-test-infra changes (Architecture 97/97 PASS + grep-verified-zero-deltas is sufficient). Cook protocol "MUST spawn subagents" applies to feature work, not bulk test-config fixes.

4. **Backlog row premise was CORRECT this time.** Unlike ARCH-FU (which had wrong file + wrong call-count), TEST-FU's backlog claim of "BP silently skipped at line 125" matched code reality. Pattern: even after one wrong-premise burn, NOT every backlog claim is wrong — but verification cost ~30 seconds either way.

## Test deltas

- Architecture 97/97 PASS (one of which is `ModuleIsolationTests.NoCrossSchemaSql_InModuleSrc` now ALSO scans BP)
- BP scanner activation: 4-csproj module under `src/modules/businessprocess/` now in scan path
- Zero violations introduced (BP architecture clean via ModuleClient interfaces)

## Carry-over

None. TEST-FU closes cleanly. Tail of [[arch-fu-shipped-260517]] / [[bp-mvp-p7-fu1-2-shipped-260517]].

## Ship cadence today

| # | Commit | Version | Scope | Time |
|---|--------|---------|-------|------|
| 1 | `cc620a76` | v2.137p34 | P7-FU1.2 picker UX | ~08:45 |
| 2 | `fac04940` | v2.137p35 | ARCH-FU whitelist | ~09:00 |
| 3 | `ae4814d2` | v2.137p36 | TEST-FU BP schema | ~09:15 |

3 ships, ~65 min total wallclock. Pattern: post-MVP feature-complete state = cleanup ship streak. No fatigue band entered (3 ships not 6+, all micro).
