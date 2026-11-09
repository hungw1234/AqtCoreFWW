# T3-05: FE Smoke-Phase5 Test Failures — Data Hygiene Debt

**Date**: 2026-04-13 14:30  
**Severity**: Medium  
**Component**: FE Test Suite, Localization, ChatPage, JobMonitorPage  
**Status**: Resolved  

## What Happened

Backlog claimed 10 failing tests in phase5. Actual count: 4. Real cost of sloppy issue tracking — time spent verifying phantom failures instead of fixing root causes.

## Root Causes

1. **ChatPage mock shape mismatch**: `useConversations` test double returned flat query shape; component hook is `useInfiniteQuery`. Test called `data?.pages.flat()` which threw in test context (mock object has no `pages`).

2. **JobMonitorPage stale locale keys**: Component refactored 2026-04-04 (commit 8762365f) to use `jobs.tab.all` + `jobs.tab.failed`. Test still asserted obsolete key `jobs.tab.recurring`. Locale file never updated. Production code didn't crash because i18n `defaultValue` fallback masked the gap.

## The Brutal Truth

Test suite diverged from component intent silently. Backlog's inflated count wasted cycles. Worse: `defaultValue` in i18n fooled us into thinking coverage was intact — it just hid locale debt from both humans and CI.

## Lessons Learned

- **Backlog hygiene**: Claimed failures ≠ actual failures. Verify count before prioritizing.
- **The defaultValue trap**: Fallback values hide missing locale keys. They're for UX safety, not test coverage. When component refactors endpoints/keys, locale updates aren't optional.
- **Test doubles must match intent**: Mock shape must match hook signature, not just field names.

## Fix Summary

4 edits / 3 files. Added mock `pages` array wrapper. Updated locale keys to match post-refactor component intent. 29/29 tests pass.

**Commit**: 34ed8ba8 (main)

## Next Steps

- Add locale key validation to pre-commit hook (catch orphaned keys before they accumulate)
- Document i18n mock expectations in test utilities
