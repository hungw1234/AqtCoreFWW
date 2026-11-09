# P7-FU1.1-FU: Pagination PageSize Clamp Follow-Up

**Date**: 2026-05-17 14:10 (ICT)  
**Severity**: Medium  
**Component**: Forms.Application, Workflow.Application  
**Status**: Resolved  
**Commit**: `ca321a97` (origin/main)

## What Happened

Added `Math.Clamp` guards on paginated query handlers across Forms and Workflow modules to cap pageSize between 1 and 1000. Small follow-up to P7-FU1 after reviewer surfaced broader cross-module pattern.

## The Brutal Truth

This is a "duh" fix — pagination DoS vectors are table-stakes. The frustrating part: we shipped P7-FU1 without it, then reviewer's grep found ~25 unclamped handlers across the codebase. Shows code-review **as pre-deployment audit** (not just polish) catches real systemic gaps that slip through individual-handler thinking.

## Technical Details

**Forms.GetFormTemplatesQueryHandler.cs**
```csharp
private const int MaxPageSize = 1000;
var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
var page = Math.Max(1, request.Page);
```

Replaces all downstream references (Dapper params, `PaginationMeta` echo). Meta now reports **clamped value** — correct REST semantics (RFC 7231: server reports actual delivered resource count).

**Workflow.GetWorkflowDefinitionsQueryHandler.cs**: Identical mirror.

**Test coverage**
- Forms: `Handle_PageSizeBeyondMax_ClampsToMaxInMeta`, `Handle_PageSizeZeroOrNegative_ClampsToOne`
- Workflow: NEW test file with 2 parallel facts (M-1 fold-in: Workflow had no prior test file)

**Results**: Forms 8→10 facts, Workflow 431→433, Architecture 97/97 PASS. Build clean.

## What We Tried

Initial plan: file as Low FU, defer to P7-FU1.1-FU. Reviewer completed during code-review → folded inline (5min symmetric test file < tracking separately).

## Root Cause Analysis

P7-FU1 delivered the pattern for **one handler** without pre-emptive sweep. Single-instance fixes don't surface systemic gaps — requester gets what they asked for, not what the codebase needs. Reviewer's grep exercise (5min) caught what no single implementation can.

## Lessons Learned

1. **Reviewer-as-auditor value**: Expanded grep during review (Forms + Workflow + other modules) revealed 25 unclamped handlers. Codify: ask reviewer to grep pattern cross-module when fixing single-instance bugs.

2. **Meta echo semantics**: Response `PaginationMeta.PageSize` = clamped server value, not original request. This is **correct** — RFC 7231 spirit. No FE breakage because picker passes 500 < 1000 cap anyway.

3. **Identity precedent reuse**: `ListSodViolationLogsQueryHandler` had 100 cap; bumped to 1000 for consistency. Different handlers can have different tiers (audit vs admin vs picker surfaces). Tier policy belongs in shared `PaginationDefaults`, not scattered constants.

4. **Fold-in economics**: Workflow test parity (~30 LOC, 5min) is cheap enough to include same ship. Carrying as separate task burns more overhead than delivery.

## Next Steps

**ARCH-FU2** (Medium, ~3-4h): Cross-module pageSize clamp sweep for ~25 unclamped handlers (`GetAuditLogs`, `GetLoginAudit`, `ListUsers`, `GetChatHistory`, etc.) + shared `PaginationDefaults` helper (50 audit / 100 admin / 1000 picker tiers) + ArchUnit enforcement rule. Reviewer recommendation surfaces real value-add beyond single-instance fix.

---

**Wallclock**: ~20min vs 30min estimate (1.5x compression). Ship #9 of day.
