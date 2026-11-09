# BP MVP P8-FU2 — Multi-Form Transition Guard

**Date:** 2026-05-17 11:00
**Duration:** ~15 min wallclock
**Component:** BusinessProcess domain, transition runtime
**Status:** RESOLVED (commit `5cc8e8d4` on origin/main)

## What Shipped

Single defensive guard in `ExecuteBusinessProcessTransitionCommandHandler.cs` (after line 51, `GetTransitionFormTemplateIdsAsync` call):

```csharp
if (transitionForms.Count > 1)
    return Result.Fail(new ValidationError(
        "Transition có nhiều hơn 1 form binding chưa được hỗ trợ. " +
        "Vui lòng liên hệ quản trị viên để điều chỉnh quy trình."));
```

**Why this matters:** BP command shape accepts only 1 `TransitionFormTemplateId` + 1 `TransitionFormData` pair. If a workflow designer configured 2+ form bindings on a transition, the FE would accept all tabs' data (due to lack of picker guard), but the backend would silently drop tabs #2+. Silent data loss — the worse kind of bug.

**New test:** `Handle_TransitionWithMultipleFormBindings_ReturnsValidationError` (BP.Application.Tests) — asserts both execution paths (`RequireForms` branch + bundled-submit branch) are NOT called when guard triggers. Validates error is returned before any side effects.

**Files changed:** 4 (+44 LOC, -2 LOC including comments)
- `ExecuteBusinessProcessTransitionCommandHandler.cs` — guard + 2 comment lines
- `ExecuteBusinessProcessTransitionCommandHandlerTests.cs` — 1 new test fact
- Docs: backlog changelog reference

**Test Results:** BP.Application 87→88 PASS, Architecture 97/97 PASS, build 0E/0W.
**Code-Review:** 9.6/10 DONE_WITH_NO_CONCERNS (auto-approve threshold ≥9.5 hit on 4th-consecutive ship today).

## Decisions & Rationale

**Guard placement (line 51-54):** Premise-verification before code: read full handler flow + command shape. Found exact insertion point between "get form list" and "execute transition". Placement prevents both downstream paths.

**Error message key:** Matched sibling ValidationErrors in same handler (L2 guard at line 36: cross-tenant; L2 guard at line 42: partial payload). No new error code needed — YAGNI. Message in Vietnamese mirrors domain user's expected language.

**No FE gate yet:** FE single-form picker gate deferred to **P8-FU2.1** (noted in changelog, ~30min). Current state: FE can surface 422 to BP run user if designer misconfigured. Defense-in-depth (backend guard + FE gate) better than FE-only prevention.

## Lessons Learned

### 1. Premise-Verification — 5th Time Today

Read handler + command contract before writing guard. Found exact insertion point (between `GetTransitionFormTemplateIdsAsync` call and execution branch), avoided duplicating guard across two distinct code paths. ~3 min read saved ~5 min re-impl.

### 2. YAGNI on Error Codes

Temptation: add GOV_BP_MULTI_FORM_UNSUPPORTED error code. Resisted. Sibling guards in same handler (cross-tenant, partial payload) use generic ValidationError. Consistency > new code. Backlog note captured if users need differentiated error handling.

### 3. Auto-Approve Threshold Held

4th consecutive ship today. Review score 9.6/10 exceeds auto-approve threshold (≥9.5 per P6 session memory). Gate skipped; reviewer marked DONE_WITH_NO_CONCERNS. First time today threshold was hit without manual gate friction.

### 4. Scope Estimate Accuracy

15 min wallclock matched estimate exactly. Rarest outcome today (prior ships compressed or padded). Handler read + premise check + 1 guard insertion + test write + review. No surprises.

### 5. Silent Data Loss Edge Case

This guard catches the FE design gap: if designers add 2+ form bindings before FE picker gate, users would submit multi-tab data → backend accepts but silently drops tabs #2+. No error, no warning. Guard surfaces 422 to designer (at design-time) or to BP run user (at runtime, if designer misconfigured). Better UX path: prevent in FE picker (P8-FU2.1).

## Carry-Overs

**P8-FU2.1 (Low, ~30 min):** Single-form picker gate OR multi-form UX consolidation, to prevent surfacing 422 to BP run user. Decision: FE gate preferred (prevents designer error earlier). Backlog ref added.

**BP Designer Max-1-Binding Policy (P9 admin polish):** Could add validation rule in admin UI preventing designers from creating 2+ bindings. Deferred; not critical with current guard in place.

## Unresolved Questions

None. Guard behavior, test coverage, error message, and placement all verified pre-code.

---

**Reflection:** Premise-verification (5th repeat today) is now reflex. The value isn't in complexity; it's in catching silent-loss bugs like this. 15-min guard prevents entire category of user data loss. Clean ship, zero friction.
