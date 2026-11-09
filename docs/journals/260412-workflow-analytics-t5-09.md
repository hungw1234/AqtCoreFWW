# T5-09 Workflow Analytics Shipped — Red-Teaming Pattern Pays Off

**Date**: 2026-04-12 evening
**Severity**: Low (shipped clean)
**Component**: Workflow module (analytics BE + FE dashboard)
**Status**: Resolved
**Commit**: `fc829071` pushed to `origin/main`

## What Shipped

- 6 Dapper analytics queries (volume, aging, status distribution, trend, bottleneck)
- WorkflowAnalyticsController + permission seeding
- ECharts dashboard (donut/bar/line/area charts) + antd DataTable (aging list)
- Query filters (date range, status, priority) with state management
- Permission `WORKFLOW.ANALYTICS.VIEW` seeded into 109 users

## The Brutal Truth

This was the smoothest gate pass in T5. T5-08 left 4 Critical + 3 High blockers at G2; T5-09 had 1 Critical + 2 High. **Red-team feedback loops are maturing.** Lessons from T5-08 (EF FindAsync ignores query filters, permission seeding is ritual not one-liner) got baked into delegation prompts → fewer surprises.

Annoying part: I caught myself trusting plan F10's assertion "Recharts not installed, must npm install" without grepping `package.json`. Codebase had `echarts@6.0.0` all along. Plan was stale. Lesson learned 3 times now: verify library assumptions.

## Technical Details

**C1 Route Mismatch (caught pre-G2)**
- Menu: `/admin/workflow-analytics`
- Actual route: `/admin/workflow/analytics`
- Build: passed. TypeCheck: passed. Click: 404.
- Root: FE agent copy-pasted from `admin-workflow-delegation-page.tsx` without normalizing route naming.
- Class of bug: no SSOT for routes when features split across files.

**H2 Top-5 + Others Aggregation**
- Product asked: "How to show top 5 assignees + Others bucket?"
- Plan suggested: complex SQL TOP N with GROUP/HAVING.
- What actually happened: in-memory C# partition. `Guid.Empty` as "Others" marker + `IsOthers: bool` flag.
- Trade-off: 23 lines C# > 50 lines SQL. KISS > clever.
- Works, performant for <1000 records. Defer SQL optimization if dataset explodes.

**Permission Const Pattern**
- Introduced `WorkflowPermissions.AnalyticsView` const file instead of magic strings.
- Small but foundational. Future features should reuse.
- Tech debt: `DelegationManage` still hardcoded (intentionally out of scope to keep change focused).

**BE DTO Class Strategy**
- Used `class` not positional `record` for analytics DTOs (Dapper compatibility).
- Followed memory feedback `feedback-dapper-no-positional-records` — positional records fail with nullable params.
- Dapper `QueryFirstOrDefault<T>` + `QueryMultiple` both respect this pattern.

## What I'd Do Differently

1. **Pre-flight library check** — grep `package.json` + `nuget.config` before trusting plan's lib assertions. Do this every time.
2. **Route SSOT** — FE should have route constants mirrored from BE controller path or shared config.
3. **Stale plan detection** — when plan says "library not installed," that's high-risk. Treat as assumption, not fact.

## Unresolved Nits (Defer)

- `DelegationManage` hardcoded permission string (T5-10 can refactor all perms to const).
- No alerting on analytics queries (they're fast, but DBA wants monitoring by T5-14).
- `IsOthers` flag workaround is pragmatic but not elegant. SQL window function `ROW_NUMBER()` better at scale.

## Next Steps

- T5-10: Profile & notification analytics (same dashboard pattern, reuse ECharts setup).
- Backlog: Permission const consolidation across all modules.
- Monitor: Analytics query perf after real data volume (currently <100 workflows in test).
