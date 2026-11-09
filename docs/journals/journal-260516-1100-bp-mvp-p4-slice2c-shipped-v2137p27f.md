# BP MVP P4 Slice 2C Runtime Endpoints — P4 Complete

**Date**: 2026-05-16 ~13:00 ICT
**Severity**: Medium
**Component**: BusinessProcess MVP P4 (runtime orchestration + cross-module composition)
**Status**: Shipped

## What Happened

Slice 2C shipped atomically — 3 BP runtime endpoints orchestrating Forms + Workflow cross-module clients, wrapped in transactional boundaries. Start Run bundles intake form snapshot. Transition discriminates: forms required or success direct. Closes P4 fully (4 ships, 3.5h total, 09:15→13:00 ICT). Commits `332405aa` feat + `f3ac48bb` docs on origin/main.

Scope crept slightly during cook (~0.6d actual vs 0.3-0.5d plan): Advisor pre-cook discovered Q1+Q2 contract gap in Workflow module (3 additions: `CreateInstanceByInternalIdAsync`, `ResolveTransitionIdAsync`, `NewStateInternalId` extension). Planner had named only 1. All 3 verified legitimate, bundled in-band.

Code-reviewer M-2 caught real bug: handler silently dropped partial form payloads when `TransitionFormData != null && TransitionFormTemplateId is null`. Explicit validation gate + regression test fixed inline (43/43 pass).

## The Brutal Truth

This felt like the closure sprint it was — 4 ships in 3.5h compresses attention. The relief is real: P4 blocks nothing further (P6/P7/P8 all genuinely unblocked now, grep-verified). But the scope-creep, even minor, stung because memory from Slice 2B *lied* about unblocking P6/P7/P8. We believed it. Advisor caught it again here. This is muscle memory we're building, not confidence. The Workflow contract extensions should have been in the plan, but they weren't *documented* anywhere — only discoverable by reading vestigial comments in InProcessWorkflowModuleClient.

The fatigue is compressing; we're on ship #4 of the day. The code quality held (9.4/10 code-review), but I felt the clock pressure during integration-test write. Deliberate pausing saved us from wiring forms backwards.

## Technical Details

**3 endpoints:**
- `POST /api/v1/business-processes/{id}/runs` → `201 Created` + `RunInstanceDto` + bundled `IntakeForm` (if configured)
- `POST /api/v1/business-processes/runs/{instanceId}/transitions` → `200 OK` (`ExecuteTransitionResult.RequiresForms=true` OR `Success=true` with `TransitionFormSubmissionId`)
- `POST /api/v1/business-processes/runs/{instanceId}/state-forms/{stateId}` → `200 OK` + form snapshot

**New contracts (Workflow.Contracts):**
- `CreateInstanceByInternalIdAsync(long bpInternalId, ...)` — wraps domain CreateInstance, returns `WorkflowInstanceResult` with T2 PK shadowed as `InitialStateInternalId: long`
- `ResolveTransitionIdAsync(long workflowDefInternalId, string currentStateCode, ...)` → `long transitionInternalId` (R8 ordering: lookup BEFORE ExecuteTransitionAsync)
- `ExecuteTransitionResult.NewStateInternalId: long` extension (matched inbound `transitionId` shape; `Guid NewStateId` kept vestigial per Phase-4c cleanup intent)

**New BP.Infrastructure helpers (IBusinessProcessRuntimeQueries):**
- `GetRunByInstanceIdAsync` (same-schema, form composition)
- `GetStateFormAsync` (same-schema, TenantId defense-in-depth both sides)
- `GetIntakeFormDataAsync` (same-schema JOIN, resolved via Advisor pre-cook)

**Patterns held:**
- Pattern-A tenant guard pre-DB on all 3 handlers (0 SystemAdmin bypass on writes)
- `TransactionScope` on Start Run + Transition (form-bundled paths only)
- `CrossSchemaSqlScanner` green — 0 violations
- `IReadDbConnection` + Dapper Async for queries (no EF)

**Failures caught:**
- M-2 (code-review): Partial form payload silently dropped when `Data != null && TemplateId is null`. Fixed: explicit `ValidationError("Form data requires template ID", "TransitionFormTemplateId")` + regression test.
- M-1 (code-review): No explicit FluentValidation classes for form shape (plan §9). YAGNI held — Workflow domain + contract validation sufficient.
- M-3 (code-review): Cross-tenant Access returns 422 `ValidationError`, not 404 (module consistency). Filed FU2.

Tests: 14 unit (NSubstitute, IReadDbConnection mock) + 3 integration (real tx scope, `DbContext.Local.Should().BeEmpty()` proof) = 17/17 pass. Architecture 97/97 + Forms.Application 155/155 + BP.Application 28/28 preserved.

## What We Tried

**Pre-cook advisor pass (saved ~1h):**
1. Grepped Workflow.Contracts for "CreateInstance" — found only DomainEvent (no method); advisor pushed for explicit check in InProcessWorkflowModuleClient.
2. Read Phase-4b vestigial comments → found 3 contract gaps, all legitimate.
3. Verified P6/P7/P8 callers against Slice 2C scope: P6 needs PrefillContext (Slice 2C independent); P7 is FE only (unblocked by P2-Write); P8 genuinely needs Slice 2C StartRun response.

**Integration test wiring (caught ordering bug):**
- First draft: `ExecuteTransitionAsync` before `ResolveTransitionIdAsync` — would fail with "transition not found in state" (Form approval path routes before state lookup).
- Caught mid-test by reading Workflow module test precedent (R8).
- Flipped order, added intermediate assertion, tests now catch N→N+1 state transitions correctly.

**Form composition edge case (M-2 M-catch):**
- Handler originally: `if (request.TransitionFormData != null) { ... }` — no check for paired `TransitionFormTemplateId`.
- Result: forms silently dropped, transition succeeded with null submission ID.
- Reviewer called it. Fixed inline with explicit gate + error message.

## Root Cause Analysis

**Scope creep (Q1+Q2 gap):**
Not oversight — legitimate contract discovery. Workflow module *did* have those 3 elements, but they weren't in the formal contract surface area. Planner named only 1 because plan was written before advisors pre-cooked. This is a process gap, not a technical one: contract discovery should happen in planner pre-flight, not mid-cook.

**Memory lie (unblocks claim):**
Slice 2B memory carried "Unblocks P6/P7/P8" from plan. Grep in 2B showed P6 + P7 already unblocked by earlier slices; 2B only unblocked P8. This session repeated the same trust-memory-over-grep mistake *until* advisor pushed. We're learning, but the pattern is fragile.

**Fatigue compression (ship #4):**
By 13:00, mental load was real. M-2 bug caught by *external reviewer*, not internal scrutiny. If we'd shipped without review, form-drop silent failure would have surfaced in integration testing (FU scope, not regression).

## Lessons Learned

1. **Advisor pre-cook scope-collapse is the highest-leverage tool we have.** Planner + advisor together caught 3 contract gaps + 1 ordering bug + 1 silent-failure bug = 5 fixes before review. YAGNI'd 2 more (FluentValidation, module-wide 422 refactor). Cost: ~30min advisor call. Saved: ~1h+ of debug spirals + 1 regression at ship.

2. **Memory ≠ truth. Grep before believing "unblocks N" claims.** Slice 2B memory lied about P6/P7/P8. Same mistake resurfaced here (we re-checked, found P6 genuinely needed P8, verified). Codify: when accepting carry-over, pull-req the actual dependency chain, don't trust narrative.

3. **Vestigial fields with documented cleanup intent are cheaper than breaking changes.** `Guid InitialStateId` + `Guid NewStateId` in Workflow results are unused-but-shadowed by `long` internal IDs. Keeping them costs 2 extensions + 1 comment (`// Phase-4c cleanup: remove`). Breaking would cost a Major bump or cross-module coordination. Right call.

4. **Discriminated request shapes need paired-mandatory gates.** `TransitionFormData + TransitionFormTemplateId` is not "optional form", it's "required-if-transition-is-form-approval". One null, the other present = bug. Explicit validation at handler boundary (not domain assumptions) prevents silent drops.

5. **Ship #4-of-day fatigue is real. Code-review caught M-2 because review is async separation of concern.** Solo-shipper fatigue would have missed form-drop bug. Process works because we *have* async review. Shipping in tight cadence requires external reviewers, not solo pushes.

## Next Steps

- **P4 unblocked P6/P7/P8:** grep confirmed, ready for intake-form prefill + FE designer + FE Run picker phases.
- **P4-2C-FU1 (Low):** FluentValidation consistency audit across BP module (3-4 handlers).
- **P4-2C-FU2 (Medium):** Module-wide 422→404 decision for cross-tenant Access (forms.contracts precedent).
- **P4-2C-FU3 (Low):** `NewStateInternalId` integration test coverage (3 transitions, 1 snapshot per state).
- **Prior ships' FU still open:** P3-FU1 (TenantIsolation fixture), P4-2A-Write-FU1/FU2 (TOCTOU, WfDefId), P4-2A-Read-FU (LIKE-escape, ListActive 100-cap doc).

Bundle this journal + Slice 2B journal for next session's first docs commit (untracked-at-close pattern established since 2A-Read 09:15).

---

**Status:** DONE
**Summary:** P4 Slice 2C shipped 3 runtime endpoints + Workflow contract extensions; code-review caught partial-form-payload bug pre-ship. P4 fully closed, P6/P7/P8 genuinely unblocked (grep-verified). 4 ships, 3.5h, 09:15→13:00 ICT.
