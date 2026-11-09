# Phase 01 — Remove Redundant Token-Expiry Poll

## Context Links
- Parent plan: [plan.md](./plan.md)
- Source files:
  - `web/src/features/auth/auth-provider.tsx` (lines 82-95 poll block)
  - `web/src/features/auth/session-expiry-modal.tsx` (union + ternary)
  - `web/src/core/i18n/locales/vi.json:113`
  - `web/src/core/i18n/locales/en.json:113`
- OIDC config: `web/src/features/auth/auth-config.ts` (`automaticSilentRenew`, `accessTokenExpiringNotificationTimeInSeconds: 180`)

## Overview
- **Priority:** P1 (blocks Phase B — shared file `auth-provider.tsx`)
- **Status:** pending
- **Brief:** Remove 60s poll that races `automaticSilentRenew` → clean slate 2-reason modal

## Key Insights (from scout)
- `oidc-client-ts` already fires `accessTokenExpiring` 180s before expiry → silent renew triggers → `userLoaded` event → user state refreshed transparently
- Poll at `WARN_THRESHOLD_S=60` fires AFTER silent renew should have run — if renew succeeded token is new so poll never fires; if renew failed `silentRenewError` already shows modal. Poll is dead code catching zombie tokens only.
- Poll is root cause of spurious popup during active work — legacy from pre-silent-renew architecture
- `tokenDesc` i18n key ONLY used by token-expiry branch — safe to delete

## Requirements
### Functional
- Remove poll `useEffect` (lines 82-95) from `auth-provider.tsx`
- Narrow `SessionExpiryReason` type: `'idle' | 'token-expiry' | 'renew-failed'` → `'idle' | 'renew-failed'`
- Collapse ternary in `session-expiry-modal.tsx` description (lines 58-62) — only idle branch remains for non-renew-failed
- Delete `auth.session.tokenDesc` from `vi.json` + `en.json`

### Non-functional
- No behavior change for idle-warning + silent-renew-failure paths
- No new deps, no new files

## Architecture — Event Flow (after change)

```
oidc-client-ts timer (180s before expiry)
  └─> accessTokenExpiring event
      └─> automaticSilentRenew fires
          ├─ SUCCESS → userLoaded event → setUser(newUser)  [invisible]
          └─ FAIL    → silentRenewError event → showSessionModal('renew-failed')

use-idle-timeout (30 min no activity)
  └─> onWarning → showSessionModal('idle') + countdown
```

No third reason. Poll removed.

## Related Code Files
### Modify
- `web/src/features/auth/auth-provider.tsx` — delete useEffect lines 82-95
- `web/src/features/auth/session-expiry-modal.tsx` — narrow union + collapse ternary
- `web/src/core/i18n/locales/vi.json` — remove `tokenDesc` key
- `web/src/core/i18n/locales/en.json` — remove `tokenDesc` key

### Create
- none

### Delete
- none (only key deletions within JSON files)

## Implementation Steps
1. `auth-provider.tsx`:
   - Delete entire useEffect block `lines 82-95` (poll + `WARN_THRESHOLD_S`)
   - Remove comment header "--- Token expiry poll → show modal instead of notification ---"
2. `session-expiry-modal.tsx`:
   - Change line 12: `export type SessionExpiryReason = 'idle' | 'renew-failed';`
   - Replace lines 58-62 description ternary with:
     ```ts
     const description = isRenewFailed
       ? t('auth.session.expiredDesc', 'Không thể gia hạn phiên. Vui lòng đăng nhập lại.')
       : t('auth.session.idleDesc', 'Phiên làm việc sẽ tự động đăng xuất sau {{seconds}} giây do không có thao tác.', { seconds: remaining });
     ```
3. `vi.json` line 113: delete `"tokenDesc": "Phiên làm việc sẽ hết hạn sau {{seconds}} giây.",`
4. `en.json` line 113: delete `"tokenDesc": "Your session will expire in {{seconds}} seconds.",`
5. Run `yarn typecheck` — ensure no `'token-expiry'` reference remains
6. Run `yarn lint` — ensure no unused var warnings

## Todo List
- [ ] Delete poll useEffect in `auth-provider.tsx`
- [ ] Narrow `SessionExpiryReason` union in `session-expiry-modal.tsx`
- [ ] Collapse description ternary in `session-expiry-modal.tsx`
- [ ] Delete `tokenDesc` key from `vi.json`
- [ ] Delete `tokenDesc` key from `en.json`
- [ ] `yarn typecheck` pass
- [ ] `yarn lint` pass
- [ ] Grep verify: `grep -r "token-expiry\|tokenDesc" web/src` returns zero matches

## Success Criteria
- Grep for `'token-expiry'` + `tokenDesc` in `web/src` → zero matches
- `yarn typecheck` + `yarn lint` pass
- Manual smoke: login → idle 5 min while typing — NO popup
- Manual: trigger silent renew failure (network off mid-session) → modal with "Đăng nhập lại"

## Risk Assessment
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Hidden caller of `'token-expiry'` reason outside scanned files | Low | Med | Full-repo grep before commit |
| i18n key referenced in test snapshots | Low | Low | Snapshot refresh `yarn test -u` |
| Users on stale browser bundle see old poll | Low | Low | Normal deploy cache invalidation handles |

## Security Considerations
- No new attack surface. Removal of poll does NOT reduce security — silent renew + idle timeout cover both expiry axes.

## Next Steps
- Blocks: Phase B (api-client 401 retry) — shares `auth-provider.tsx`
- On completion: commit `refactor(auth): remove redundant token-expiry poll`
