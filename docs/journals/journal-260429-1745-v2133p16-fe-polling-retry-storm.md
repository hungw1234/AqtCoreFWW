# FE Polling/Retry Storm Mitigation — v2.133p16

**Date**: 2026-04-29 17:45  
**Severity**: High  
**Component**: Frontend (React Query, SignalR, polling)  
**Status**: Resolved  
**PR**: https://github.com/thiennd212/AqtCoreFW/pull/68

## What Happened

User reported: *"sao có nhiều api call liên tục vào API vậy"* — DevTools Network showed 362/676 requests with `ERR_CONNECTION_REFUSED` to localhost:5001 when BE was temporarily DOWN.

Deeper investigation revealed NOT a bug, but **design accumulation**:
- react-query default `retry: 1` for every error (network, 4xx, 5xx) → duplicate requests
- SignalR `withAutomaticReconnect([0, 2, 5, 10, 30s])` with consumer re-mount → infinite exponential backoff loop  
- 10+ polling timers (notification 30s, inbox 60s, dashboard 5m, admin-dashboard 5×60s, workflow 10s/30s, backup 10s, report dynamic) accumulating without auth gate

## The Brutal Truth

This is the classic **death by a thousand cuts** — each component individually sensible, but combined they create a storm. react-query's default `retry: 1` looked innocent until paired with SignalR's unbounded backoff + polling timers triggered during StrictMode re-mounts. Nothing was obviously broken; everything just thrashed in production.

The 401 silent renew + axios replay layer made react-query's retry a **redundant re-query** — we were double-hitting endpoints.

## Technical Details

Network captures showed:
- 362 failed requests to localhost:5001 (backend down)
- 676 total requests in 5-minute window (~2.2 req/sec sustained)
- Top offenders: notification SignalR (continuous reconnect), admin-dashboard counters (5 parallel pollers at 1min each)
- 401 responses triggering both axios-interceptor replay AND react-query retry

Error pattern: `ERR_CONNECTION_REFUSED` → react-query retry + SignalR auto-reconnect simultaneously → exponential backoff collision.

## What We Tried

1. **axios circuit breaker** — DEFERRED (overlaps with P0 scope)
2. **BE aggregate admin-dashboard endpoint** (5 → 1 poller) — DEFERRED (breaking change, out of scope)
3. **SignalR push migration** for counters — DEFERRED (large refactor)
4. **Auth gate top-4 critical pollers** — APPROVED
5. **Custom react-query retry strategy** — APPROVED

## Root Cause Analysis

react-query's permissive default `retry: 1` + SignalR's infinite exponential backoff assumptions created **no backstop**. Combined with 10+ independent polling timers and Suspense/StrictMode double-mounts, requests piled up during any network degradation.

The real issue: **no global retry guardrail** at app initialization — every consumer duplicated logic.

## Implementation

- `query-config.ts`: Custom `shouldRetryQuery` skips network/4xx, retries 5xx 1× with exponential backoff capped at 8s
- `notification-signalr.ts`: Circuit breaker pauses 5min after 5 consecutive failures; skip start when no user
- 8 hooks gated by `enabled={isAuthenticated}`
- 25 new tests (PASS)
- vitest: 822/830 (8 pre-existing failures, 0 new file imports changed per grep)
- code-review: 9.5/10 APPROVE, 0 P0/P1

## Lessons Learned

1. **react-query `retry: 1` is dangerous in production apps with polling** — must override globally at app level, not per-hook. Default assumes isolated request, not coordinated timers.

2. **SignalR `withAutomaticReconnect` needs hard ceiling** — exponential backoff without circuit breaker creates cascading retries. App-layer breaker mandatory.

3. **Polling timers amplify on StrictMode/re-mounts** — `enabled={isAuthenticated}` gate is cheap insurance; always gating long-lived pollers prevents 2× request storms.

## Next Steps

- Monitor Network tab in prod for sustained connection errors
- When confident: evaluate P2 axios circuit breaker (separate PR)
- Future: SignalR push migration for real-time counters (reduces polling from 5→0 for admin-dashboard)
- Document polling guidelines in code-standards.md (retry strategy + auth gating pattern)

---

**Commit**: 844e25a7 | **Branch**: fix/v2-133p16-fe-polling-retry-storm | **Files Changed**: 12 | **Tests**: 25 new
