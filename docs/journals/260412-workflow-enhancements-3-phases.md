# Workflow Enhancements: 3 Phases Delivered (T5-11, T5-13, T5-12)

**Date**: 2026-04-12 04:00
**Severity**: Low
**Component**: Workflow Engine — Transitions, Audit, Webhooks
**Status**: Resolved

## What Happened

Shipped three phases of workflow enhancements: webhook transitions (P3), batch execution (P5), and audit export (P4). 26 tests added, 0 regressions, 258/258 workflow tests passing.

## The Brutal Truth

This session felt clean because we planned ruthlessly. But the wins were small — each phase was 1-2d work. The real value wasn't the features; it was discovering 14 issues before code hit main through deep red-teaming (RowVersion timestamp precision, TenantId leaking via webhook envelopes, FE double-unwrap in response interceptor).

## Technical Details

- **P3 webhook fix (F06)**: TenantId must not reach webhook envelope. Pre-existing WebhookDispatcher stripped it during serialization; we just had to test it.
- **P5 double-unwrap bug**: API client interceptor already unwraps the envelope, but FE code was unwrapping again. Caught in code review before merge.
- **P4 UTF-8 BOM critical**: Excel + Vietnamese text requires BOM prefix on CSV. Learned this the hard way in previous projects.

## Lessons Learned

1. **Planning beats panic**: Deep plan + red-team review reduced post-merge bug density to near-zero.
2. **Code review ROI is real**: Every phase caught ≥1 bug during review. Invest in this process.
3. **YAGNI scope cuts work**: PDF deferred (blocked on QuestPDF); JSON+CSV shipped. No regret.
4. **Mock SQL with reflection**: New pattern for unit-testing SQL isolation — works, but verify with integration tests to be safe.

## Next Steps

- P1 Delegation/Proxy (3d)
- P2 Dashboard Analytics (3d)  
- P6 Version Migration (5d — highest risk, defer last)

All commits pushed to origin/main.
