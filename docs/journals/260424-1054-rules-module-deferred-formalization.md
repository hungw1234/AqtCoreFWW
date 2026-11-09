# Journal — Rules Module Deferred Status Formalization

**Date:** 2026-04-24 10:54 ICT  
**Branch:** thiennd/amazing-cohen-e36cd3  
**Type:** Documentation drift fix + YAGNI state formalization  
**Scope:** doc-only, no code changes

## Context

Anh thắc mắc Workflow và Rules module trông rời rạc, rồi hỏi Rules engine có thật sự cần thiết trong core base không, và nếu cần thì nghiệp vụ nào phù hợp.

## Findings (2 scout passes)

### Pass 1 — Workflow ↔ Rules integration audit
Xác nhận **disconnected at code level**:
- `IRuleEngineModuleClient` grep trong `src/modules/workflow/` → **0 matches**
- `IWorkflowModuleClient` grep trong `src/modules/rules/` → **0 matches**
- Workflow dùng `ConditionEvaluator` riêng (11 hardcoded operators)
- Rules dùng `RulesEngineEvaluator` riêng (Microsoft.RulesEngine)
- 0 FK cross-schema, 0 DI cross-wiring, 0 event subscription
- Chỉ có planned integration AI-W4 (Decision Support) trong backlog — chưa build

### Pass 2 — Rules necessity & business fit
Phát hiện **doc drift**: `dependency-map.md:41` ghi `Forms → Rules` nhưng empirical grep `src/modules/forms/` for `IRuleEngineModuleClient` = **0 matches**. Forms dùng `VisibilityRulesJson` inline + FluentValidation, không gọi Rules module.

Verdict: Rules module **production-ready nhưng 0 active consumer** — orphan state.

### Top 3 candidate business triggers (justify keeping module)
1. **Fee calculation + waiver** cho dịch vụ công — ngưỡng/thông tư đổi quý cần versioning + audit + deploy-less. ROI 4–6× per thông tư.
2. **Eligibility pre-check gate** — multi-agency rule packs trước khi render form.
3. **Form auto-approval thresholds** — tuning fraud_score / document_count không deploy code.

### Top 3 overkill (KHÔNG wire)
- Workflow FSM transitions (domain boundary, mất type safety)
- SoD conflict check (O(1) matrix lookup nhanh hơn)
- ABAC authorization (đã có `PolicyRule` + NCalc tích hợp Identity)

## Decision

Chọn **Option A** — giữ module, đánh dấu deferred, fix doc drift. Lý do:
1. Module đã polish xong (v2.131p1, tenant hardening T4-05 FU5/FU5.1/FU5.2, 12 tests)
2. Fee calculation/Eligibility là use case chắc chắn sẽ đến cho gov platform VN
3. Khớp pattern AI modules deferred (memory `project-ai-modules-deferred-implementation.md`)

## Changes applied

| File | Change |
|------|--------|
| [docs/dependency-map.md](../dependency-map.md) | L41: consumer `Forms` → `(none — deferred)`. §Rules: thêm Status note giải thích YAGNI-deferred + candidate triggers |
| [memory/project-rules-module-deferred.md](../../../../Users/thien/.claude/projects/C--AqtCoreFW/memory/project-rules-module-deferred.md) | NEW — mirror pattern của AI deferred memory |
| memory/MEMORY.md | L6: thêm index entry cho project-rules-module-deferred |
| [backlogs/backlog.md](../../backlogs/backlog.md) | L30 section mới "Rules Engine Module Deferred Implementation" với R-W1/W2/W3/W4 tasks |

## Finalize Verification

| # | Artifact | Status | Evidence |
|---|----------|--------|----------|
| 1 | docs/dependency-map.md | ✅ | `Grep "deferred"` → L41 "_(none — deferred; see §Rules note)_"; §Rules "Status (2026-04-24): DEFERRED" paragraph added |
| 2 | docs/project-roadmap.md | N/A | No phase status change / version bump — doc drift fix only |
| 3 | docs/project-changelog.md | N/A | No PR / version bump — doc drift fix only (will attach to next chore commit) |
| 4 | backlogs/backlog.md | ✅ | `Grep "R-W1"` → L34 "R-W1: Fee calculation + waiver rules" + full R-W1..R-W4 section |
| 5 | memory/project-state.md | N/A | No session-level state change — same v2.131p1 baseline |
| 6 | memory/MEMORY.md | ✅ | `Grep "project-rules-module-deferred"` → L6 index entry present |
| 7 | Session journal | ✅ | This file |

## Follow-ups

- Commit via git-manager (chore(docs): formalize Rules module deferred status + fix Forms→Rules doc drift)
- R-W4 in backlog: sweep other docs (codebase-summary, system-architecture, plans/) nếu có claim khác về Rules consumption
- Chờ business trigger để activate R-W1/R-W2/R-W3

## Unresolved questions

1. Product roadmap 3–6 tháng tới có feature Fee calculation hoặc Eligibility gate không? → sẽ quyết khi nào activate R-W1/R-W2.
2. Identity `PolicyRule` (NCalc-based) có plan gộp vào Rules module không, hay giữ 2 engine riêng? Hiện tại đánh giá: giữ riêng vì ABAC cần per-request cache + tight Identity integration.
