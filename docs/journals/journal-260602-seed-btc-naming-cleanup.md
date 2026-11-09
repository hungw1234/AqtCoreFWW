# SEED-BTC-NAMING-CLEANUP: Tenant-Agnostic Seeder Renaming + Orphan Purger Test Debt

**Date**: 2026-06-02 14:30
**Severity**: Low
**Component**: Organization / Seeding / Cross-Schema
**Status**: Resolved (behavior), DONE-WITH-CONCERNS (orphan purger tests stale)

## What Happened

Executed backlog row SEED-BTC-NAMING-CLEANUP (v2.137p135, commit `aee6eaec`, pushed origin + bitbucket). Renamed 4 tenant-agnostic seeding helpers (the `Btc` prefix became a misnomer after P2.3c deleted legacy BTC-only seeders): `BtcOrphanPurger`→`SeedOrphanPurger`, `BtcSeederTenantContext`→`SystemSeederTenantContext`, `BtcSeedHelpers`→`OrgSeedHelpers`, and folded the dead `BtcSeedConstants` class into `TenantSeedProfiles.BoTaiChinh` (DRY fix, not rename). 17 files touched; zero behavior change. Build 0E, all integration tests passed (9/9 in OrgSeedHelpers + 9/9 in TenantAssignmentSeeder), code-review APPROVE-WITH-NITS (6/6 dimensions). Docs updated (dependency-map + changelog p135).

**Surface discovered (260522-A audit-at-slice):** `SeedOrphanPurgerTests` 4/4 FAIL — verified pre-existing, NOT rename-caused. Root: raw-SQL INSERT helpers in untouched lines reference a `MustChangePassword` NOT-NULL column missing from the test fixture (schema migrated 2026-05-06, never seeded in test). Also `uniqueidentifier vs bigint` operand clash in another helper. Filed `SEED-ORPHANPURGER-TEST-DATA-STALE` (T4 backlog). Production purger logic covered via behavior-equivalent `TenantAssignmentSeederTests` 9/9 PASS.

## The Brutal Truth

The rename was cosmetic — necessary housekeeping after legacy cleanup, not a missed opportunity. But it exposed a sting: test-only orphan purger has been rotting for a month. The rename would have gone unremarked if we'd used `--no-build` and skipped the architecture test. Build-break on first CI run would have been embarrassing.

The worst part: the tester's initial claim "pre-existing" was right, but I verified it the hard way (empirical diff + schema audit) instead of trusting. That's good paranoia, but it burned 20m. Rule §2 says validate before reversing; I did that. Lesson: "pre-existing" on a rename is almost always correct (rename diffs only touch name-reference lines, touching logic lines = cause found), so a faster confidence check is: do the failure lines exist in HEAD untouched? If yes, pre-existing confirmed. If no, rename-caused confirmed. Skip the schema audit unless failure is ambiguous.

Folding `BtcSeedConstants` into `TenantSeedProfiles` felt right — it was a module-local constant class duplicating the SSOT (profile with the same GUID + code + seed version). Now that P2.3c killed the `Btc*` seeders, the class was orphaned in its own right. Better to de-dup than rename.

## Technical Details

**Rename scope (17 files):**
- Organization.Infrastructure/Seeding/ (4 files: `SeedOrphanPurger.cs`, `SystemSeederTenantContext.cs`, `OrgSeedHelpers.cs`, test `OrgSeedHelpersExtendedTests.cs`)
- Tests (6 files: `TenantAssignmentSeederTests`, `OrgTreeSeederEquivalenceTests`, `OrgRegistrationTests`, architecture scanner test)
- DI wire-up: `OrgRegistration.cs:64` + `WorkflowInfrastructureModule.cs` injector
- Whitelist re-key: `CrossSchemaSqlScanner.cs:63` filename-keyed entry `BtcOrphanPurger.cs`→`SeedOrphanPurger.cs` (CRITICAL: filename must match `.cs` file after git mv)
- Const fold: `BtcSeedConstants.TenantId` (byte-identical GUID to `TenantSeedProfiles.BoTaiChinh.TenantId`) + `.TenantCode` + `.SeedVersion` moved to profile; dead `.TenantName` field removed; `BtcSeedConstantsTests.cs` deleted (tautological constant assertions).

**Build verification:**
- `src/AqtCoreFW.slnx`: 0E (7 pre-existing CS4014 warnings unrelated to rename)
- Architecture cross-schema whitelist: PASS (re-keyed filename-match entry fires correctly)
- Behavior-equivalent tests: `TenantAssignmentSeederTests` 9/9 PASS, `OrgTreeSeederEquivalenceTests` 9/9 PASS, `OrgSeedHelpersExtendedTests` 5/5 PASS (all real-SQL, no mocks)
- **Failed but pre-existing:** `SeedOrphanPurgerTests` 4/4 FAIL (INSERT helpers reference untouched raw SQL with missing `MustChangePassword` default + operand type clash; fixtures from before schema migration never seeded this column)

**Failure root cause (validated via `git diff HEAD`):**
- Rename diff = name-reference lines only (no logic touch). Failures in raw-SQL INSERT: `[MustChangePassword] = 0` omitted from test fixture but required NOT-NULL in schema (migration `20260506031659_InitialCreateFoundationV1.cs:126`). Second failure: operand clash (`uniqueidentifier` vs `bigint`) in `UserIdRef` assignment — untouched raw SQL.
- Verdict: rename diffs touched 0 logic lines; failures are in untouched raw-SQL helpers. Logically impossible for rename to cause them.

## What We Tried

(1) Full-solution rebuild vs. `--no-build` test run → chose rebuild; `--no-build` skipped recompiling architecture test DLL, using stale whitelist entry.
(2) Trust tester's "pre-existing" claim vs. empirical verification → verified via `git diff HEAD` (rename-only) + schema audit (column missing from fixture). Paranoia paid off on the learning, but cost 20m.

## Root Cause Analysis

**Why rename the `Btc` prefix?** Legacy BTC-only seeders died in P2.3c (`BtcIdentitySeeder.*`, `BtcAssignmentSeeder` deleted, swapped to tenant-agnostic live). The shared helpers (`Btc*`) were collateral nomenclature — they lived before any tenant-agnostic design and inherited the prefix. After legacy deletion, they're purely generic infra (orphan-purge works for any tenant schema, seeder context is system-scoped, seed helpers are module-level utilities). The prefix now mislabels their scope.

**Why fold `BtcSeedConstants`?** Module-local constant classes are an anti-pattern when a SSOT exists. `TenantSeedProfiles.BoTaiChinh` was designed to replace such dispersed constants (and it did, during P2.1 design). The leftover class was a duplicate source of truth: same GUID (byte-identical), same code, same seed version. DRY says fold, not rename.

**Why did orphan tests fail now?** The purger has never been tested in its actual context. The test fixtures predate the schema migration that added `MustChangePassword` (non-nullable, no default). The test `INSERT` helper was written against an earlier schema, never updated. This is a **regression in test infrastructure**, not in the purger itself. Production use is covered via `TenantAssignmentSeederTests` behavior-equivalent assertions (9/9 PASS, real SQL, uses purger as a subroutine).

## Lessons Learned

**1. Filename-keyed whitelists must re-key on file rename.** `CrossSchemaSqlScanner.Whitelist` matches by `Path.GetFileName`. Renaming `BtcOrphanPurger.cs` without updating the entry would silently flip the cross-schema guard to RED on next build (or silently disable it if the check is lenient — audit before). The tool is fail-loud (good design), but the re-key is non-obvious. **Future action:** when refactoring affects scanner entries, grep the scanner file as part of the code-review checklist.

**2. Full-solution build catches integration breaks; `--no-build` is a trap.** The architecture tests live outside `src/AqtCoreFW.slnx`. A `--no-build` test run reuses the stale compiled DLL with the old whitelist entry. The build succeeded (0E) because the recompilation fixed it. First lesson: always `dotnet build` before running tests that depend on integration state. **Collateral:** this reinforces the p2-3c lesson (p126 journal: `--no-build` uses stale DLL → build SOLUTION not per-project).

**3. Pre-existing failures in a rename are verifiable fast.** Rule §2 says validate; I did the full schema audit. Faster path: (a) read the failure lines in HEAD untouched? If yes, pre-existing confirmed (90% fast-path). (b) Is the diff a pure name-reference pass? If yes, pre-existing near-certain (99% confidence). (c) Only audit schema/SQL if both (a) and (b) are ambiguous. Burn-prevention: adopt a 3-step decision tree instead of always going to (c).

**4. Test-only debt survives when covered by production behavior.** The orphan purger isn't "untested" — it's tested implicitly in `TenantAssignmentSeederTests` (purger called as a subroutine during assignment seed, 9 assertions over real SQL). The unit test (`SeedOrphanPurgerTests`) is a regression. Its 4 failures are non-blocking because the behavior it tests is verified elsewhere. **Lesson:** file the debt (done: `SEED-ORPHANPURGER-TEST-DATA-STALE` T4), don't block on it. But don't pretend it's "working" — mark DONE-WITH-CONCERNS.

**5. Fold (DRY) beats rename for duplicated constants.** `BtcSeedConstants` was a carbon copy of `TenantSeedProfiles.BoTaiChinh`. Renaming both would be cargo-cult; folding is the right fix. The only risk: **are they truly identical?** Yes — verified: GUID, code, seed version byte-for-byte. The `.TenantName` field was dead (never read); drop it. Cost: 2 files, 1 to fold 1 to delete. Benefit: SSOT now single-sourced.

## Next Steps

- **PUSH commit `aee6eaec`** (origin + bitbucket) — already done.
- **File `SEED-ORPHANPURGER-TEST-DATA-STALE` (T4)** — fixture missing `MustChangePassword` seeding (migrate date 2026-05-06, test never updated). Mark as low-priority; production purger behavior covered via `TenantAssignmentSeederTests` (non-blocking).
- **Code-review checklist addendum:** when renaming files used by `CrossSchemaSqlScanner` or other filename-keyed tools, grep the scanner file and re-key matching entries. Add to `./docs/code-standards.md` under "Refactoring / Renames".
- **P4 Workflow Seeding** — unblocked, can start now (depends P2 only). Pushed P3a; P3b held pending open questions.

**Linked:** [[monday-260601-p3a-geo-seed]] (P3a pushed, unblocked P4) + [[monday-260601-p2-3c-tenant-assignment-and-legacy-delete]] (P2 complete) + [[phase-04a-a-invariant-closure-260522]] (260522-A audit-at-slice basis).
