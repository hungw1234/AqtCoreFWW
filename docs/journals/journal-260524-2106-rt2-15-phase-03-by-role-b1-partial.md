---
date: 2026-05-24
time: 19:00-21:15 ICT (~2h15min actual)
slug: rt2-15-phase-03-by-role-b1-partial
type: implementation-session
session_ships: 1 (commit pending, v2.137p109)
related_commits: []
related_memory: rt2-15-scaffold-and-q-fold-260523 (Phase 03 base plan, gitignored)
---

# RT2-15 Phase 03 (B1 Partial) — ByRole Predicate-Pool Producer + Intentional Read-Side Gap

## Session goal

Execute RT2-15 Phase 03 ByRole clause mode producer (predicate-pool variant). Original plan documented **Path 2** (materialize users via `IIdentityModuleClient.ResolveUsersInRolesAsync`). Estimate: 5-8h including client cascade + read-side integration. **Reality: ~2.5h actual after architectural pivot to Path 1.** User explicitly chose B1 partial scope (write-side producer + canonicalize) with read-side gap deliberately accepted despite advisor warning (Option A: defer + replan + product re-ratify; Option B1: ship predicate-pool tonight with T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION P0 critical blocker).

## The brutal truth

This session exposes a **planning-phase architectural blind spot** + **user override of advisor guidance** + **intentional breach of 260523-D feeder-with-reader invariant** that must be documented plainly:

1. **Plan doc vs reality gap:** Phase 03 Architecture diagram (Path 2 materialize) was internally inconsistent with the existing `CandidatePrincipalType` enum (mode 4 = Role direct). Slice-start scout caught the contradiction empirically (grep + type inspection). Plan was answered against the wrong architecture.

2. **Advisor disagreement escalation:** I recommended Option A (defer phase 03, amend plan docs, surface new PQ-1 architecture question to product, 4-8h Tue AM replan cycle). User chose Option B1 (ship predicate-pool producer tonight, file P0 CRITICAL backlog row for read-side, accept unclaimable-tasks risk until Phase 04 lands). User ratified this choice 3 separate times: slice-start gate, code-review gate, release decision.

3. **260523-D invariant intentionally violated:** That invariant says "feeder write-path MUST ship same slice as reader setup OR feature-flagged." Phase 03 B1 ships with reader gap explicitly known. Mitigation: **T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION (P0 CRITICAL)** filed in backlog + production alert documented in changelog. This is NOT silent acceptance; it's escalated risk acceptance with artifact trails.

4. **5th ship of day + fatigue floor warning:** Before starting Phase 03, fatigue-check fired 3 times (memory 260522-M precedent). User acknowledged, proceeded anyway. Session stayed clean (9.3/10 code-review, all tests GREEN, no rework), but this is the EXACT scenario where fatigue + advisor-disagreement + invariant-breach converge. Whether this ships cleanly Tue when P0 lands is the empirical test of whether "user override works safely" or "we needed the 8h deferral."

## What happened

### 1. Slice-start audit + architectural pivot discovery (10 min)

Grepped plan-doc Phase 03 Architecture section against working code:
- **Plan claimed Path 2:** "Materialize user IDs via `IIdentityModuleClient.ResolveUsersInRolesAsync(tenantId, roleName)` → flatten to direct users → pool via Assignee union logic."
- **Empirical reality:** `CandidatePrincipalType` enum (CandidatePrincipalType.cs:8-14) defines `Role = 1, Group = 2, OrgUnit = 3` with design comment "retained for deferred resolver modes (RT2-15)."
- **Existing code:** `WorkflowHandlerResolver.ResolveAsync()` already has union logic for `CandidatePredicate(type, ref)` pools (mode 1 ByPermission, mode 2 PreviousHandler both use this). No materialization to users anywhere.

**Architectural insight:** Path 2 materialize contradicts the enum design intent. The architecture was never "flatten to users" — it was always "predicate pools for claim/inbox filtering." Phase 03's design doc was written against the wrong frame.

**Choice point:** Proceed with Path 1 (direct predicate-pool via `CandidatePrincipalType.Role = 1`, mirror ByPermission structure) or pause for replan. Flagged with advisor + user.

### 2. Advisor Option A vs User Option B1 (decision gate, 15 min)

**Advisor recommendation (Option A):**
- Scope: Admit architectural contradiction in plan
- Action: Defer Phase 03 execution; amend plan-doc Phase 03 Architecture section
- Replan: Clarify with product: "Is PQ-1 `direct-only` (no materialization) or `all-inherited` (materialization)?"
- Timeline: +4-8h Tue AM (Mon evening too late for product decision; this is scope-changing)
- Benefit: No invariant breach; clean feeder-with-reader coupling in Phase 04 ship

**User ratification (Option B1):**
- Scope: Accept Path 1 direct predicate-pool (aligns with existing enum design)
- Ship: ByRole clause mode producer + canonicalize validators + FE editor tonight (~2.5h)
- Gap: Read-side claim/inbox still hardcode `PrincipalType == Permission` → ByRole-pooled tasks unclaimable until Phase 04
- Risk: Production ByRole assignments are write-only dead code until T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION (P0) closes
- Mitigation: NEW backlog row (P0 CRITICAL) + production alert in changelog + BLOCKED-ON-PREREQ markers in plan
- **Ratifications:** (1) slice-start gate approved B1; (2) code-review gate approved M2 fold + P0 setup; (3) release decision approved ship

User explicitly said: "Path 1 aligns with the enum design intent I see in the code. File the P0 blocker. Ship it." Three times.

### 3. Path 1 execution: ByRole predicate-pool producer (120 min)

**Phase 03a: Resolver case wiring** (~15 min)
- `WorkflowHandlerResolver.ResolveAsync()` adds new case: `AssignmentClauseMode.ByRole`
- Logic mirrors ByPermission exactly:
  ```
  foreach (var roleName in ParseRefs(clause.RefsJson))
  {
      var roleKey = (CandidatePrincipalType.Role, roleName);
      if (!predicates.ContainsKey(roleKey))
          predicates[roleKey] = new CandidatePredicate(
              CandidatePrincipalType.Role, roleName, combine);
  }
  ```
- No user enumeration. No materialization. No new repo calls. Structural copy of ByPermission case (13 lines).
- Lifted ByRole out of `_notSupportedModes` list (remaining: ByGroup, ByOrgUnit per phases 02, 04).

**Phase 03b: Canonicalization helpers** (~20 min)
- New pure functions `CanonicalizeRoleNames()` (string? → string?) + overloads for entity/spec
- Semantics: Trim each role name + ToUpperInvariant (mirror PermCodes pattern)
- Fail-loud: Malformed JSON → R3 violation (same fail-closed as ByPermission)
- Wired in validator at 2 sites (entity clauses + spec clauses per Phase 01 pattern)
- Symmetry check M2 fold: validated that both ByPermission AND ByRole emit R3 on malformed JSON (code-review M2)

**Phase 03c: Validator R4 lift** (~10 min)
- Removed `AssignmentClauseMode.ByRole` from `_notSupportedModes` hashset (line 29)
- Rationale: ByRole now productionizable (resolver case implemented, canonicalize wired)
- ByGroup + ByOrgUnit remain deferred (phases 02, 04) — kept fail-loud

**Phase 03d: FE editor wiring** (~25 min)
- New component `workflow-role-multi-select.tsx` (reuses `useRoles()` hook + AntD MultiSelect)
- Mode 4 "Vai trò" option added to `transition-assignment-editor.tsx` AssignmentClauseMode enum
- FE-A4b Vitest test: assertions cover mode 4 in editor option list + renders without error
- Test PASS (7/7 FE vitest suite)

**Phase 03e: Unit tests** (~30 min)
- Removed `AssignmentClauseMode.ByRole` from fail-loud deferred-mode Theory (5 test methods now removed)
- Added 5 new positive ByRole facts:
  1. Single role resolves to one predicate
  2. Multiple roles union across predicates
  3. Duplicate roles across transitions deduped by union
  4. Empty refs array contributes nothing (IsEmpty = true)
  5. **Anti-tautology lock:** ByRole + ByPermission both in same pool (proves they're not mutually exclusive code paths, validates union semantics)
- Added 3 validator facts:
  1. CanonicalizeRoleNames Trim + Upper works
  2. Malformed JSON emits R3 violation (fail-loud)
  3. Integration between resolver case + canonicalize (end-to-end)
- Tests now drive the spec: **what ByRole means** (pooled predicates) vs **what happens on error** (R3 fail-loud) vs **what doesn't work yet** (read-side claim integration).
- All 8 new tests GREEN; WF.App 554 → **562** (+8 net from Phase 01 baseline).

### 4. Read-side gap documentation + P0 critical backlog row (20 min)

**Honest section in WorkflowHandlerResolver:**
```csharp
case AssignmentClauseMode.ByRole:
    // RT2-15 Phase 03 (B1 partial 2026-05-24): direct-only role semantic (PQ-1 A).
    // ...
    // READ-SIDE GAP: WorkflowTaskRepository claim/inbox queries still filter
    // PrincipalType=Permission only. Until T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION
    // (P0 CRITICAL) ships, Role-pooled tasks are unclaimable + inbox-invisible.
    // Do NOT seed ByRole assignments in production-touching test seeders.
```

**NEW backlog row filed:**
```
T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION (P0 CRITICAL, ~1-2d)
Status: ⚠ CRITICAL — UNCLAIMABLE TASKS RISK
Discovered: 2026-05-24 RT2-15 Phase 03 (B1 partial) ship

Summary: Extend WorkflowTaskRepository claim/inbox queries to support
PrincipalType IN (Role, Group, OrgUnit). Phase 03 writes predicates
with PrincipalType=Role but read-side still hardcodes PrincipalType=Permission
→ tasks invisible in inbox + unclaimable.

Mitigations until ship:
- E2E seeders kept clean (no ByRole assignments)
- Consider runtime WARN log if workflow activates ByRole/ByGroup/ByOrgUnit
- Production alert: ByRole not yet production-ready

Unblocks: Phases 02 (ByGroup) + 04 (ByOrgUnit) — same read-side gap
```

**Carry-fwd backlog row:**
```
RT2-15-PHASE-03-FU (P4, ~1h): Integration test for ByRole predicate-pool

Status: BLOCKED on T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION (P0 CRITICAL)

Scope: After read-side lands, add integration test covering:
- Seed users + roles directly
- Create workflow with ByRole(['ROLE_REVIEWER'])
- Assert CandidateIndex row written with PrincipalType=Role
- Assert direct-member users see task in inbox + can claim
- Assert group-inherited user does NOT see task
  (anti-tautology lock for PQ-1 A direct-only semantic)

Unit tests sufficient for shipped resolver. Integration assertion
locks the contract once read-side shipping allows.
```

### 5. Code-review gate + decision ratification #2 (15 min)

Code-reviewer approved with nits:

**APPROVE-WITH-NITS 9.3/10:**
- 0 Critical
- 0 High
- 2 Medium (M1, M2 below)
- 4 Low (L1-L4)
- 1 Observation

**M1 (folded same-slice):** `ParseRefs()` call doesn't explicitly ToUpperInvariant in resolver before passing to CandidatePredicate. Defense-in-depth: caller should not rely on canonicalize validator having run first (what if validator was skipped in test mocks). **Fix:** Add `roleName.ToUpperInvariant()` inline in resolver case, ~2 lines. **Rationale:** validator runs Save-time; resolver runs runtime. Defensive double-normalize beats "trust validator ran."

**M2 (folded same-slice):** Canonicalize asymmetry — ByPermission emits R3 on malformed JSON AND returns null (safe). ByRole emits R3 but returns nullable. Validate behavior matches: both should handle malformed gracefully. **Fix:** verified both emit R3 + return null when JSON corrupt; M2 is purely observational (code is correct, just confirmed). **Rationale:** symmetric fail-loud pattern with ByPermission (anti-tautology check).

**L1-L4 carry-fwd:**
- L1: Schema constraint comment (ByRole refs are just strings, not validated against static set — tenants define their role names)
- L2: FE no-error assertions (mode 4 renders; actual role name resolution at runtime)
- L3: seeder discipline (do not seed ByRole in production-adjacent fixtures)
- L4: P0 backlog visibility (production alert)

**Decision ratification #2:** Code-reviewer confirmed P0 CRITICAL + mitigations sufficient for "ship B1 partial" gate. No blocker.

### 6. Release decision + decision ratification #3 (5 min)

Final release gate: Ship Phase 03 B1 partial commit despite known read-side gap?

**User decision:** Yes. Rationale: Path 1 aligns with existing architecture (enum design intent). P0 blocker is crisp + specific (claim/inbox queries, ~1-2d work). Phase 02 + 04 are BLOCKED-ON-PREREQ until P0 closes — can't proceed with ByGroup/ByOrgUnit anyway (same read-side gap). Silent deferral (Option A) delays other phases with no concrete artifact. Explicit gap (Option B1) unblocks parallel work (e.g., prod ops team can prep tenant comms that ByRole disabled until week-N).

**This is the 3rd explicit ratification of Option B1 choice.** Not blind, not fatigue-driven — user saw all three advisor objections (planning inconsistency, invariant breach, fatigue context) and chose anyway.

### 7. Cycle metrics (ACTUAL vs PLAN)

| Aspect | Plan estimate (Phase 03 docs) | Actual 2026-05-24 |
|--------|------------------------------|-------------------|
| Architectural discovery + pivot decision | 0 (assumed Path 2 only) | 15 min |
| Resolver case + canonicalize | 2-3h | 35 min |
| Validator R4 lift | 0.5h | 10 min |
| FE editor wiring | 1-2h | 25 min |
| Unit tests (ByRole + validator cases) | 0.5-1h | 30 min |
| Integration tests | 1-2h | **deferred to RT2-15-PHASE-03-FU (P4)** |
| Code-review + closure | 0.5-1h | 15 min |
| **Total wall-clock** | **5-8h** | **2h15min actual** |

**Why 2.3-3.5× faster than plan:**
1. Path 1 simpler than Path 2 (no cross-module client, no user enumeration, no materialization)
2. Direct predicate-pool mirrors existing ByPermission pattern (mechanical copy, low discovery cost)
3. Canonicalize pure text transform (no stateful validation like PermCodes.All lookup)
4. FE editor reuses existing `useRoles()` hook (no new component design)
5. Tests mechanical (add + remove cases in existing Theory suite)

**Honest take:** Original 5-8h estimate assumed Path 2 discovery cost ("what does IIdentityModuleClient need, how do we handle user enumeration, what's the fallback if a role has 1000+ users"). Plan doc was internally incoherent; architect discovered the inconsistency by comparing plan to code (not by implementing). Once architectural pivot clarified, execution was straightforward pattern-application. **This is exactly the scenario where planner over-allocates 5-9× because the real design question wasn't answered upfront.**

## Subagents spawned

- **code-reviewer (async):** Phase 03 full review + M1/M2 fold — APPROVED-WITH-NITS 9.3/10
- **docs-manager (async):** changelog + roadmap update
- **git-manager (async):** Single commit TBD (v2.137p109 pending at 21:15 ICT)

(No fullstack-developer needed; no mock migration required. Path 1 uses existing resolver patterns.)

## Test status (FINAL)

- **WF.Application:** 562/562 ✅ (+8 net from Phase 01 baseline 554)
- **WF.Infrastructure:** 84/84 ✅ (no schema changes, no new migrations)
- **Workflow.Integration:** existing 50+ tests still PASS (no breaking changes)
- **FE (Vitest):** 7/7 ✅ (mode 4 editor test added + passing)
- **Architecture.Tests:** 105/105 ✅ (no module-boundary violations)
- **Build:** 0E/0W (no warnings)
- **EF has-pending-changes:** clean (no orphan migrations)

## What hurt (root causes)

**Planning-phase architectural blind spot (260524-E candidate INVARIANT):**
- Plan-doc Phase 03 Architecture diagram stated Path 2 materialize as THE design
- Existing enum + resolver code implied Path 1 predicate-pool design
- Root cause: Orchestrator (me) did NOT cross-check plan-doc Architecture section against existing enum/type definitions BEFORE submitting to product for ratification
- Lesson: Architecture diagrams in plan docs are hypotheses, not ground truth. **INVARIANT 260524-E (candidate for promotion on 2nd reinforcement):** Pre-ratification verification MUST include orchestrator-verifies-architecture-matches-existing-types (grep enum definitions, check resolver patterns, look for design-intent comments). Plan docs can be outdated or aspirational; code is authoritative.

**Product ratification against wrong architecture (260524-F candidate INVARIANT):**
- PQ-1 (ByRole: direct-only vs all-inherited) was answered against Path 2 materialize model
- Once architect switched to Path 1, PQ-1 answer may not apply (direct-only makes sense for materialization; predicate-pool is always "direct until read-side filtering applies")
- Root cause: I ratified PQ-1 without re-surfacing to product that the architecture had changed
- Lesson: **INVARIANT 260524-F (candidate):** When architecture pivots during planning, surface new questions to product before coding. PQ-1 re-ratification against Path 1 would have been cleaner than shipping B1 and hoping the semantic aligns.

**Intentional invariant breach (260524-G):**
- 260523-D says "feeder write-path MUST ship same slice as reader OR feature-flagged"
- Phase 03 B1 violates this: ships writer, defers reader by design
- Root cause: User accepted the breach explicitly (ratified 3×), with written artifact trail
- Lesson: **INVARIANT 260524-G (candidate, may become permanent if pattern repeats):** Invariant breach is allowed if (1) user explicitly ratifies + (2) mitigations documented + (3) backlog row filed CRITICAL or higher + (4) production alert in docs. This is NOT free pass; it's escalation of risk acceptance. Track if this pattern invokes again — rarity is the requirement.

**Fatigue floor warning + advisor override (260524-H):**
- Memory 260522-M fatigue floor = "3+ sequential fatigue overrides + late evening + advisor disagreement = escalated risk"
- This session hits all three: 5th ship of day + 21:00 ICT (local evening) + advisor explicitly recommended Option A deferral, user chose Option B1 anyway
- Root cause: User judgment call; not a mistake, but the exact scenario the invariant warns about
- Lesson: **INVARIANT 260524-H (observational):** When fatigue + advisor disagreement + invariant breach converge, document it PLAINLY so that if the gamble fails (e.g., P0 blocking reads + feature breaks Tue AM), there's a clear "we knew the risks and decided anyway" paper trail. This entry is that trail. If Phase 04 lands cleanly and P0 closes by Tue PM, the gamble paid off. If not, memory.md should note "260524-H scenario recurrence" for future fatigue assessments.

## Decisions made

1. **Architecture: Path 1 direct predicate-pool over Path 2 materialize.** Rationale: aligns with existing `CandidatePrincipalType` enum design intent, mirrors ByPermission pattern, avoids user-enumeration complexity. User ratified on evidence (code inspection, enum design comment).

2. **Scope: B1 partial (writer + canonicalize + FE) over full Phase 03.** Rationale: read-side gap is crisp + specific (claim/inbox queries); P0 blocker isolates risk; phases 02/04 blocked anyway (same gap). User ratified 3×.

3. **Read-side gap acceptance with P0 critical backlog row.** Rationale: Not silent deferral; explicit artifact trail (backlog + changelog + code comments). Production alert documented.

4. **Fold M1 + M2 same-slice rather than carry-fwd.** Rationale: M1 is <2 lines (defense-in-depth); M2 is observational validation. Carrying forward would be lazy.

## Unresolved questions

- **Phase 02 Identity client scope:** Does Product expect ByGroup resolver to materialize user IDs OR use predicate-pool like Path 1? (Phase 02 slice-start audit next session will clarify — may trigger another PQ-re-ratification if product had materialize intent.)
- **P0 effort timing:** T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION estimates 1-2d; if it slips to 3-4d, Phases 02 + 04 both stall. Should this be escalated to priority P-CRITICAL (vs P0) to secure resource commitment? (Defer to project-manager at sprint start.)
- **Production comms:** Tenant ops team needs alert that ByRole is write-only until P0 ships. Who owns that comms (product owner or eng)? (Out of scope for this journal; should be tracked in backlog row impact field.)

## Lessons → memory anchors

| Anchor | Pattern | Confidence |
|--------|---------|-----------|
| **260524-E (NEW candidate)** | Plan-doc Architecture section can be aspirational/outdated vs actual code intent — verify BEFORE product ratification via enum grep + design-intent comments. Promotion on 2nd reinforcement. | PROMOTE on 2nd |
| **260524-F (NEW candidate)** | Product ratification (PQ-1/Q1-Q5) can be answered against wrong architecture → invalidates downstream design. Pre-ratification: orchestrator MUST verify architecture matches existing codebase before surfacing Q. | PROMOTE on 2nd |
| **260524-G (NEW, may become permanent)** | Invariant breach (260523-D feeder-with-reader) allowed if: (1) user explicit ratify + (2) mitigations documented + (3) CRITICAL backlog row + (4) production alert. NOT free pass. | Observational; track recurrence |
| **260524-H (NEW, fatigue warning)** | Fatigue + advisor-disagreement + invariant breach = specific warning scenario. Document plainly when it occurs; empirical test is whether Phase 04 closes cleanly. | Observational; fatigue-context |
| **260524-D (from Phase 01)** | Memory "~N callers" decay as test harness grows; regrepped Phase 01 and found pattern holds (Q1 cascade 17 sites vs memory "~7"). | Still on tracking |

## Next session (Phase 02 readiness)

1. **Before Phase 02 planning:** Ask product to clarify ByGroup resolver architecture: predicate-pool (Path 1) or materialize (Path 2 with IIdentityModuleClient cascade)? Document PQ-2 answer against chosen architecture.
2. **Phase 02 risk:** If product expects Path 2 materialize (which breaks antipattern for ByGroup group enumeration — potential 1000+ users), signal design risk early rather than at Phase 02 code-review.
3. **P0 blocker tracking:** Weekly check on T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION progress. If slips >3d, escalate to PM.
4. **Phase 04 hand-off:** ByOrgUnit producer (Phase 04) has identical read-side dependency. Plan simultaneous P0 + Phase 04 execution once Phase 02 product clarity lands.

## Session notes

**Honest assessment of the risk:**

This session is the clearest example of "user chose the riskier option over advisor recommendation" we've had since 260522-M fatigue-override pattern. Differences:

- 260522-M was **fatigue override of mechanical scope-cut** (ship 7 rows of similar work despite exhaustion). Low complexity, high pattern-confidence. **Outcome:** Clean (shipped, all tests GREEN, no rework).
- 260524 is **architecture pivot + invariant breach + product ratification question** on top of fatigue. **Higher complexity, higher failure modes.** If P0 blocks read-side delivery, ByRole becomes a "write-only dead feature" for 3-7 days. That's operationally messy.

**What makes this defensible:**

1. User SAW the risks (I surfaced all three: architecture inconsistency, invariant breach, fatigue context).
2. User ratified explicitly 3 times, not blindly.
3. Artifacts trail: backlog row + code comments + changelog document the gap.
4. Not silent deferral: P0 CRITICAL creates accountability (if it doesn't land, someone has to explain why Phase 04 stalled).

**What could have gone better:**

1. Pre-ratification re-surface PQ-1 against Path 1 architecture (I didn't, and that's on me).
2. Ask product: "Are you comfortable with 3-5d where ByRole is write-only?" before shipping (I deferred to user without re-surfacing this trade-off).
3. Option B2 (compromise): Ship B1 partial + feature-flag ByRole in seeder so production can't use it until P0 lands (more defensive than current approach).

**Fatigue reality check:**

Session stayed clean technically (9.3/10 code-review, all tests pass, no syntax errors, no broken builds). No sign of fatigue-induced mistakes in the code itself. The risk is operational (P0 blocking reads) not technical (bad code). That said, decision-making happens differently at 21:00 after 5 ships — whether it's *worse* or just *different* is hard to assess in the moment.

**Working tree state:** Staged + ready for commit (all changes staged, zero unstaged).

---

**Status:** DONE ✅ Phase 03 B1 Partial ByRole Predicate-Pool shipped (pending git commit v2.137p109)

**Commit message (pending TBD):**
```
feat(workflow): ByRole clause mode predicate-pool producer + validator R4 lift

RT2-15 Phase 03 (B1 partial 2026-05-24): Implemented AssignmentClauseMode.ByRole
using predicate-pool pattern (mirror ByPermission). Removed ByRole from
_notSupportedModes; lifted R4 validator rule.

INTENTIONAL READ-SIDE GAP (B1 partial scope): WorkflowTaskRepository claim/inbox
queries still hardcode PrincipalType=Permission. Role-pooled tasks are unclaimable
until T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION (P0 CRITICAL) ships.

Mitigations: P0 CRITICAL backlog row filed + production alert + E2E seeders kept
clean (no ByRole assignments). Phases 02 (ByGroup) + 04 (ByOrgUnit) blocked-on-prereq.

Tests: +8 unit (ByRole predicate-pool + validator) + FE mode 4. All GREEN.
Code-review: 9.3/10 APPROVE-WITH-NITS; M1 defense-in-depth + M2 validation folded.

Related: 7dc3784b (Phase 01), 260523-D (invariant), 260524-E/F/G/H (lessons).
```
