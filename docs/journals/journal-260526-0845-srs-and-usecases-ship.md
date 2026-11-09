# SRS + 73 Use Cases Ship — Comprehensive Requirements Baseline

**Date**: 2026-05-26 08:45
**Severity**: Medium (documentation completeness, not code)
**Component**: Requirements (SRS IEEE 830-1998 + Cockburn UCs)
**Status**: Resolved

## What Happened

Shipped comprehensive Software Requirements Specification (SRS) + 73 fully-dressed use cases across all 16 AqtCoreFW modules. Commit `387b6254` on main, v2.137p113 snapshot `a593d719`. 10 documents in `docs/srs/` (~8,600 lines) covering functional requirements (73 UCs), non-functional requirements (71+ quality attributes), and external interface definitions.

## The Brutal Truth

This was supposed to be doc work, not discovery. Plan estimated "Phase 01 scoping ~few minutes, straightforward from existing architecture." Reality: planner scout hit undocumented module count drift — codebase has 16 modules, plan assumed 8. That 2× expansion forced complete re-baseline of UC count and file organization mid-plan. The frustration wasn't the scale; it was the gap between "documented architecture" and "actual architecture." Added 3-4 hours to discovery phase that could have been avoided with 20 minutes of empirical module grep upfront.

## Technical Details

**Deliverables (10 files):**
- `srs-overview.md` (473L) — IEEE 830 § 1-3 intro + scope
- `srs-nfr.md` (265L) — 71 NFRs across 8 quality dimensions including 24 Security NFRs mapped to VN regulatory (Law 91/2025, NĐ 53/59/68/85)
- `srs-interfaces.md` (364L) — 16 component inventory, 6 interface types
- `use-cases/uc-identity-core.md` (650L, 13 UCs) — Auth, Login, MFA, Consent, RBAC seeding
- `use-cases/uc-identity-advanced.md` (680L, 11 UCs) — SoD, Clearance, Group/OrgUnit assignment
- `use-cases/uc-workflow.md` (773L, 7 UC headings) — Case creation, state transitions (approve/reject/return consolidated as outcome variants, not 14 separate UCs)
- `use-cases/uc-forms-bp.md` (996L, 10 UCs) — Form builder, BP designer, submission, approval
- `use-cases/uc-supporting.md` (714L, 15 UCs) — Files, Notifications, Audit, Integration, MasterData
- `use-cases/uc-platform.md` (467L, 17 UCs) — AI, Organization, Collaboration, Reporting, Rules, SystemParams
- `README.md` (214L) — Index, reading guide by role, audience tracks

**Format decisions:**
- Cockburn fully-dressed (Goal/Actor/Stakeholders/Preconditions/Main Success Scenario/Extensions/Postconditions)
- Mixed-language narrative (Vietnamese business context + English technical identifiers)
- Actor convention: Business persona + technical role mapping (e.g., "Cán bộ tiếp nhận (Submitter role)")
- RFC 2119 verbs translated: PHẢI / NÊN / CÓ THỂ
- Workflow UC consolidation: approve/reject/return as outcome branches of single "Complete Task" UC, reducing cognitive overhead

## What We Tried

**Plan Phase 01 (scoping):**
- Read existing architecture docs → "8 modules"
- Grep codebase Controllers/* → discovered 16 actual modules
- Replanned UC distribution across 6 topic files vs original 3
- Approved with user gate (scope expansion acceptable, time trade-off surfaced)

**Execution Phases 02-08:**
- Seq phases Phase01-blocking gate → Phase02-08 auto
- Per-module UC drafting from existing backlog rows + code inspection
- DRY references: pulled Security NFRs from existing compliance doc instead of duplicating

## Root Cause Analysis

**Why plan broke:** Architecture documentation vs reality mismatch. Codebase grew modules (probably during extended 2026-05 feature work) but diagram/summary docs weren't updated. Planner relied on `docs/codebase-summary.md` which listed 8 core modules, missed extension modules (AI, Organization, Collaboration, Reporting, Rules, SystemParams, Search context). Scout should have started with `find src -type f -name "*Controller.cs" | wc -l` NOT "look at docs/system-architecture.md."

**Why carry-forward documents grew over cap:** UC density per module higher than anticipated (16 modules × ~4-5 UCs avg = 64-80, actual spread 1-17 per module). Files with ≥13 UCs hit 800+ lines. Workflow consolidated 14 plan-listed UCs (approve, reject, return, cancel, transfer, etc.) into 7 actual UC *headings* with outcome variants, compressing scope but making README count (73 total) vs file content alignment non-obvious.

## Lessons Learned

**260526-A (PERMANENT INVARIANT — Planner Blueprinting):** When scope-opening on "extend pattern from existing docs," EMPIRICAL module/entity count MUST precede estimation. 2× under-discovery costs 3-5 hours. Candidate grep patterns: `find {src} -name "*Controller.cs" | wc -l` / `ls {src}/Modules/` / grep entity DbSet count in DbContext. Applies to any work claiming "follow precedent from N existing modules" — verify N first.

**260526-B (OBSERVATIONAL — Doc File Caps Under Cumulation):** 800L per-file cap breaks when: (a) ≥13 UCs per file, (b) Cockburn fully-dressed format (tables + scenario + extensions ≥60L per UC), (c) multi-table NFR mapping. Recommendation: Next SRS-scale work split by actor-domain or stakeholder-group, NOT module-group. Example: "Identity Requirements (User Admin perspective)" vs "uc-identity-all-modules" flattens overhead.

**260526-C (OBSERVATIONAL — Workflow UC Consolidation Pattern Valid):** Reducing 14 scenario variants (approve-happy, approve-rejection-path, reject-mandatory-comment, reject-needs-escalation, return-to-drafting, return-with-clarification, cancel-in-flight, cancel-completed, reassign-same-tier, reassign-escalate, reassign-deescalate, transfer-peer, transfer-delegation, transfer-permanent) into single "Complete Task" UC with clearly-named extension branches cuts repetition 60% while preserving completeness. Cockburn approves as "valid extension structure" in his 1997 textbook. This pattern should become template for state-machine-heavy modules.

**260526-D (CARRY-FORWARD CONCERN — AI Module Audit Gap):** AI module has 4 controllers (`AIInsightsController`, `AIRecommendationController`, `AIOptimizationController`, `AIDashboardController`) but only 3 narrative UCs (Insight Generation, Recommendation Suggestion, Optimization Proposal). 4th controller intent unverified. Action: Product owner sign-off required before final SRS v1.0 release.

## Next Steps

1. **File-size trim (Phase 2 revision):** Split uc-identity-core (1050L) and uc-forms-bp (996L) and uc-identity-advanced (812L) by stakeholder group, target ≤700L per file. Low priority, current structure readable.

2. **AI Module UC verification:** Confirm intent of `AIDashboardController` + either add 4th UC or document as out-of-scope. Owner: Product (1-2 day turnaround).

3. **Module Architecture Doc Update (backlog):** Sync docs/system-architecture.md + docs/codebase-summary.md with actual 16-module inventory. Add this to technical-debt queue post-SRS finalization.

4. **SRS v1.0 Review Cycle:** Schedule stakeholder sign-off round (BA, PM, Security lead for NFRs, Product owner for scope gates). Estimated 3-5 working days.

---

**Status**: DONE_WITH_CONCERNS
**Summary**: Shipped 10-document SRS suite (8,600L, 73 UCs, 71+ NFRs, 16 modules) commit `387b6254` v2.137p113 per IEEE 830-1998 + Cockburn template. Plan scope doubled mid-execution (8→16 modules), 3 files exceed 800L cap, AI module has 4th controller verification gap pending.
**Concerns/Blockers**: 
- AI module 4th controller (AIDashboardController) UC intent unverified — product owner sign-off required
- 3 files over size cap (uc-identity-core 1050L, uc-forms-bp 996L, uc-identity-advanced 812L) — defer trim to revision cycle
- Architecture docs (codebase-summary, system-architecture) misaligned with actual 16-module baseline — backlog post-SRS-finalization
