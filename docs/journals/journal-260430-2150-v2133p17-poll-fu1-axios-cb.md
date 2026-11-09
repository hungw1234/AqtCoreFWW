# Journal — v2.133p17 POLL-FU1 Axios Circuit Breaker

**Date:** 2026-04-30 ~21:50 ICT
**Branch:** main (direct, no feature branch — single-file FE patch)
**Plan:** [plans/260430-2136-poll-fu1-axios-circuit-breaker/](../../plans/260430-2136-poll-fu1-axios-circuit-breaker/)

---

## Context

POLL-FU1 was P2 backlog item from v2.133p16 FE polling/retry storm follow-ups. Spec: rolling-window network-failure counter ở axios layer → pause new requests with deduped toast. Defense-in-depth tier 3 trên top of:

1. **v2.133p16** — `query-config.ts` retry guard (skip status=0/4xx, 5xx 1× exponential cap 8s)
2. **v2.133p16-FU2** — `notification-signalr.ts` circuit breaker (5-fail / 5min pause)
3. **v2.133p17** *(this patch)* — `api-client.ts` axios CB (≥3 network fails / 10s window / 30s pause + dedupe toast)

User chose option **A** (FU1 only) trong câu hỏi triage; FU2/FU3 deferred chờ trigger.

---

## Implementation

### Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Threshold | 3 fails / 10s window / 30s pause | Conservative vs SignalR (5/5min) — req rate higher; spec từ backlog |
| Reject point | Inlined pre-check ở request interceptor | axios request interceptors LIFO → inlining keeps order deterministic; pre-empts auth/token work |
| In-flight cancel | Not cancelled | axios khó cancel from interceptor; pragmatic — chỉ chặn req mới |
| Auto-reset | First successful response | Spec; matches SignalR pattern |
| Toast dedupe | antd `key='backend-unavailable'` | Mirror existing 403 toast pattern |
| Test exports | `__resetBackendBreakerForTests` + `__getBackendBreakerState` | Mirror SignalR breaker test surface |

### Surface

- Modified: `web/src/core/api/api-client.ts` (110→181 lines)
- Modified: `web/src/core/i18n/locales/{vi,en}.json` (1 key `error.http.backendUnavailable`)
- Created: `web/src/test/api/api-client-breaker.test.ts` (6 vitest fake-timer cases)

### File Size

181 lines vẫn dưới ngưỡng modularization 200 — không cần split.

---

## What Went Wrong

### T2 initial failure: pre-emptive rejection re-normalized

First test run, T2 failed:
```
Expected: { code: 'BACKEND_UNAVAILABLE' }
Received: { code: '0' }
```

**Root cause:** Khi request interceptor `Promise.reject(backendUnavailableError())`, axios routes that rejection through response error interceptor. Error handler thấy `status === 0`, calls `normalizeError(0, undefined)` → returns generic `code: "0"` shape, overwriting custom error.

**Fix (api-client.ts:117-121):**
```typescript
if (!error.isAxiosError) {
  return Promise.reject(error);
}
```
Early-return for non-AxiosError preserves our custom shape. Code-reviewer P2 nudged comment scope to also document axios.Cancel coverage (applied in-PR).

**Lesson:** Custom rejection from request interceptor passes through response error handler — always check `isAxiosError` to short-circuit.

### Initial wrong interceptor ordering attempt

First draft registered breaker check as separate interceptor BEFORE auth interceptor, comment claimed "runs FIRST." Wrong — axios request interceptors are **LIFO** (later registration runs earlier). Caught immediately during self-review trước khi run tests. Fixed by inlining the pre-check at top of single interceptor body.

**Lesson:** Re-verify axios docs on interceptor ordering — counter-intuitive vs response-side (FIFO).

---

## Test Results

- **New tests:** 6/6 pass (T1 below-threshold, T2 trip+reject, T3 30s expiry, T4 success-reset, T5 sliding-window prune, T6 toast dedupe)
- **Existing api-client.test.ts:** 13/13 pass (401 silent renew unchanged)
- **Full FE suite:** 824/836 pass — 12 failures pre-existing (jsdom/Antd Modal compatibility, matches `c5-finalize-blockers` memory; tester verified 0 regressions caused by changes)
- **TypeScript:** clean
- **Code-review:** 9.2/10 APPROVE_WITH_MINOR (0 P0/P1)

---

## Mirror Fidelity to SignalR CB

| Aspect | SignalR CB | Axios CB | Justification |
|--------|-----------|----------|---------------|
| Counter type | Consecutive | Sliding window | Higher req rate → window pruning prevents stale tally |
| Threshold | 5 | 3 | Lower bar OK because window-bounded |
| Pause | 5 min | 30 s | Axios CB chỉ chặn new req; recovery cần ngắn hơn |
| Toast | None | `key='backend-unavailable'` | User cần feedback explicit khi axios chặn UI actions |
| Test exports | `__resetSignalRBreakerForTests` + `__getSignalRBreakerState` | Same naming pattern | Consistency |

---

## Operational Concerns

- **Tuning:** 3/10s/30s là educated guess. Sau prod monitoring, có thể cần điều chỉnh. Backlog đã ghi rõ trigger gates.
- **Per-tab state:** Module-scoped (in-memory). Refresh resets — acceptable, expected UX.
- **Cross-tab independence:** Each tab có own breaker. Acceptable; per-tab UX consistent.
- **No metric emission:** `console.warn` only. Add nếu prod monitoring cần.

---

## Out of Scope (Deferred per Backlog)

- **POLL-FU2** (P2, ~1d, BE+FE breaking) — BE aggregate endpoint `/admin/dashboard/aggregate`. Trigger: prod admin dashboard >50 active tabs OR Hangfire job count latency >2s.
- **POLL-FU3** (P3, 3-5d) — SignalR push migration cho counters. Trigger: real-time UX requirement OR cost analysis polling >$X/month.

---

## Next Steps

- Monitor prod cho `BACKEND_UNAVAILABLE` toast frequency — tune threshold nếu cần
- Optional: schedule agent in 1-2 weeks để check telemetry và xem có cần cook FU2/FU3
