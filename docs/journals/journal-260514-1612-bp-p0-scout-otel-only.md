# Journal — 2026-05-14 16:12 ICT — BP MVP P0 scout: OTel-only

**Type:** Validation checkpoint (no code commit; plans/ gitignored)
**Plan:** `plans/260420-1957-business-process-definition-mvp/`
**Trigger:** `/aqt-next` → `/cook "Tiến hành P0 scout-first --auto"`

## Session goal

D4 follow-through từ BP MVP Validation Session 1 (2026-05-14 14:50): scout 2 index P0 đề xuất trước khi implementation, xác nhận có cần migration hay không.

## What worked

1. **Inline read 2 EF configs** thay vì spawn Explore agent. Phạm vi scout = 1 câu hỏi yes/no trên 2 file → overhead subagent (200K context) là YAGNI.
2. **Verdict rõ ràng:**
   - `IX_WorkflowInstances_Tenant_Entity_Active` → redundant (đã biết Session 1 D4).
   - `IX_WorkflowTransitions_DefinitionId_FromStateId` → **đã tồn tại** tại `WorkflowTransitionConfiguration.cs:32-33` (D9 new).
3. **Plan housekeeping:** updated `plan.md` Session 2 log (D9 + D10) + `phase-00.md` (status `ready-after-scout` → `ready`, effort `0.5-1d` → `0.25-0.5d`, both index reqs struck through, migration task dropped, risk row dropped).
4. **Scout report:** `plans/260420-1957-business-process-definition-mvp/research/scout-260514-1612-bp-p0-index-audit.md` — verdict + unresolved Qs.

## What didn't / lessons

- **Confused `git status` empty:** ban đầu nghĩ Edit/Write failed; verify qua `git check-ignore` → `plans/` được gitignore. Lesson: ở repo này, mọi work trong `plans/` không lên git — chỉ journal/backlog/docs là persistent record. **Codified pattern:** plan-internal validation deltas không cần commit; chỉ journal tổng kết cần commit.
- **YAGNI win trên subagent spawn:** scout 1-question = inline (2 Read calls); scout multi-file/multi-question = Explore. Saved ~5min + 200K context allocation.

## Decisions made

| # | Topic | Decision | Rationale |
|---|-------|----------|-----------|
| D9 | P0 `(DefinitionId, FromStateId)` index | DROPPED — đã tồn tại | `WorkflowTransitionConfiguration.cs:32-33` direct evidence |
| D10 | P0 effort + status | `0.5-1d` → `0.25-0.5d`; `ready-after-scout` → `ready` | OTel-only, no schema mutation |

## Test status

N/A — read-only scout, không chạm source.

## Next session

**Recommended next:** Ship P0 OTel-only (~0.25-0.5d) — fresh-morning fit:
1. Create `WorkflowMetrics.cs` (Meter + 2 histograms)
2. Wire `execute_transition.duration_ms` vào `ExecuteTransitionCommandHandler`
3. Wire `allowed_transitions.query_duration_ms` vào `QueryAllowedTransitionsHandler`
4. Register Meter ở `WorkflowModuleRegistration`
5. Verify OTLP local (existing infra per project-state)

**Parallel-safe alternative:** P1 FormTemplateVersion entity (1.5d BE+FE). Forms-only, no overlap với P0.

**Carry-over from BP MVP:**
- P2 finalization vẫn unlock P3→P4 sequence.
- Pre-P4 P4-FU đã ship (`92db0981`, baseline 125 locked).

## Today's cadence

8 ships hôm nay (đến `2a9ef6a1` v2.137p21 close-out). Session này thuần advisory — 0 ship, 0 push. Wrap đúng fatigue window.
