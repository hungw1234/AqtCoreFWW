# Phase 6 Slice A — Profile Signature Management Tab (Foundation + Read)

**Date**: 2026-06-30 10:45
**Severity**: Low
**Component**: FE Profile / Signature Management
**Status**: Resolved

## What Happened

Shipped Phase 6 Slice A: self-service signature CRUD + enrollment read-only tab in user profile page. Reconciled flat profile-page.tsx → antd Tabs pattern (mirroring admin-page.tsx). New tab "Chữ ký số" displays enrolled signatures (DisplayName/set-default) + approval-status badges from GET `/api/v1/me/signature-overview`.

## The Brutal Truth

Scout-first re-verify of 6-day-old plan caught 6 contract/path drifts BEFORE coding. Plan assumed Slice A was simple reuse; reality: greenfield FE, JSON STRING placement serialization, CCCD self-service gap blocks Slice B Remote entirely. Dodged a scope-hole that would have surfaced mid-Slice-B.

## Technical Details

**New files:** web/src/features/profile/signatures/ — signature-types.ts, signatures-api.ts, enrollment-list.tsx, signature-profile-form-modal.tsx, signature-management-tab.tsx + 3 test files (8 tests total).

**Drifts discovered:**
1. signing-prompt.tsx lives in digital-signature/, not forms/builder/
2. Both profile-page + signature-config-page FLAT (no Tabs structure)
3. Zero existing enrollment FE — greenfield implementation required
4. Remote enroll reads user.SigningNationalId; NO self-service endpoint to update CCCD
5. Placement is JSON STRING (not nested object), enums serialize STRING names (JsonStringEnumConverter)

**Test gates:** vitest +8 new, full 1225/1225 pass. Typecheck + eslint clean. Code-reviewer: APPROVE_WITH_NITS (0 Crit/High/Med; 2 nits fixed: query-key hierarchy, info-tab centering restored).

## Lessons Learned

Scout-first re-verify of stale plans catches contract drift PRE-CODE. Slice-B blocker (CCCD self-service setter missing on BE) surfaced as design-gate for Slice D before proceeding. P6 split into 4 independently-shippable slices; user selected A.

## Next Steps

P6 Slices B (USB enroll), C (visible-stamp placement UI), D (CCCD setter + Remote gate) pending scope review. Slice D design must resolve BE self-service CCCD endpoint before Slice B cook proceeds.
