# Journal — PHR-V2 P3 Slice 8a: RequiredPermissionCode wire-strip

**Date:** 2026-05-20
**Branch:** `feat/process-handler-resolution-p3` @ `2ac9730a` (LOCAL, 20 ahead `origin/main`)
**Version:** v2.137p79
**Slice scope:** strip RPC from production code (DTO + 2 Dapper SQL); column DB-side STAYS LIVE (S8b deferred IRREVERSIBLE)
**Phase 3 progress:** 7/11 → 8/11 → 63.6% (no change in absolute %; S8 was 1 slice in plan, now S8a + S8b)

---

## Outcome

Slice 8a SHIPPED LOCAL. Wire format no longer carries `requiredPermissionCode` key; resolver-based assignment authz (S5 contract) is the sole authz path. A7 TDD integration test locks the wire-format absence with 4 layered preconditions.

**3 commits behind: branch HEAD = `2ac9730a` (single commit, no follow-up needed unlike S6+S7).**

---

## What shipped

12 files modified, +72/-17 LOC delta:

| Category | Files | Change |
|----------|-------|--------|
| DTO | `WorkflowTransitionDto.cs` | strip record param (11 → 10 required + 2 default) |
| SQL projections | `GetAvailableTransitionsQueryHandler.cs`, `GetWorkflowDefinitionQueryHandler.cs` | drop RPC col; add SourceHandle+TargetHandle to GetAvailableTransitions to satisfy Dapper ctor-match |
| Command handlers | `SaveDefinitionGraphCommandHandler.cs`, `CreateWorkflowDefinitionCommandHandler.cs` | drop named-arg RPC from DTO ctor |
| Stale comments | `WorkflowTransition.cs`, `WorkflowTransitionConfiguration.cs`, `GetAvailableTransitionsQuery.cs`, `ExecuteTransitionCommand.cs` | reference S8a strip + S8b column-drop plan |
| Tests | `GetAvailableTransitionsQueryHandlerTests.cs`, `WorkflowApiContractTests.cs` | drop RPC arg; ctor-match |
| NEW test | `WorkflowFineResolverGateTests.cs` | A7 `GetAvailableTransitions_ResponseBody_DoesNotContain_RequiredPermissionCode` |

---

## Architectural moves (1 only — scope is tiny)

**(1) DTO contract narrowing:** `RequiredPermissionCode` is no longer carried in any wire-facing DTO or its serializations. Assignment-based authz via `WorkflowTransitionAssignment` (S5 contract) owns all access decisions. DB column stays as legacy data only; S8b drops it.

That's it. No new abstractions, no new boundary surfaces, no new factories. Pure removal.

---

## 🆕 NEW META-LESSON: Dapper + positional records + partial column coverage

**Symptom found during A7 GREEN verification:**
```
System.InvalidOperationException: A parameterless default constructor or one
matching signature (Id, FromStateId, ToStateId, ActionName, ActionLabelVi,
ActionLabelEn, ConditionsJson, SortOrder, IsRecall) is required for
WorkflowTransitionDto materialization
   at Dapper.SqlMapper.GenerateDeserializerFromMap(...) :line 3502
```

**Cause:** Dapper does NOT support optional-param ctors on positional records when the SQL column set partially covers the ctor params. Specifically, when:
- DTO ctor has N params, last K with `= null` defaults
- SQL provides M cols matching first M params (M < N - K + K_used)

Dapper requires either a parameterless ctor + init properties, OR a ctor whose parameter count EXACTLY matches the SQL column count (with all names matching).

**Why it worked PRE-S8a:** Pre-strip SQL had 10 cols matching 10 required params + DTO had 12 params (10 required + 2 default = SourceHandle, TargetHandle). The 10-col SQL fully covered the 10 required params; defaults handled the rest.

**Why it failed POST-S8a:** Post-strip SQL had 9 cols matching 9 required params (after removing RPC from both). DTO had 11 params (9 required + 2 default). Dapper rejected because no ctor with exactly 9 params exists.

**Fix:** Add `SourceHandle, TargetHandle` to `GetAvailableTransitions` SQL → 11 cols matching 11-param ctor.

**Invariant (pin in S9+ carry-fwd):**
> When removing fields from DTOs that Dapper materializes, RE-RUN AN INTEGRATION TEST OF THE IMPACTED ENDPOINT BEFORE SHIPPING. Compile-pass + unit-pass + build-pass do NOT catch Dapper ctor mismatches. Only the runtime materialization path catches the divergence. This is a class of regression the test harness CANNOT statically detect.

---

## Inherited META-LESSONS that bit (or worked) this slice

(a) **S5 META-L (a) Subagent under-states REPEATED.** Scout listed 25 src files as "RPC-referencing"; only 3 were real strip targets. Inflation: 8 historical migrations (Cat D = do-not-edit), 14 comments-only refs (Cat C clean, Cat E.c keep). Orchestrator independently grep-verified before plan locking. Time saved: ~30 min of unnecessary edit attempts.

(b) **S5 META-L (b) Worktree-diff** — not needed this slice (no claims of "pre-existing" failures; A7 RED→GREEN clean cycle).

(c) **S5 META-L (c) Tests-to-impl-lock — A7 designed defensively from start.** 4 layered preconditions per design rubric: (a) endpoint reachable, (b) body non-empty (anti-tautology), (c) body contains "Submit" (proves resolver path healthy — NOT just stripped, also alive), (d) body NOT contains "requiredpermissioncode" (SPEC-LOCK). Strip any → false-green possible.

(d) **S5 META-L (d) Memory drift** — S7 memory said `IWorkflowTransaction.ExecuteTransitionAsync` for M2 doc (caught in S7); S8a memory said "25 files to strip" (caught in S8a scout verify). Memory entries decay; always re-grep before locking.

(e) **NEW S7 META-L (e) Refactor-introduced rollback paths inherit invariants** — N/A this slice (no new rollback paths introduced; pure removal slice).

(f) **S6 D1-α advisor catch (boundary preservation)** — N/A this slice (no new boundary surfaces).

(g) **S8a-specific (Dapper):** see NEW META-LESSON above.

---

## Cost analysis

- **Plan:** ~5 min (inline micro-plan; planner subagent overkill for 10-LOC scope)
- **Scout:** ~10 min (Explore subagent, 5 surfaces)
- **TDD A7:** ~5 min (reuse existing seed helper, 4 preconditions)
- **Strip impl:** ~5 min (3 prod files + 4 stale comments)
- **Dapper debug:** ~15 min (RED→GREEN→200/empty→200/200, fix took 2 iterations)
- **Verify:** ~5 min (G1 + App suite + Domain + Contract + WorkflowFineResolverGateTests)
- **Commit + docs + journal + memory:** ~10 min

**Total: ~55 min for a 10-LOC strip.** Most time = Dapper debug (15 min, NEW META-LESSON earned). 

This is the kind of slice where you LEARN something even when the strip itself is trivial. Worth it.

---

## Decisions ratified (user gate Step 3)

| # | Choice | Why |
|---|--------|-----|
| Scope | Split S8a (strip, reversible) + S8b (column drop, IRREVERSIBLE) | Safer staging; backup table preserved for S8b recovery |
| A7 | Wire-format JSON string-contains assertion | Strongest spec-lock; catches accidental JsonPropertyName overrides; rename-resistant via case-insensitive substring |
| Push | LOCAL only | Branch already 19 ahead; default per S5/S6/S7 |

---

## Carry-forward to Slice 8b

- **M2 column-drop migration** for `WorkflowTransition.RequiredPermissionCode` — **IRREVERSIBLE**
- Q3 verified at this slice: Model-B resolver fully shipped + green (no behavioral regression from RPC absence)
- Backup table from S2 remains in DB as recovery option
- Apply Dapper META-LESSON: any DTO change at S8b that might shrink SQL column count needs integration test verification (unlikely since S8b is DB-only)
- Apply S7 META-L (e): column-drop migration may introduce new rollback paths (down() migration) — enumerate invariants

**Push state:** branch HEAD = `2ac9730a`, 20 ahead origin/main, not pushed. Default S8b = LOCAL.

---

## Unresolved

None.

---

## Test gate matrix to inherit at S8b baseline

| Gate | At S8a ship | Baseline |
|------|-------------|----------|
| Build | 0E/0W | locked |
| G1 ExecuteTransitionCommandHandlerTests | 22/22 | locked |
| Workflow.Application total | 482/482 | locked |
| Workflow.Domain.Tests | 145/145 | locked |
| AqtCoreFW.Tests.Contracts | 41/41 | locked |
| WorkflowFineResolverGateTests | 5/5 (incl A7) | locked |
| Workflow.Integration (sample) | 30p/1f/2skip (SimultaneousRace pre-existing per S7 worktree-diff) | locked |
