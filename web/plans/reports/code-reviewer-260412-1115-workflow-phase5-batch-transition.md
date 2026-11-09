# Code Review — Workflow Phase 5 Batch Transition

**Date:** 2026-04-12
**Scope:** 14 files (6 new BE, 2 modified BE, 4 new/modified FE, 2 i18n)
**Verdict:** Ship with 1 high-priority FE fix. **Score: 8/10**

---

## Summary

Solid, conservative implementation. Partial-failure semantics are clean, reuses `ExecuteTransitionCommand` via MediatR (DRY), rate limit added, max 50 items enforced, tenant isolation preserved because each item flows through existing single-transition handler which already does tenant checks. Tests cover partial failure, exceptions, cancellation, and actor propagation.

One real bug on the FE (response unwrapping). A few medium-severity polish items.

---

## Critical / Blocking

**None.**

---

## High Priority

### H1. FE `useBatchTransition` double-unwraps `.data`

`workflow-api.ts:320-323`:
```ts
apiClient.post<{ data: BatchTransitionResult }>('/workflow/instances/batch-transition', body)
  .then((r) => r.data.data)
```

Every other mutation in the file uses `(r) => r.data` (single unwrap). `ToApiResponse` in `ApiControllerBase` typically returns the payload directly as the axios response body — the extra `.data.data` will return `undefined` unless this endpoint wraps uniquely.

**Check:** `src/shared/AqtCoreFW.SharedKernel.Presentation/ApiControllerBase.cs` envelope shape — if it's `{ data: ... }` then ALL other hooks in `workflow-api.ts` are broken (they're not). More likely: this should be `(r) => r.data`.

**Impact:** `result` in toolbar will be undefined → `setResult(undefined)` → dialog never opens, `message.success(... count: undefined)` → bad UX on success path. Unit tests don't catch this (pure BE tests).

**Fix:**
```ts
apiClient.post<BatchTransitionResult>('/workflow/instances/batch-transition', body).then((r) => r.data)
```

---

## Medium Priority

### M1. Cancellation returns partial success as `Result.Ok`

Handler `Handle` breaks on `ct.IsCancellationRequested` but still returns `Result.Ok(batchResult)` with silently-missing items. Caller cannot distinguish "batch completed with 2 of 10" from "user cancelled after 2". Consider returning `Result.Fail` with partial data in metadata, or setting a `Cancelled=true` flag on `BatchTransitionResult`. Minor because controller usually cancels only on client disconnect, but the contract is ambiguous.

### M2. Exception message leaked to client as `Error`

`ExecuteOneAsync` catches `Exception ex` and puts `ex.Message` directly into `BatchTransitionItemResult.Error`, which is serialized to FE and displayed in the result dialog. Internal exceptions (`DbUpdateException`, EF key-not-found, null refs) will leak implementation details / SQL fragments to authenticated admins. Checklist: "no internal stack traces leaking to external consumers."

**Fix:** map unexpected exceptions to a generic message (e.g., `"Unexpected error"`), log the full exception server-side. Keep `result.Errors` messages (domain errors — already user-safe).

### M3. FE does not fetch transitions dynamically — admin types raw GUID

`workflow-batch-action-toolbar.tsx:113-121` asks admin to paste a transition GUID. This is error-prone (and there's no validation that the transition is actually valid for the selected instances). At minimum:
- Validate GUID format on the form (`pattern` regex).
- Better: if all selected instances share the same definition+currentState, fetch available transitions and render a dropdown. Defer to a follow-up if out of scope for Phase 5, but flag in UX debt.

### M4. Rate-limit `batch-ops` keyed by IP, not user

All named policies in `RateLimiterConfiguration` (including `batch-ops`) don't set a partition key — they fall back to the default (global) partitioning. Per the file comment: "Anonymous: keyed by RemoteIpAddress." But this endpoint is `[Authorize]`, so it should key by `sub` claim. With IP keying, shared NAT / office networks will collide. The `GlobalLimiter` does the right thing; named policies don't. Check other policies — this is a pre-existing pattern issue, but batch-ops is the most abuse-prone.

### M5. Toolbar rendered above table forces `count > 0` render logic only

`if (count === 0) return null;` — fine, but `WorkflowBatchResultDialog` is a child of the toolbar. After a batch completes, `onClearSelection` is NOT called automatically. User sees result dialog, closes it, but the 50 rows remain selected → next click re-runs the same batch on already-transitioned (and now likely invalid-state) instances. Recommend calling `onClearSelection()` after successful batch.

---

## Low Priority

### L1. `getCheckboxProps` disables completed rows, but "select all" on a page can still enable selecting a page with only completed rows (empty selection). Cosmetic.

### L2. `BatchTransitionItemResult.Error` length unbounded — a verbose exception could balloon a 50-item batch response to MBs. Consider truncating to e.g. 500 chars.

### L3. `Handler` signature says `IReadOnlyList<BatchTransitionItem>` but controller passes `.ToList()` — fine, but the command's deserialization from JSON will produce `List<>` which satisfies the interface; remove the `.ToList()` in the controller to skip one allocation.

### L4. `message` import from Antd in toolbar — in Antd v5, `message` static API is deprecated in favor of `App.useApp()`. Won't break but emits console warning.

### L5. Test file location — existing tests live in `AqtCoreFW.Workflow.Application.Tests`, the review prompt referenced `.UnitTests`. Confirm no parallel test project was created; I only found the former, which matches the existing convention.

### L6. `workflow-batch-action-toolbar.tsx` inlines styles. Project convention check needed but other components do the same — not a regression.

---

## Security (Checklist)

- Tenant isolation: preserved — each item routed through `ExecuteTransitionCommand` which resolves tenant from instance and checks against actor. **BUT** I did not verify `ExecuteTransitionCommandHandler` re-checks tenant on each iteration; trusting the comment + existing tests.
- Auth: endpoint `[Authorize]`, `ActorId = ResolveUserId()` from JWT — cannot be spoofed.
- Mass-assignment: separate `BatchTransitionRequest` record — good (GOV_SEC_004).
- Rate limit: added (M4 caveat).
- Input bounds: 50-item cap, 2000-char comment, unique instance IDs — good.
- Data leak: see **M2** (exception messages).

## Concurrency

- Sequential loop intentional to avoid DbContext races — correct.
- ChangeTracker: each `SaveChanges` clears dirty state, but entities remain tracked across 50 iterations in the same scoped DbContext. For 50 items in a per-request scope, memory is fine. The inline comment covers this.
- Cancellation: see **M1**.

## Performance

- 50 × ~200ms = 10s worst case is acceptable for an admin-only operation behind a strict 10/min rate limit. No pagination of results needed (50 items fit easily).
- No N+1 at the batch layer itself; underlying single-transition handler should already be optimized.

## Tests

- 5 handler + 7 validator = 12 tests, all the critical paths (all-ok, partial-fail, exception-in-middle-continues, sequential-count, actor-propagation, size bounds, dupes, empty actor). **Coverage adequate.**
- Missing: cancellation mid-batch behavior (M1), but low-value.

---

## Recommended Actions

1. **H1** — Fix `useBatchTransition` response unwrap (likely broken on success). 2 min.
2. **M2** — Sanitize exception messages before returning to client. 10 min.
3. **M5** — Call `onClearSelection` + invalidate instance queries on successful batch. 5 min.
4. **M4** — Audit named rate-limit policies for user-key partitioning (separate task, cross-cutting).
5. **M3** — UX follow-up: transition dropdown instead of raw GUID input.

---

## Unresolved Questions

1. Does `ApiControllerBase.ToApiResponse` wrap the result in `{ data: ... }` envelope or return it directly? Determines whether **H1** is a bug or a correct single-case exception. One quick read of `ApiControllerBase.cs` resolves it.
2. Is `ExecuteTransitionCommandHandler` guaranteed to verify tenant on every call (not cache it), given the same scoped DbContext runs 50 times? Worth one grep of that handler.
3. Should batch operations emit a single consolidated audit entry or one per item? Current design emits per-item (via underlying handler) — likely correct for compliance but confirm with stakeholder.

**Status:** DONE_WITH_CONCERNS
**Summary:** Phase 5 is structurally sound and tested. One likely FE bug (H1) and exception-message leak (M2) should be fixed before shipping; other items are polish.
