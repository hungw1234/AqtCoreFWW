# Business Process Definition MVP — Planning Session Chaos to Clarity

**Date**: 2026-04-20 20:39  
**Severity**: Medium  
**Component**: BusinessProcess Module Design  
**Status**: Resolved  

## What Happened

Afternoon session closed at v2.125 SHIPPED. Evening: user asked simple business question — "When creating case file, how do we know which workflow applies?" — triggered 2.5-hour architecture rethink, 3 design iterations, and spawned planner agent for red-team validation. 

Scout confirmed: Case entity has ZERO foreign key to Workflow. WorkflowInstance table holds the linkage via `EntityType="Case"` + `EntityId`. Current mapping hardcoded as `"CaseProcess"` in CreateCaseCommandHandler with feature flag.

Observability gap: CaseDto doesn't expose workflow metadata. This felt like a "low-code platform" design question, not a data model gap — but we'd been treating it as domain module design, adding years of complexity.

## The Brutal Truth

I overthought this for 3 iterations. The frustration isn't failure — it's **recognizing the pattern AFTER the wasted cycles**.

**Iteration 1 crush:** Proposed 4 options (FK template, binding registry, convention-based, hybrid). Each reasonable but none felt right. User: "scout deeper. better proposal. mind perf ultrathink."

**Iteration 2 trap (perf-rabbit-hole):** Deep scout revealed Workflow module has ZERO cache. GetAvailableTransitionsQuery polled every 5s on FE case detail → 6K-24K req/min uncached. Found missing index `WorkflowInstances(EntityType, EntityId, TenantId)`. No OTel. State sync inconsistent (Cases command-driven, Forms event-driven).

Proposed perf-first P0-P9 roadmap WITH binding layer. Then realized: "This is corebase. 5-10 projects will clone it."

**Iteration 3 mess:** Proposed base handler abstraction + scaffolding template generator + binding layer (Option F). Thought I was solving the general case. User response: "em overthinking quá" — stopped me cold.

**The reset:** User clarified actual vision: "Admin defines workflow + forms, bundles into business process. User picks process → system renders forms per state."

**AHA moment that should've come 2 hours earlier:** This isn't a CODE-BASED extension platform — it's a LOW-CODE PLATFORM. Cases module is generic runtime. Admin config UI is the business logic layer.

The kick in the teeth: **Low-code vs code-based architecture are completely different paradigms**. The question should've been asked at minute 5, not minute 120.

## Technical Details

**Iteration 3 bloat (DISCARDED):**
- Binding registry ceremony
- Base handler abstraction (`IWorkflowBindingHandler<>`)
- Dotnet template generator for module scaffolding
- 19-day estimate with ~8 days of infrastructure plumbing

**Iteration 4 clarity (SHIPPED to planner):**
- 3 new entities in `businessprocess` module:
  - `BusinessProcessDefinition` (Code, Name, WorkflowDefinitionId, IntakeFormTemplateId)
  - `BusinessProcessStateForm` (state-entry form)
  - `BusinessProcessTransitionForm` (confirm-action form)
- Plus `FormTemplateVersion` in Forms module (immutable snapshot on Publish)
- 11 phase files + 17 red-team findings
- 14.5-day estimate (scope reduced from 19d, P0-P2 perf work kept separate)

**Red-team critical catches (pre-code, 10x value):**
- **R1 (FormSubmission immutability):** FormSubmission is IMMUTABLE (gov audit pattern). User decision 6 "edit in-place" would violate this. Planner proposed query-latest append-only pattern. Code would've broken at integration test 4h in.
- **R2 (prod data risk):** Memory flagged `replicaCount=2` prod. Cases table empty per tenant → DROP+recreate safe. Mitigation confirmed.
- **R3 (version migrate infra):** `WorkflowInstance.RecordMigration` + migration `20260412144356` ALREADY SHIPPED (T4-05 closeout). Scope revised 1.5d → 0.5-1d.

**Other resolutions:** R16 (CaseStatus drift) → user chose DROP enum, use string for arbitrary BP state flexibility.

## What We Tried

1. **Option A (FK template):** Direct FK Case → Workflow. Rejected — breaks polymorphic design (Forms, Messages also use WorkflowInstance).
2. **Option B (binding registry):** Central registry mapping entity type to workflow ID. Rejected — ceremony too high for corebase cloning.
3. **Option C (convention-based):** Infer workflow from Case.Code prefix. Rejected — fragile, unclear.
4. **Option D (hybrid):** FK + registry fallback. Rejected — overengineered.
5. **Option E (perf-first binding):** Cache + index + OTel. Rejected — architecture wrong, just plastering symptoms.
6. **Option F (base handler + scaffolding):** Abstraction for module reuse. **USER REJECTED** — overthinking for low-code platform.
7. **Option G (low-code config-driven — APPROVED):** Admin UI configures workflow↔form binding. System renders generically. **14.5 days, 11 phases, clean separation of concerns.**

## Root Cause Analysis

**Why overthinking happened:**

1. **Incomplete context:** "How do we know which workflow applies?" sounded like a DESIGN GAP, not a DEPLOYMENT GAP.
2. **Corebase bias:** Defaulted to "make this reusable for 5-10 cloned projects" too early. Valid concern, but premature abstraction.
3. **Perf rabbit-hole:** Real findings (Workflow cache gap, index missing, OTel absent) were true — but architecting around them instead of keeping them separate P0/P1.
4. **Pattern recognition failure:** Didn't ask "is this a low-code platform or code-based extension model?" early. Architecture hinges on this distinction.

## Lessons Learned

**Architecture discovery must surface deployment model first:**
- Ask "is this platform or product?" at minute 5
- Ask "will projects clone this or extend it?" before proposing abstraction layers
- Low-code + code-based = orthogonal design spaces, not a spectrum

**Red-team catches value before code:**
- R1 (immutability violation): ~4h debugging + rebuild if missed
- R3 (migrate infra already shipped): 1.5d waste if duplicated
- Pre-code red-team = 10× cheaper than post-code

**Overthinking pattern recognition:**
- 3+ design options without clarity on constraints = spinning
- User "em overthinking quá" = signal to reset, not signal to work harder
- Simpler design wins: 3 entities + admin UI > base handler + registry + templates

**Perf findings are orthogonal to architecture:**
- Workflow cache (P0), index (P0), OTel (P1) are REAL and VALID
- But they're not blocking shape — they're blocking performance
- Don't confuse "we need to fix this" with "we need to redesign for this"

## Next Steps

1. **Finalize:** All 11 phase files in `plans/260420-1957-business-process-definition-mvp/` ready for CODE phase
2. **Prerequisites (P1):**
   - FormTemplateVersion entity + migration (Forms module)
   - Case.CaseStatus enum → string refactor
3. **Parallel track (P0 — can start while Form scaffolding):**
   - Workflow cache layer implementation (leveraging `TwoTierCacheService` pattern from T4-05)
   - Index: `WorkflowInstances(EntityType, EntityId, TenantId)`
4. **Code phase:** Delegate to cook agent, phase-01 → phase-09 sequential, P10 deferred
5. **Red-team sign-off:** Planner confirmed all RESOLVED, 0 BLOCKED markers

**Owner:** cook agent (delegation ready)  
**Timeline:** Phase-01 starts when approved  
**Risk:** FormSubmissionImmutability pattern (R1) — code-reviewer must validate at P2 integration

