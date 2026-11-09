# Phase 4-A: Reassign + Assign IDOR Hotfix — Live Exploitable Gap Closed

**Date:** 2026-05-21 11:30
**Severity:** Critical
**Component:** WorkflowTasks controller, ReassignTaskCommandHandler, TaskAssignmentType enum
**Status:** Resolved (LOCAL, awaiting push + PR + auto-merge)

---

## What Happened

Live IDOR discovered on `POST /api/v1/workflow/tasks/{id}/reassign` endpoint during planner validation pass on Phase 4 spec. Endpoint had only class-level `[Authorize]` (authenticates but does not authorize) — any logged-in user could reassign ANY task in ANY tenant. Severity: **exploitable for 5+ days** during entire Phase 3 ship cycle (2026-05-16 through 2026-05-21). Frontend had no client-side gate to mask the gap; backend was the only defense and it failed.

Code-reviewer caught **sibling endpoint** `POST /api/v1/workflow/tasks/{id}/assign` with identical IDOR class during 4-A review (H1 discovery). User ratified folding both into single 4-A commit per bug-class discipline (cost: 5min + 1 integration test).

---

## The Brutal Truth

This is infuriating because:
- **Plain `[Authorize]` is not a security gate.** It only checks "is this request authenticated," not "does this user have permission." The codebase convention is `[Authorize(Policy = "...")]` — this endpoint missed the Policy parameter entirely. No code-reviewer, no planner pass for 5 days caught it.
- **Two endpoints, same bug.** The sibling `/assign` carried identical vulnerability. If we shipped a hotfix for one and left the other live, that's an incomplete mitigation. User correctly overrode the "out of scope" instinct to fix both.
- **No security audit in place.** A routine grep for `[Authorize]` without `Policy =` should be non-negotiable pre-flight for any controller change. It wasn't. This slipped because authorization review was reactive (code-reviewer caught it) not proactive (planner should have flagged it).

---

## Technical Details

**Vulnerability class:** IDOR (Insecure Direct Object Reference) via missing permission policy.

**Controller code before fix (lines 67, 85):**
```csharp
[HttpPost("{id:long}/assign")]
[Authorize]  // ← only checks "is authenticated"
public async Task<IActionResult> Assign(...)

[HttpPost("{id:long}/reassign")]
[Authorize]  // ← same gap
public async Task<IActionResult> Reassign(...)
```

**Fix applied:**
```csharp
[HttpPost("{id:long}/assign")]
[Authorize(Policy = PermCodes.Workflow.Assign.Manage)]  // ← now checks "has permission"
public async Task<IActionResult> Assign(...)

[HttpPost("{id:long}/reassign")]
[Authorize(Policy = PermCodes.Workflow.Assign.Manage)]  // ← now checks "has permission"
public async Task<IActionResult> Reassign(...)
```

**Guard implementation (ReassignTaskCommandHandler, lines 44-69):**
- Pre-state snapshot: `wasAlreadyClaimed = task.AssigneeId.HasValue` **before** mutation
- Branch logic: `wasAlreadyClaimed ? TaskAssignmentType.ReassignAdmin : TaskAssignmentType.Reassign`
- Stomp validation: if `wasAlreadyClaimed && string.IsNullOrWhiteSpace(request.Reason)` → `ValidationError` → 422
- Reason propagated verbatim into audit row (load-bearing for future "reason mangled" regression detection)

**Audit type preservation:**
- `TaskAssignmentType.Reassign` (1) = unclaimed → claimed (existing behavior, backward-compat)
- `TaskAssignmentType.ReassignAdmin` (7) = claimed → reclaimed by admin with stomp + reason (new)
- Enum gap-free: 0-6 pre-existing, 7 new, no collisions

**Test gates (5 integration facts + 11 unit facts):**
- F1: anon 401 (proves policy fires, not global filter)
- F1b: authenticated non-admin 403 (proves policy gates non-admins)
- F2: reassign unclaimed → audit type `Reassign`, assignee updated
- F3: reassign claimed → audit type `ReassignAdmin`, reason verbatim in DB, assignee updated
- F4: missing reason on stomp → 422 response, "reason" in body (field-name proof)

Build: 0E/0W; Workflow.Application unit: 491/491 PASS; Workflow Integration: 5/5 PASS.

---

## What We Tried

N/A — this was a discovery + fix slice. No iteration needed; code-reviewer APPROVE 9.4/10 on first pass (9 findings tracked, none blocking).

---

## Root Cause Analysis

**Primary:** Missing parameter in policy attribute. Planner copy-pasted `[Authorize]` from class definition or existing code and did not add `(Policy = ...)`. Code-reviewer inherited this gap across 5 days of Phase 3 work (no re-review of controller files for auth hygiene).

**Secondary:** No pre-flight security checklist. When authz is in scope, a grep for `[Authorize]` lacking `Policy =` should be automatic. It was not. Reactive (code-reviewer found it) beat proactive (planner should have flagged it).

**Tertiary (sibling):** Post-review discovery of `/assign` IDOR was lucky. If user had rejected "out of scope" objection, we would have shipped a hotfix for Reassign and left Assign live — same vulnerability class, same endpoint surface. Bug-class discipline would have been violated.

---

## Lessons Learned

### 4-A-A: Planner validation pass = security audit value
Routine "is the plan still valid after 5 days of implementation" check surfaced a live IDOR that no code-reviewer or test had flagged. **Invariant for future phase-4+ plans touching `[Authorize]` endpoints: planner MUST `grep` controller files for `\[Authorize\]` lacking `Policy =` and surface as pre-flight blockers.** Class-level `[Authorize]` authenticates but does not authorize; it is not a security gate.

### 4-A-B: Bug-class fix discipline
When code-reviewer finds a sibling endpoint with the same vulnerability class, fold into the same PR rather than punt to a separate slice. Cost: ~5min + 1 test. Benefit: closes the class, not just one instance. User gate at ~11:25 ICT ratified this trade-off (H1 inclusion). **Invariant: if fixing endpoint A and endpoint B is identical vulnerability type, ship both or risk incomplete mitigation.**

### 4-A-C: Anti-tautology on audit type assertions
F3 assertion on `Type == TaskAssignmentType.ReassignAdmin` + verbatim `Reason` field is the right proof-target. A status-code-only test would miss "handler returns OK but audit type silently wrong" regression. This honors the S5 META-L (c) invariant (test design MUST include precondition asserts proving WHICH code path executes).

---

## Next Steps

1. **User gate (push + PR + auto-merge):** branch `feat/phase-04-slice-a-reassign-idor-hotfix` → PR to main → auto-merge per Phase 4-A cycle (mirror PR #107 cycle from Phase 3).
2. **Phase 4-B planning (Claim/Unclaim/concurrent-test):** explicitly decide on `/assign` endpoint — either add `[Authorize(Policy = ...)]` for admin initial assignment, or split into Claim (self-assign by pool-eligible user) vs Admin-Assign. Document upfront to avoid H1 slip-through to 4-C.
3. **Carry-forward to 4-B:** absorb RT2-12 19-file regression sweep + DCAND-a F5 carry-fwd (concurrent Claim/Unclaim + RowVersion guard) + code-reviewer H3 documented risk (see review report, acceptable for 4-A scope).
4. **Backlog items (low, defer):** nvarchar length validation on Reason (pre-existing concern), reassign-to-same-assignee short-circuit (semantics TBD), /assign XML doc remark update (cosmetic).

---

## Unresolved Questions

1. **Phase 4-B scope on `/assign` IDOR:** orchestrator prompt frames 4-B as Claim/Unclaim only. Will admin-Assign be gated or split? If neither, controller retains residual IDOR surface.
2. **Reassign-to-same-assignee behavior:** should handler short-circuit if `NewAssigneeId == task.AssigneeId`, or is rewrite + audit-row the intended behavior? No business requirement in slice spec.
3. **Reason nvarchar length cap:** does `TaskAssignment.Reason` EF column have a length constraint? If yes, pre-validate to 422 rather than risk EF 500 on oversized input (not in scope but flag for 4-B grep).
