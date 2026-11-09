# Phase 02 — API Client 401 Defense-in-Depth Retry

## Context Links
- Parent plan: [plan.md](./plan.md)
- Prev phase: [phase-01](./phase-01-remove-token-expiry-poll.md) (MUST complete first)
- Source files:
  - `web/src/core/api/api-client.ts` (response interceptor lines 61-66)
  - `web/src/features/auth/auth-provider.tsx` (userManager export line 163)
  - OIDC: `web/src/features/auth/auth-config.ts`

## Overview
- **Priority:** P1
- **Status:** pending
- **Brief:** Replace hard `signinRedirect()` on 401 with one-shot `signinSilent()` retry + surface modal on fail. Cross-module dispatch via module-singleton callback registry (no CustomEvent, no new deps).

## Key Insights (from scout)
- Current behavior: any 401 → immediate `signinRedirect()` → user loses app state (form data, router position)
- Silent renew scheduled 180s before expiry — but clock drift, sleep/wake laptop, network blip can land token in 401 window
- `api-client.ts` imports `userManager` from `auth-provider.tsx` (already cross-module wired) — new file just needs same pattern
- No mocking lib installed (package.json scan: no msw, no axios-mock-adapter) — use vitest `vi.mock` + inline axios adapter mock

## Requirements
### Functional
1. On 401 response: if `config._retried !== true`, attempt `userManager.signinSilent()`:
   - **Success:** replay original request with fresh token (update Authorization header from new user)
   - **Fail:** call `triggerSessionError()` → surfaces modal (reason=`'renew-failed'`) → reject normalized error
2. On 401 with `config._retried === true`: do NOT retry again (infinite loop guard) → reject normalized + trigger error
3. Parallel 401s while renew in flight: share single `signinSilent()` promise (no thundering herd)
4. `auth-provider.tsx` registers handler on mount via `setSessionErrorHandler(() => showSessionModal('renew-failed'))`; deregister on unmount
5. Remove old `isRedirectingToLogin` flag + auto-redirect — superseded

### Non-functional
- No new runtime deps
- Test coverage: 4 scenarios (see Test Spec below)
- Behavior preserved: 403 + `X-Password-Expired` unchanged; other statuses unchanged

## Architecture — Data Flow

```
Caller (apiClient.get/post)
   │
   ▼
[Request Interceptor] attach Bearer + CorrelationId
   │
   ▼
 Backend ──401──> [Response Interceptor — 401 branch]
                       │
                       ├─ config._retried? ──YES──> trigger error + reject
                       │
                       NO
                       ▼
                   renewInFlight ??= userManager.signinSilent()
                       │
                       ├── SUCCESS ──> config._retried=true
                       │               config.headers.Authorization = 'Bearer ' + newUser.access_token
                       │               return apiClient(config)      [REPLAY]
                       │
                       └── FAIL    ──> triggerSessionError()
                                       └─> AuthProvider handler fires
                                            └─> showSessionModal('renew-failed')
                                       reject normalized error
```

Cross-module callback wiring:
```
session-error-handler.ts (singleton module scope)
   │ export setSessionErrorHandler(fn)
   │ export triggerSessionError()
   │
   ├──[consumer]── api-client.ts calls triggerSessionError()
   │
   └──[provider]── auth-provider.tsx calls setSessionErrorHandler(fn) on mount
```

## Related Code Files
### Create
- `web/src/features/auth/session-error-handler.ts` (~15 LOC singleton registry)
- `web/src/core/api/api-client.test.ts` (unit tests)

### Modify
- `web/src/features/auth/auth-provider.tsx` — register/unregister handler in useEffect (alongside OIDC event wiring)
- `web/src/core/api/api-client.ts` — rewrite 401 branch (retry + registry dispatch); remove `isRedirectingToLogin` flag

### Delete
- none

## Implementation Steps

### Step 1 — Create `session-error-handler.ts`
```ts
// web/src/features/auth/session-error-handler.ts
// Module singleton: decouples api-client (consumer) from auth-provider (provider)
// of session-error UX without DOM events or extra deps.

type SessionErrorHandler = () => void;

let handler: SessionErrorHandler | null = null;

export function setSessionErrorHandler(fn: SessionErrorHandler | null): void {
  handler = fn;
}

export function triggerSessionError(): void {
  handler?.();
}
```

### Step 2 — Wire into `auth-provider.tsx`
Inside existing OIDC events useEffect (around lines 49-79), add registration:
```ts
setSessionErrorHandler(() => showSessionModal('renew-failed'));

return () => {
  // ... existing cleanup
  setSessionErrorHandler(null);
};
```
Add import at top: `import { setSessionErrorHandler } from './session-error-handler';`

### Step 3 — Rewrite 401 branch in `api-client.ts`
- Add to imports: `import { triggerSessionError } from '@/features/auth/session-error-handler';`
- Delete module-scope `isRedirectingToLogin` flag (lines 10-12)
- Add module-scope: `let renewInFlight: Promise<void> | null = null;`
- Replace 401 block (lines 61-66):

```ts
if (status === 401) {
  const config = error.config as InternalAxiosRequestConfig & { _retried?: boolean };
  if (config && !config._retried) {
    config._retried = true;
    try {
      renewInFlight ??= userManager.signinSilent().then(() => undefined);
      await renewInFlight;
      const fresh = await userManager.getUser();
      if (fresh?.access_token && !fresh.expired) {
        config.headers.Authorization = `Bearer ${fresh.access_token}`;
        return apiClient(config);  // replay
      }
      // renew returned but no valid user — fall through to error path
    } catch {
      // signinSilent rejected — fall through
    } finally {
      renewInFlight = null;
    }
    triggerSessionError();
  } else {
    triggerSessionError();
  }
}
```
Note: wrap the outer response interceptor error callback in `async` so `await` works.

### Step 4 — Create `api-client.test.ts`
Mock strategy: vitest `vi.mock('@/features/auth/auth-provider', ...)` to stub `userManager`; inline axios mock adapter via `apiClient.defaults.adapter`. Mock `triggerSessionError`.

**Test matrix:**

| # | Scenario | Setup | Assert |
|---|----------|-------|--------|
| T1 | 401 → silent OK → replay OK | 1st adapter call 401; `signinSilent` resolves; 2nd adapter call 200 | caller gets data; `triggerSessionError` NOT called |
| T2 | 401 → silent FAIL | adapter 401; `signinSilent` rejects | caller error rejects; `triggerSessionError` called once |
| T3 | No infinite loop | adapter returns 401 for BOTH 1st and replay | `signinSilent` called exactly once; `triggerSessionError` called once; error rejects |
| T4 | Parallel 401s coalesce | 2 concurrent requests both 401; single `signinSilent` mock | `signinSilent` mock invoked exactly once; both retries share result |

Sketch:
```ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import MockAdapter from 'axios-mock-adapter';  // if unavailable: manual adapter stub

vi.mock('@/features/auth/auth-provider', () => ({
  userManager: {
    getUser: vi.fn(),
    signinSilent: vi.fn(),
  },
}));
vi.mock('@/features/auth/session-error-handler', () => ({
  triggerSessionError: vi.fn(),
}));

// ... tests
```
**Fallback if `axios-mock-adapter` not installable:** replace `apiClient.defaults.adapter` with a vi.fn returning `Promise<AxiosResponse>`.

### Step 5 — Validate
1. `yarn typecheck`
2. `yarn test src/core/api/api-client.test.ts`
3. Full `yarn test` + `yarn lint`
4. Manual: set access token TTL to 30s in dev, make API calls crossing expiry → should retry silently; no visible redirect

## Todo List
- [ ] Create `session-error-handler.ts`
- [ ] Wire handler registration in `auth-provider.tsx`
- [ ] Rewrite 401 branch in `api-client.ts`
- [ ] Remove `isRedirectingToLogin` flag
- [ ] Add `renewInFlight` singleton promise coalescing
- [ ] Create `api-client.test.ts` with 4 scenarios (T1-T4)
- [ ] `yarn typecheck` pass
- [ ] `yarn test` pass (new tests + regressions)
- [ ] `yarn lint` pass
- [ ] Manual verify: no redirect on 401 when renew succeeds

## Success Criteria
- All 4 test scenarios green
- `triggerSessionError` called only when renew truly fails
- `signinSilent` called at most once per burst of parallel 401s
- Zero residual references to `isRedirectingToLogin`

## Risk Assessment
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Infinite retry loop | Med | High | `config._retried` flag + test T3 |
| Thundering herd `signinSilent` | Med | Med | `renewInFlight` promise coalesce + test T4 |
| Replay fails (token still bad) | Low | Med | Fall through to `triggerSessionError` — user sees modal |
| Test mock library missing | Med | Low | Use `apiClient.defaults.adapter = vi.fn(...)` inline — no extra deps |
| Replay loses original request body | Low | High | Axios `config` object retains data/params — replay is safe |
| `auth-provider` unmount during renew leaves dangling handler ref | Low | Low | `setSessionErrorHandler(null)` on unmount |

## Security Considerations
- **Infinite loop guard**: `_retried` flag is per-config mutation — cannot be bypassed since axios retry creates new promise chain but reuses config object. Confirm with test T3.
- **Token leak**: Fresh token attached via `config.headers.Authorization` — overwrites stale Bearer. Replay uses new token.
- **Same-origin only**: `apiClient` fixed to `/api/v1` base — no risk of token leak to foreign host during replay.
- **Silent renew over iframe**: existing OIDC config uses iframe + PKCE — no change; inherits same security.
- **Failed renew not retried**: any 401 with `_retried=true` short-circuits — prevents attacker flooding 401s to force infinite signinSilent calls.

## Next Steps
- Blocked by: Phase A (shared file `auth-provider.tsx`)
- Blocks: final regression test + v2.133 ship
- On completion: commit `feat(auth): add 401 silent-renew retry with fail-open modal`
