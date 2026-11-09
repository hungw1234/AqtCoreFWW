# Journal — Debug session: antd deprecations, chat hub CORS+schema, transient SQL

**Date:** 2026-06-03 17:35 +07
**Commits:** `9eaca113` (antd Spin→CenteredSpin), `cf6bc5cc` (inbox tabs + chat hub FE), `cf5acc5d` (collab schema in broadcast query)
**Trigger:** 7 sequential `/ck-debug` reports from live browser/curl while exercising the app.

## Session goal
Triage and fix a string of runtime issues surfaced while using the running dev stack (inbox, chat, permissions).

## What worked (verified)
1. **antd Tabs deprecation** — `destroyInactiveTabPane`→`destroyOnHidden` (antd 5.29). tsc 0 (`cf6bc5cc`).
2. **antd Spin `tip` warning** — standalone `<Spin tip>` warns + drops label on 5.x. New shared `CenteredSpin` (nest pattern) routes 7 call sites + `route-loading-spinner`. tsc+eslint 0 (`9eaca113`).
3. **Inbox empty-table flush** — `<AdminContentCard noPadding>` + Tabs `padding:'0 16px'` left 0 bottom gap. Verified via live DOM (gap 0→16px). Fixed in `cf6bc5cc`.
4. **Chat hub CORS** — chat used absolute `VITE_API_BASE_URL` → cross-origin negotiate failed preflight on `x-requested-with`. Switched to relative `/hubs/chat` (dev proxy, ws:true) matching notifications. Verified: negotiate 200, realtime message delivery (`cf6bc5cc`).
5. **Chat message 500** — `MessageSentSignalRHandler` raw SQL hit `dbo.Messages`/`dbo.Conversations`; tables live in `collab`. Handler runs sync on `MessageSentEvent` publish → 500 after persist. Qualified with `collab`. Verified: API restart + `POST /api/v1/messages` → 200 + realtime (`cf5acc5d`).
6. **"stopped during negotiation"** — chat hook stopped the connection on refCount=0, racing in-flight negotiate during StrictMode unmount→remount. Deferred teardown via `queueMicrotask` + refCount re-check. Verified: error gone, realtime preserved (`cf6bc5cc`).

## Diagnosed, deliberately NOT fixed
- **Table phantom horizontal scrollbar (empty table)** — antd hardcodes `min-width:100%` for any `scroll.x` → fractional width vs integer container → sub-pixel overflow under display-scaling. `x:'max-content'` does NOT fix (proved: inline style still `min-width:100%`). Systemic (~40 tables). User chose **D = accept** (cosmetic, dev/scaling-only, zero functional impact). Real fixes (remove `scroll.x` / global CSS) disproportionate.
- **/me/permissions 500 transient** — burst ~50× SqlException "connection forcibly closed during pre-login handshake" (error 10054) at 10:21–10:23 UTC, self-recovered (all API 200 after). Container healthy (no OOM this time). EF has no `EnableRetryOnFailure` (deliberate) so transient blip surfaced as 500. Likely dev-box memory pressure during heavy rebuild window. NOT a code bug.

## Lessons
- **EF "add EnableRetryOnFailure" message is a red herring** — the real exception was the inner SqlException; suggested fix is an architecture-wide change (15+ DbContexts; breaks 18 explicit `BeginTransaction` unless wrapped in execution strategy). Don't auto-apply the framework's suggestion.
- **`x:'max-content'` ≠ remove-the-fractional-source** — verified the actual rendered inline style before committing to a fix; it disproved the chosen approach (option B), reverted cleanly.
- **Sibling-as-oracle** — notifications hub (relative URL, no CORS) and the other 39 collab queries (collab schema) both pinpointed the lone deviating file. Convention grep beats guessing.
- **Verify-before-claim, repeatedly** — every fix confirmed with fresh evidence (tsc/eslint/live-DOM/curl 200/realtime delivery), not "should work".

## Test status
Per-fix: tsc 0E, eslint 0 (FE); BE build 0E/0W; live curl 200 + realtime delivery. No automated test added (runtime bug fixes; covered by existing suites). Note: chat hook teardown change has no unit test (lifecycle/StrictMode — manual-verified).

## Next session
- `COLLAB-BROADCAST-RESILIENCE` (T4) — wrap SignalR broadcast in try/catch so realtime failure doesn't 500 the write.
- `SQL-TRANSIENT-RETRY-STRATEGY` (T3) — if prod resilience wanted, adopt `EnableRetryOnFailure` with execution-strategy-wrapped transactions (design + test; deliberate-omission today).
- Dev-env: consider `mem_limit` for SQL container to avoid transient connection storms under build load.
- Demo `019e8c5f…` conversation has test messages from debugging — clean via UI if desired.
