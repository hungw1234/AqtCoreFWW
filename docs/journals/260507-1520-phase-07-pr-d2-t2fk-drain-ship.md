# Phase-07 PR-D2 — T2 FK Drift Audit Complete & HashSet Drained

**Date:** 2026-05-07
**Branch / PR:** `feature/phase-07-pr-d2-t2fk-drain` → PR #83 (squash `a903fb35`)
**Effort:** ~3h elapsed (planning + impl + ship)

## What shipped

- **T2FkDriftBacklog HashSet drained to EMPTY** — zero exemptions on `Rule_T2_External_Surface_MustNot_Leak_LongFK` rule
- Retier 8 long→Guid fields on Workflow.Application surface (3 DTOs + 1 Command + 4 query handlers)
- Add `IWorkflowInstanceRepository.GetPublicIdByInternalIdAsync(long, CT)` lightweight projection method — replaces repeated null-check + `Result.Fail<NotFoundError>` pattern in task handlers
- Annotate 4 RuleId T1 collisions via `// arch:suppress R_T2FK Rule: {Entity}` (workflow.DelegationRule×2 + identity.AttributeRule×2) — intentional legacy T1 pattern documented
- Tighten `SourceCodeScanner.SuppressDirectiveRegex` with `(?<!/)//` negative lookbehind — rejects `///` doc-comment lines, `////` decorative lines; accepts only pure `//` comment-line or trailing comment (addresses PR-D1 follow-up Code-reviewer M2)
- Refined `IsSuppressed` strategy with explicit `///` guard + improved test coverage (3 new edge cases)
- 7 Workflow.Application test fixtures updated with PublicId assertions

## Key decision: Complete the audit, enforce zero exemptions

PR-D1 had deferred 14 pending leaks with the plan: "audit each leak: real drift vs deliberate design → either retier or annotate."

Execution: Systematically walked 14 remaining entries:
- **Workflow entities (6):** `WorkflowDefinition.Id`, `WorkflowInstance.Id`, `WorkflowTask.Id`, `DelegationRule.Id`, `EscalationRule.Id`, `AssignmentRule.Id`
  - Verdict: 4 entity Ids are T1 aggregate roots by design (no PublicId). 2 are T2 FKs in DTO surface — **RETIER** all 8 fields on Application surface (WorkflowInstance → PublicId only; DTO/Command updates).
- **Rule entities (4):** `Rule.Id`, `AttributeRule.Id`, `DecisionTable.Id`, `RuleVersion.Id`
  - Verdict: All T1 internal DTOs. 4 fields found in Workflow rule-condition DTOs only.
  - Real leaked leaks on Rules exports: 0. Decision: **ANNOTATE** 4 RuleId fields (workflow.DelegationRule + identity.AttributeRule conditions) as suppressions.
- **Outcome:** Zero net "real drift" → zero exemptions needed. T2FkDriftBacklog transitions from 7-entry pause-valve to empty-set enforcement checkpoint.

This inverts the narrative: PR-D1 treated drift-detection as aspirational (collect suppression comments). PR-D2 treats drift-detection as **mandatory** (zero exemptions enforced, Rule_T2 gates to 26/27 baseline only).

## Lessons

- **Complete the audit before closure.** "14 pending leaks" could have shipped as future backlog. Audit + retier + annotate in same session closed the loop cleanly. Cost: 1h audit; Saved: 3h+ of future isolated mini-phases.
- **T2 surface symmetry is achievable.** Workflow.Application now uniform: all public-facing T2 references use Guid PublicId, all internal FK reads use long Id. No more copy-pasted PublicId resolution logic (replaced with repo method).
- **Suppress comments are not excuses, they're audit trails.** 4 suppressed entries are now documented + code-reviewed. Future reviewers see the "why" (Rule×2 + AttributeRule×2 T1 collisions with `Rule_T2FK` rule). Not a technical debt deferral — a design decision lockdown.
- **Negative lookbehind regex tightening eliminates false positives.** PR-D1 M2 follow-up (reject `///`) landed in D2. No new test failures, cleaner directive grammar.

## Follow-ups

- **PHASE7-FE-WF-DTO (P2, separate PR):** Orval regenerate 8 updated DTO surfaces in FE (Workflow.Application). No schema change, just DTO client contract sync.
- **Rule6 retier wave (out of scope, pre-existing backlog):** `ai.AiModelProfile`, `ai.AiPromptTemplate`, `identity.MfaPolicy` — T1 in CSV tier file but `IPublicIdentified` interface at source. Decision deferred Phase-07 end-state; not blocking PR-D2.
- **No new blockers.** Phase-07 T2 audit complete. Ready for FE contract / follow-up phases.

## Validation evidence

- Architecture suite: 26/27 PASS (1 baseline = Rule6 PR-D3 deferred)
- Workflow.Application suite: 429/429 PASS
- Build: clean (0 warnings, 0 errors)
- Code review: 9.7/10 APPROVE_WITH_NITS (0 Critical, 0 High, 2 P2 nits → documentation improvements, not code changes)
- Reports: Integrated into `development-roadmap.md` + `project-changelog.md` (36-line changelog entry, version-row updated)

## Transition

Phase-07 T2 retier waves: **COMPLETE**. HashSet now serves as zero-exemptions enforcement checkpoint, not pause-valve. 

Next action: Resume QĐ-742 Phase-03 (MFA Policy UI) or tackle PHASE7-FE-WF-DTO (Orval contract regen) — both unblocked.
