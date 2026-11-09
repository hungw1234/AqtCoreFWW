# Frontend Code Standards

React + TypeScript conventions for `web/`. Split out of [`code-standards.md`](./code-standards.md)
on 2026-07-28 — that file had grown to 2361 lines and these sections form the one clean boundary in
it (frontend/UI standards vs backend). Content is unchanged.

`code-standards.md` remains the entry point and covers everything backend: CQRS, thin controllers,
domain entities, repositories, error handling, the API response envelope, security, cross-module data
access, encryption, caching, multi-tenancy, testing conventions, and the migration folder convention.

> Section titles here are unchanged, so existing references of the form
> `code-standards.md § "<title>"` still name the right section — only the file differs. Nothing in the
> repo references these by number.

---

## Frontend Local Typecheck Practice (v2.137p57, 2026-05-17)

**Always use `npm run typecheck`, never `npx tsc`** for local validation.

### Rule
```bash
# CORRECT — project-scoped, picks up tsconfig.app.json
cd web && npm run typecheck

# WRONG — naked tsc walks default config, misses project-scoped settings
cd web && npx tsc --noEmit
```

### Why
`npx tsc` (no `-p` flag) walks the default `tsconfig.json` resolution which differs from project-scoped configs:
- Skips `paths` aliases declared in `tsconfig.app.json`
- Misses test-only types from `tsconfig.test.json`
- May include `node_modules` types that the app config excludes

### Incident (v2.137p57)
BP form-map picker shipped with DTO field rename (`templateId` → `formTemplateVersionId`). Local `npx tsc` passed (didn't traverse the actual app config); CI caught it via project-scoped check. Wasted ~20min on regression hunt before realizing the typecheck command itself was the gap.

### CI gate (already enforced)
`.github/workflows/ci-frontend.yml` runs both project configs explicitly (split from `ci.yml` on 2026-11-03 so web-only changes do not start the .NET jobs):
```yaml
- name: Type check
  run: npx tsc --noEmit -p tsconfig.app.json
- name: Type check (tests)
  run: npx tsc --noEmit -p tsconfig.test.json
```

CI is safe; local discipline is the gap. Always `npm run typecheck` before pushing.

---

## Frontend Error Handling Patterns (V2)

### Error Boundary Component
Wrap lazy-loaded page routes with error boundary to catch render errors:

```jsx
// Layout routes
<ErrorBoundary fallback={<ErrorPage type="500" />}>
  <Suspense fallback={<Skeleton />}>
    <WorkflowInstancesPage />
  </Suspense>
</ErrorBoundary>
```

### Session Timeout Warning
Display toast 2 minutes before token expires (AuthProvider monitors expiry):

```jsx
// In AuthProvider context
useEffect(() => {
  const checkTimeout = () => {
    const expiresIn = expiresAt - Date.now();
    if (expiresIn < 120000) {  // 2 min = 120s
      notification.warning({
        message: 'Session Expiring',
        description: 'Your session will expire in 2 minutes. Please save your work.',
        duration: 0,
      });
    }
  };
  const interval = setInterval(checkTimeout, 10000);
  return () => clearInterval(interval);
}, [expiresAt]);
```

### Notification Toast (Action Feedback)
Use Ant Design notification for CRUD operations:

```jsx
const handleCaseApprove = async (caseId) => {
  try {
    await approveCaseMutation.mutateAsync(caseId);
    notification.success({
      message: 'Case Approved',
      description: `Case ${caseId} approved successfully.`,
    });
  } catch (error) {
    notification.error({
      message: 'Approval Failed',
      description: error.response?.data?.errors?.[0]?.detail_vi || 'Unknown error',
    });
  }
};
```

### Rate Limit Response (429 Handler)
Axios interceptor catches rate limit and displays retry countdown:

```javascript
// In api/client.ts
response.interceptors.response.use(null, (error) => {
  if (error.response?.status === 429) {
    const retryAfter = parseInt(error.response.headers['retry-after'] || '60');
    notification.error({
      message: 'Too Many Requests',
      description: `Please try again in ${retryAfter} seconds.`,
      duration: retryAfter,
    });
    // Exponential backoff retry
    return delayedRetry(error.config, retryAfter);
  }
  return Promise.reject(error);
});
```

---

## Frontend Polling Guidelines (v2.133p16)

Hooks dùng `refetchInterval` (TanStack Query) PHẢI tuân 4 rules dưới. Code-reviewer reject PR thiếu bất kỳ rule nào. Pattern này chặn ~85% request storm khi BE rớt — bằng chứng v2.133p16 (PR #68): `query-config.ts` retry guard + `notification-signalr.ts` circuit breaker + `enabled` gate cho 8 polling hooks.

### Rule 1 — `enabled` gate (mandatory)

Mọi polling hook PHẢI có `enabled` gate. Tối thiểu là `isAuthenticated`; thêm điều kiện cụ thể nếu cần (e.g., page visible, feature flag).

```ts
// ✅ CORRECT — useNotifications poller
const { isAuthenticated } = useAuth();
useQuery({
  queryKey: ['notifications', 'unread-count'],
  queryFn: fetchUnreadCount,
  refetchInterval: 30_000,
  enabled: isAuthenticated,  // ← required
});

// ❌ WRONG — fires before login → 401 storm
useQuery({
  queryKey: ['notifications', 'unread-count'],
  queryFn: fetchUnreadCount,
  refetchInterval: 30_000,
});
```

**Rationale:** BE 401 không tự động unmount component (auth race tại app boot). Không có `enabled`, hook spam request trước khi auth resolve → 5+ failed requests trong 1s. `query-config.ts` retry guard skip 4xx, nhưng request đã fire vẫn tốn round-trip.

### Rule 2 — `staleTime ≥ refetchInterval / 2`

Default `staleTime: 30_000` (`query-config.ts:56`). Nếu hook override `staleTime`, phải đảm bảo ≥ một-nửa `refetchInterval` để tránh refetch ngay sau mount khi data còn fresh.

```ts
// ✅ CORRECT
useQuery({ refetchInterval: 60_000, staleTime: 30_000 });   // 30 ≥ 60/2
useQuery({ refetchInterval: 30_000 /* staleTime mặc định 30_000 */ }); // 30 ≥ 30/2

// ❌ WRONG — double-fire on mount
useQuery({ refetchInterval: 60_000, staleTime: 5_000 });    // 5 < 60/2
```

### Rule 3 — `refetchIntervalInBackground: false` (default; KHÔNG override)

TanStack mặc định `false` — không poll khi tab background. KHÔNG bao giờ override `true` trừ khi có Product requirement explicit (e.g., compliance dashboard cần update cả khi tab ẩn). Nếu cần, document trong PR description + thêm comment `// Reason: ...` ngay trên option.

```ts
// ❌ WRONG — drains battery + spams BE khi user mở 10 tabs
useQuery({ refetchInterval: 30_000, refetchIntervalInBackground: true });
```

### Rule 4 — ≤1 poller per page baseline

Mỗi page chỉ nên có 1 polling hook. Nếu cần ≥3 endpoints poll cùng cadence → CHỌN MỘT:

- **(a) `useQueries` gộp cùng INTERVAL** (preferred khi BE chưa có aggregate). Pattern: `web/src/features/admin-dashboard/admin-dashboard-api.ts:35-82` (`useAdminDashboardStats` poll 4 endpoints song song, 60s interval).
- **(b) BE aggregate endpoint** (preferred khi >5 endpoints HOẶC sub-queries có dependency). Backend tạo `GET /api/v1/{module}/dashboard/aggregate` trả 1 object combined; FE thay `useQueries` 5 → `useQuery` 1. Trigger: prod usage ≥50 active tabs OR p95 latency >2s. Reference: backlog `POLL-FU2`.

```ts
// ✅ CORRECT — useQueries gộp cùng INTERVAL
const INTERVAL = 60_000;
const { isAuthenticated } = useAuth();
const results = useQueries({
  queries: [
    { queryKey: ['admin-dashboard', 'health'], queryFn: ..., refetchInterval: INTERVAL, enabled: isAuthenticated },
    { queryKey: ['admin-dashboard', 'users'],  queryFn: ..., refetchInterval: INTERVAL, enabled: isAuthenticated },
    // ... thêm sub-query khi cần
  ],
});
```

### Defense-in-Depth (đã ship — KHÔNG cần config thêm)

Polling hooks tự động hưởng các guard sau từ singleton `queryClient` (`web/src/core/api/query-config.ts`):

- `shouldRetryQuery`: skip retry khi `status=0` (network) hoặc `4xx` (client-side); chỉ retry `5xx` 1 lần.
- `queryRetryDelay`: exponential backoff cap 8s (tránh tight loop khi BE đang phục hồi).
- SignalR realtime channel (`notification-signalr.ts`): circuit breaker pause 5 phút sau 5 fail liên tiếp + skip-start-when-no-user.

Hook authors KHÔNG cần re-implement retry/backoff. Nếu thấy hook custom retry policy, CR phải hỏi tại sao.

### Code-Review Checklist

Khi review PR thêm/sửa polling hook:

- [ ] Có `enabled` gate (Rule 1)
- [ ] `staleTime ≥ refetchInterval / 2` (Rule 2)
- [ ] KHÔNG có `refetchIntervalInBackground: true` (Rule 3)
- [ ] Page có ≤1 poller HOẶC dùng `useQueries`/aggregate (Rule 4)
- [ ] KHÔNG override `retry`/`retryDelay` (defense-in-depth)

---

## Frontend React Performance & Memoization (2026-03-21)

### React.memo Guidelines
Use `React.memo` to prevent unnecessary re-renders of child components:

```tsx
// Memoize pure components that receive stable props
const CaseStatusTag = React.memo(({ status, priority }: CaseTagProps) => {
  return <Tag color={statusColorMap[status]}>{status}</Tag>;
});

CaseStatusTag.displayName = 'CaseStatusTag';
```

**When to apply:**
- Child components that depend on non-primitive props (objects, arrays, functions)
- Table cells, list items with stable data
- Components passed as children (prevent re-render on parent update)

**When NOT to apply:**
- Primitive props (string, number, boolean) — memoization overhead > benefit
- Top-level page components
- Components with frequently-changing props

### useMemo Hook
Cache computed values to avoid recalculation on every render:

```tsx
const filteredItems = useMemo(
  () => items.filter(item => item.status === selectedStatus),
  [items, selectedStatus]
);
```

Use when computation is expensive (array filter, sort, reduce).

### i18n Label Pattern
Always use translation keys for user-facing strings:

```tsx
// ✓ Correct
<Label>{t('audit.filterByAction')}</Label>

// ✗ Incorrect
<Label>Filter by Action</Label>
```

Keep translation files in `web/src/core/i18n/locales/{vi,en}.json` (sync both languages simultaneously).

---

## Backend Dark Mode & UI Features (V2)

### Dark Mode CSS Variables
Theme colors defined as CSS custom properties, switched via Zustand store:

```css
/* light theme (default) */
:root {
  --color-primary: #1B3A5C;      /* Navy */
  --color-error: #C8102E;         /* Red */
  --color-bg: #FFFFFF;
  --color-text: #000000;
}

/* dark theme */
[data-theme="dark"] {
  --color-primary: #2C5AA0;       /* Light navy */
  --color-error: #E63946;         /* Light red */
  --color-bg: #121212;
  --color-text: #FFFFFF;
}
```

Toggle implementation in ThemeProvider:

```jsx
const themeStore = useThemeStore();
const [isDark, setIsDark] = useState(themeStore.isDark);

const toggleTheme = () => {
  setIsDark(!isDark);
  themeStore.setTheme(!isDark);
  localStorage.setItem('theme', isDark ? 'light' : 'dark');
  document.documentElement.setAttribute('data-theme', isDark ? 'light' : 'dark');
};
```

### Health Check Dashboard
Admin endpoint `/admin/health` returns status of all infrastructure components:

```json
{
  "status": "HEALTHY",
  "components": {
    "api": {"status": "OK", "responseTime": "45ms"},
    "database": {"status": "OK", "openConnections": 12},
    "redis": {"status": "OK", "memoryUsage": "256MB"},
    "rabbitmq": {"status": "OK", "queueCount": 8},
    "minio": {"status": "OK", "buckets": 3}
  },
  "timestamp": "2026-03-19T10:00:00Z"
}
```

### Form Field Reorder
PATCH endpoint to update field display order:

```
PATCH /api/v1/form-templates/{templateId}/fields/{fieldId}/order
Content-Type: application/json

{
  "newPosition": 3
}
```

Response: Returns updated FormTemplateDto with reordered fields array.

---

## Batch Operations (V2)

### Batch Workflow Transition
Checkbox selection on workflow instances table allows bulk actions:

```jsx
// Workflow instances page with checkbox column
const [selectedInstanceIds, setSelectedInstanceIds] = useState([]);

const handleBatchApprove = async () => {
  await batchTransitionMutation.mutateAsync({
    instanceIds: selectedInstanceIds,
    action: 'APPROVE'
  });
  notification.success({ message: `${selectedInstanceIds.length} items approved.` });
  setSelectedInstanceIds([]);
};
```

Endpoint: `POST /api/v1/workflow/instances/batch/transition`

```json
{
  "instanceIds": ["uuid1", "uuid2", "uuid3"],
  "action": "APPROVE",
  "reason": "Bulk approval per QĐ123"
}
```

---

## E2E Test Fixture Patterns — test-base.ts Auth-Helper 401-Retry (Phase-07 TIER-D-TOKEN-REVOKE, 2026-05-09)

E2E specs MUST use `./fixtures/test-base` instead of raw Playwright `@playwright/test`. The test-base fixture wraps the `request` object with a Proxy that transparently intercepts 401 responses, invalidates cached auth-helper tokens, refreshes via ROPC, and retries the original request once.

### Problem Addressed

Mid-suite token revocation (introspection returning `active: false` despite wall-clock validity) cascades 401 errors across downstream specs. Root cause (reference-token pathway) latent; mitigation required transparent retry logic without touching spec code.

### Usage Pattern

```typescript
// ✅ CORRECT: Import test-base (includes Proxy-wrapped request)
import { test, expect } from './fixtures/test-base';

test('spec-name', async ({ page, apiContext, auth, request }) => {
  // request is Proxy-wrapped; 401 auto-retry on first failure
  const response = await request.get('/api/v1/protected-resource');
  expect(response.status()).toBe(200);
});

// ❌ WRONG: Raw @playwright/test — lacks 401-retry infrastructure
import { test } from '@playwright/test';  // ← NO
```

### Auth-Helper Enhancement Methods

New helper methods for advanced scenarios (not needed in standard happy-path specs):

```typescript
// Force a fresh token outside beforeAll lifecycle
await auth.forceFresh({ role: 'admin' });

// Invalidate a specific cached token (simulates mid-run revocation)
await auth.invalidateToken(currentToken);

// Get role name for a token string (debugging, logging)
const role = auth.getRoleForToken(token);

// Map token string → role name (e.g., for fixture debug output)
const roleMap = auth.tokenToRole; // Map<string, string>
```

### Implementation Details

- **Proxy intercept location:** `web/e2e/fixtures/test-base.ts:50` (Proxy `get` trap; file 117 LOC)
- **Retries:** One retry on 401. If refresh also 401, propagates error upstream.
- **Multipart streams:** Skipped (streaming responses detected and bypass intercept per code-review safety gate)
- **Fallback:** If refresh fails for any reason, original 401 propagates; test fails with full context

### When to Extend test-base

If a spec uses `apiContext` or `testData` (which create separate `request` contexts via `playwright.request.newContext()`), those bypass the Proxy. Document as P3 follow-up PHASE7-TIER-D-TOKEN-REVOKE-FU2 if future 401 cascades surface in those 6 specs.

---

## Announcement Banner (V2)

Dashboard displays configurable announcement banner fetched from API:

```
GET /api/v1/announcements/active

{
  "id": "banner-001",
  "content": "System maintenance scheduled for 2026-03-20.",
  "type": "WARNING",
  "expiresAt": "2026-03-20T23:59:59Z"
}
```

Frontend renders via Ant Design Alert component, dismissible with localStorage persistence.
