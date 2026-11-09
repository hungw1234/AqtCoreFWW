# POLISH-D: File Upload Scan UX — Poll Timeout & Error Toast

**Date:** 2026-06-16 15:00 ICT · **Slice:** `POLISH-D-FILE-UPLOAD-SCAN-UX` (Theme D, Forms upload widget) · **Scope:** FE-only, 0 BE↔FE contract change · **Commit:** `acf6ee31` (8 files), pushed origin/main, 0 ahead.

## What Happened

File-upload scan UX gap: when antivirus scan polling times out or fails, uploader was **fully silent** — no red badge, no toast, no recovery option. User can't tell whether the system is stuck or the file is quarantined. Row premise was wrong about the symptom visibility; audit-at-slice-start corrected the fix shape before implementation.

## The Brutal Truth

The row claimed defect = "upload error shows red badge but no toast." Reality: `<Upload showUploadList={false}>` suppresses antd's error list. When scan poll expired, `file` lifecycle stepper returned null (no render), so UI was **completely silent**. The code line `setFileList([{status:'error',…}])` was dead — never reached because the polling gate prevented submission. Audit-at-slice-start (INVARIANT 260522-A, 10th payoff) prevented coding against a misdiagnosed symptom.

## Technical Details

### Audit Finding → Corrected Fix Shape

Row premise: "Add toast next to existing error badge."  
Reality: "Delete dead state assignment, add toast from the polling timeout gate."

Empirical trace revealed 2 dead points:
1. `setFileList([{status:'error'}])` in `onError` callback — unreachable because `showUploadList={false}` suppresses the entire antd list + null stepper return = no render at all.
2. Toast was already wired in the success path (`refetchInterval` reset); only missing from the timeout path.

### Shipped — FE Web (`web/src/features/files/`)

**file-lifecycle-steps.tsx**: 
- New `deriveFilePhase(file, fileStatus, queryState)` helper: reads QueryClient state during render to detect cumulative poll timeout. Returns phase enum: `uploading | scanning | timedOut | success | error`.
- Timeout detection: uses `queryClient.getQueryState(key)` to read the **same Query instance** the observer subscribed to. Resolves on final poll's completion render (no stale read, no flicker across 3 logic steps).
- Two guards on precedence: if `fileStatus.status === 'quarantined'` the phase skips to `error` (ScanStatus takes precedence over retry-count). If status is terminal (Available/Rejected/Quarantined) the retry loop exits before checking `failureCount`.

**form-field-renderer.tsx**:
- Wrapped `<Upload onError>` in try/catch to surface toast. Previously: if upload mutate threw (e.g., network), rejection was unhandled.
- Reused `extractApiErrorMessage` util (shipped same day in POLISH-B) to unify error shape extraction. Undefined-for-bare-Error contract prevents internal-message leaks.
- Removed dead `setFileList` assignment on onError (now gate logic prevents reaching that branch anyway).

**file-list-page.tsx**:
- Out-of-scope per row specification (row named the Forms widget specifically). Uploader here could trivially adopt the same `timedOut` fallback + toast, but filed as optional follow-up, not silently widened.

### Poll Timeout Gate Logic

`useQuery` result exposes `failureCount` (resets on success) but **NOT `dataUpdateCount`**. The refetchInterval gate checks cumulative `dataUpdateCount + failureCount`:

```typescript
const queryState = queryClient.getQueryState(queryKey);
const dataUpdateCount = queryState?.dataUpdateCount ?? 0;
const fetchFailureCount = queryState?.failureCount ?? 0;
const timedOut = (dataUpdateCount + fetchFailureCount) >= MAX_SCAN_POLL_ATTEMPTS;
const canRetry = timedOut ? false : true; // Gate decides whether to refetch
```

Naive `refetch()` call alone ticks the counter past the cap → the interval gate immediately returns false again → loop never resumes. Must use `resetQueries(key)` to zero both counters before retry (manual button).

## What We Tried

Audit-at-slice-start: grepped file upload error paths, traced `onError` callback to dead code via `showUploadList={false}` gate. Empirical render-path verification (not just handler review) prevented wrong fix shape.

Tester: vitest 101/101 (+6 new tests for timeout/precedence logic, 2 isolation checks to prevent jsdom contention flakes on poll-state reads). All green.

## Root Cause Analysis

**Row misdirected the diagnosis** (INVARIANT 260522-A, 10th): Backlog row described UI symptoms without verifying whether the symptom was even renderable. The antd error badge exists *only* when `showUploadList={true}`. This project uses `showUploadList={false}` (custom file list), so the badge never rendered and no toast could be "next to" it. Audit-at-slice-start caught this by tracing the render path, not trusting the symptom name.

**Poll timeout detection requires cumulative counters**: `useQuery.failureCount` alone is insufficient because retries can succeed partway, resetting failureCount. Must read QueryClient state directly during render to get `dataUpdateCount` (never reset). A lone refetch() call bypasses the gate and re-enters the infinite loop.

**Retry = reset, not refetch**: Users expect a "Retry" button to *actually* retry from the beginning. A `refetch()` on an exhausted query still fails immediately because the gate rejects it again. Must `resetQueries` to zero counters.

## Lessons Learned

1. **Audit-at-slice-start pays off again (10th payoff of INVARIANT 260522-A)**: Row named UI symptoms without verifying they're even observable. Empirical render-path trace caught a misdirected fix before any code. Grammar in the row ("error badge + toast") was fatally misleading; code archaeology was necessary.

2. **Poll timeout detection must read cumulative state, not hook result**: useQuery's `failureCount` resets on success; gate logic needs permanent `dataUpdateCount`. ReadQueryClient during render, not after (stale). Use same Query instance the observer subscribed to.

3. **Retry of a timed-out poll = resetQueries, not refetch**: One lone refetch() past the cap re-triggers the gate immediately → poll never resumes. This is a footgun for any manual-retry pattern (button, toast action). Document it.

4. **DRY dividend from same-day POLISH-B slice**: `extractApiErrorMessage` util was dropped in same morning; reused verbatim for upload-error toast. Its undefined-for-bare-Error contract is exactly what prevents leaking internal errors to user toast.

5. **Scope discipline: out-of-scope is better than silent scope creep**: Files-page uploader could now trivially adopt the same timeout fallback. Did not silently widen — filed as optional follow-up in backlog. The row named Forms widget; it stays scoped to Forms.

## Verification

- `npm run typecheck` (app) 0E. `eslint 0E (2 pre-existing react-refresh warnings, not regression).
- `npm run vitest -- files forms` 101/101 (+6 new tests for timeout detection, poll precedence, manual-retry reset).
- Isolation: mock `queryClient.getQueryState` to return staged state + manual `resetQueries` to verify gate reopens. 2 runs isolated (no flake, jsdom contention risk mitigated).
- i18n: added `scanTimeoutMsg`, `scanRetryLabel` to vi.json + en.json; marked `onUploadScanError` as internal (not user-visible, replaced by toast).
- Code-review: APPROVE_WITH_NITS; 0 Critical/High/Medium. 2 Low (YAGNI: optional scope-widening for file-list-page + pre-scan cache optimization).

---

**Status:** DONE

**Summary:** FE-only upload scan timeout UX fixed. Audit caught row misdirected the symptom; corrected fix shape before code. Poll timeout gate reads cumulative QueryClient state; manual retry resets counters. No BE changes. All tests pass; scope held.

