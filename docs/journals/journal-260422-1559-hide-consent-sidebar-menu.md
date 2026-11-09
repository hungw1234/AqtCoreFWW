# Hide Consent Sidebar Menu (v2.127p5)

**Date**: 2026-04-22 15:59  
**Version**: v2.127p5  
**Scope**: UI chore — temporary menu hide (sister task to journal-260422-1547-hide-rtbf-admin-menu.md)  
**Status**: DONE

## What

- Scouted `/consent` route → user-facing PDPL consent self-service page (Luật 91/2025 Art. 11)
- Hid sidebar menu entry: commented `SolutionOutlined` import (line 17) + menu item (line 69) in `web/src/layouts/sidebar-menu.tsx`
- Verified TypeScript compile (`tsc --noEmit`) → exit 0

## Why

User requested "tạm ẩn menu này" — feature not yet ready for user release, same pattern as v2.127p4 RTBF admin menu.

## How

Two-comment-lines pattern (established v2.127p4 convention):
```typescript
// tạm ẩn: Luật 91/2025 Art. 11 consent management — user-facing, not yet ready
// { key: '/consent', label: t('nav.consent'), icon: <SolutionOutlined /> }
```

## Docs Updated

- `docs/project-changelog.md` → v2.127p5 entry + parent task link
- `docs/project-roadmap.md` → footer "Last updated v2.127p5"
- `memory/project-state.md` → version bump, sister task reference
- `memory/MEMORY.md` → index row updated

## Lessons

Pattern "comment icon + menu item + 'tạm ẩn' marker" now confirmed codebase convention across two files (`admin-menu-entries.ts`, `sidebar-menu.tsx`). Single grep auditable: `grep "tạm ẩn" web/src/layouts/` lists all hidden menus. Consider extracting into shared helper if pattern recurs 3+ times.

## Next Steps

User chooses: Phase-01 Outbox Dispatcher (v2.128) OR Integration Hub P1.
