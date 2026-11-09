# Hide RTBF Admin Menu Temporarily

**Date**: 2026-04-22 15:47 Asia/Saigon  
**Version**: v2.127p4  
**Severity**: Low  
**Component**: Web UI / Admin Menu  
**Status**: Resolved  

## What Happened

User requested to hide the RTBF (Right To Be Forgotten) admin menu item temporarily while the feature remains in development. RTBF is compliance-critical (Luật 91/2025 Art. 9 + PDPL Art. 17): multi-module PII anonymization SLA 30 days, status machine Pending→Processing→Completed|Rejected. Route `/admin/rtbf` exists in both FE and BE but was hidden from UI.

## The Brutal Truth

This is a "tạm ẩn" (temporary hide) — UI visibility only. The route, controller, permissions, and i18n keys all remain live in codebase. If someone navigates directly to `/admin/rtbf` by URL, the page loads fine. Menu item just doesn't appear. Re-enabling is two comment-line uncomments. Low-friction, low-risk change.

## Technical Details

**File edited**: `web/src/layouts/admin-menu-entries.ts`

- Line 26: Commented `DeleteOutlined` import (paired with menu item deletion to avoid unused-import warning)
- Line 152: Commented menu object `{ key: '/admin/rtbf', icon: <DeleteOutlined />, label: 'RTBF', ... }`
- Line 90: **Left untouched** — `SYSTEM_PATHS.push('/admin/rtbf')` remains. This preserves AdminLayout classification; direct URL navigation still works.

**Verification**: `npx tsc --noEmit -p tsconfig.app.json` → exit 0. No type errors.

## Why This Approach

Two imports point to same icon (DeleteOutlined). Commenting menu item alone triggers TS strict unused-import warning. Cross-referenced both with inline marker "tạm ẩn" for future re-enable search. Dependency-map left unchanged — zero entity/endpoint/module impact.

## Next Steps

- Uncomment 2 lines in `admin-menu-entries.ts` when RTBF ready for user release
- Phase-01 Outbox Dispatcher (v2.128) OR Integration Hub P1 — user's call
- Docs bumped: changelog + roadmap footer + memory/project-state + memory/MEMORY.md

---

**Status**: DONE  
**Summary**: Journal file written at `C:/AqtCoreFW/docs/journals/journal-260422-1547-hide-rtbf-admin-menu.md`  
**Concerns**: None — pure UI hide, zero impact on feature completion or data layer.
