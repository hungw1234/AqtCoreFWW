# Journal — Designer Fork-Save Fix + Live-Stack Verify (2026-06-10 ~10:00)

**Slice:** `/cook WF-DAGRE-COORDS-SAVE-VERIFY + drag-fork eyeball` (interactive) · commits `1935f9f0` (fix) + docs · stack brought up via `/aqt-runapp` mid-slice.

## What happened

Two carry-over "cheap verify" rows turned into a real bug hunt. The dagre save round-trip FAILED on first try (toast "Lỗi", PUT /graph 400), and chasing the 400 through a captured XHR payload exposed: **every visual-designer save of a definition containing a parallel branch was broken** — not a dagre problem at all.

## Root cause (one missing block, two-layer distance)

`normalizeDefinitionIds` (`workflow-api.ts`) coerces ids to strings at the API boundary (React Flow Node.id must be string). It covered `states` and `transitions` — but not `branches`. Downstream, `handleSave` builds a string-keyed `Map(states → names)`; `branchToInput` resolves branch refs through it with a `?? ref` fallback. Numeric branch refs missed the string-keyed map and the fallback **silently leaked raw ids** into the name-keyed payload (`"branchingStateName": 30015`) → BE deserialization 400.

The killer detail: transitions in the same payload mapped correctly ("Draft") while branches leaked numbers — same map, same lookup pattern. The asymmetry was the clue that the *inputs* differed (normalized vs not), not the lookup.

## Lessons (reusable)

- **A `?? raw` fallback on a map lookup converts a type mismatch into silent garbage-out.** The fallback was designed for "name already a name" but also swallowed "key type wrong". When a lookup CAN'T legitimately miss, prefer loud failure over passthrough.
- **Normalization at an API boundary must cover the whole aggregate.** Partial coercion (states+transitions yes, branches no) created a worse state than none: everything *looked* uniform until the one un-coerced family hit the string-keyed path.
- **"Verified by reading the code" ≠ verified.** Both closed rows had recorded exactly this gap ("FE fork-save round-trip never exercised live"). First live exercise found the bug within minutes.
- **Capture the failing payload, not just the status.** The 400's `errors` object named `$.branches[0].branchingStateName` — wrapping `XMLHttpRequest.send` to capture the request body turned a vague "Lỗi" toast into a one-look diagnosis.
- **Past automation friction is not a law.** The 06-09 session couldn't fire AntD Popconfirm or React-Flow drag via browser MCP; today a direct coordinate `left_click_drag` on the fork diamond just worked (and Popconfirm was avoided entirely by API-cloning… which then 500'd and exposed ANOTHER bug — see below).
- **Self-induced noise:** mid-session the `/definitions/undefined` 404s were my own lost `window` global (page reload cleared it), visible instantly in API logs via `RequestPath`. Check the server's view of the URL before theorizing.

## Second find (filed, not fixed): clone-after-deleted-version 500

Cloning the seeded `demo-parallel-process` 500s permanently. SQL shows the 06-09 broken clone (v2) was soft-deleted but **kept `IsLatest=1`**, leaving seed v1 stuck `IsLatest=0`; re-clone's next-version calc collides with the soft-deleted row on the unfiltered (Name,Version) unique index. Two BE defects (IsLatest handoff on delete-version; soft-delete-blind version calc) → `WF-CLONE-AFTER-DELETED-VERSION-500` (T3 🔴). Worked around by authoring a throwaway fork Draft via the proven e2e API path.

## Verification

- Verify A (dagre): auto-layout → Save ("Đã lưu thiết kế") → GET exact coords (fork 600,72) → reload renders identical. ✅
- Verify B (drag): real drag → Save → reload → float-exact round-trip (718.3167…/2.9818…), `positionMapRef` path distinct from auto-layout's DTO path. ✅
- Fix: 3 new unit tests (all fail pre-fix; regression guard goes through real `branchToInput`) · workflow folder 26 files/236 green · tsc 0 · review APPROVE 0C/0H/0M.
- Cleanup: throwaway def DELETE 204. Stack left UP (5001/5002/5173, Vault re-seeded — dev-mode Vault lost all secrets on container restart, re-seeded via vault-init).

## Carry

- `WF-CLONE-AFTER-DELETED-VERSION-500` (T3 🔴) — needs plan + integ tests; dev-DB seed IsLatest repair rides along.
- `WF-MUTATION-RESPONSES-UNNORMALIZED-IDS` (T4) — create/clone/save-graph responses bypass normalization (review Low).
- 2 live `E2E Parallel` orphan pairs in dev DB (spec cleanup silently failed at some point).
