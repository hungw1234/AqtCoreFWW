# Journal — Oracle plan docs drift sweep before session boundary

**Date:** 2026-04-25 11:25
**Branch:** `main` (synced với `oracle-19c` @ `f7c2dc2a`)
**Session intent:** User chuẩn bị đóng session hiện tại + mở session mới trên `oracle-19c` để bắt đầu migration; yêu cầu rà soát tài liệu trước khi cắt.

## What happened

Audit toàn bộ tài liệu liên quan Oracle 19c migration sau khi plan đã revise qua Session 2 + 2.5. Phát hiện **6 drift điểm** giữa tài liệu vs plan reality — fix tất cả trong 1 commit `f7c2dc2a`.

### Drift items found + fixed

| # | File | Drift | Root cause |
|---|------|-------|------------|
| 1 | `memory/project-state.md` line 12-13 | `main @ 85d1f115`, "NOT PUSHED" | Updated lần 1 chỉ chỗ "branch list" line 15-17, missed "Current Branch" line 12 |
| 2 | `docs/project-roadmap.md` line 9 | "Plan validated 7/7 questions" — chỉ Session 1, không có Session 2+2.5 | Plan revision không trigger doc sync — manual sweep cần |
| 3 | `docs/project-changelog.md` line 136-152 | "1 Oracle user", "1-shared-container CI" — Session 1 lock, mâu thuẫn Session 2 D1+D3 | Session 1 entry không updated khi Session 2 lock override |
| 4 | `docs/dependency-map.md` line 6 | `feat/oracle-19c-migration` branch | Tên branch đổi sang `oracle-19c` ở Session 2.5, dep-map lỡ |
| 5 | `backlogs/backlog.md` line 13, 22, 28 | `feat/oracle-19c-migration` + "29 policies spec'd" | Phase scope details thay đổi Session 2 (~45 tables F2) |
| 6 | `docs/tech-stack.md` line 64, 67, etc. | "feat/" branch + "1 Oracle user `aqtcore`" + ODP.NET unversioned | Future State section là copy của Session 1 lock |

## Brutal truth

Plan revision (Session 2 + 2.5) viết trong `plan.md` + 8 phase docs **không đủ** để tài liệu đồng bộ. Memory được update inline (project-state.md), nhưng 5 docs (roadmap, changelog, dep-map, backlog, tech-stack) vẫn giữ Session 1 wording cho đến khi user yêu cầu rà soát.

**Pattern nguy hiểm:** Plan files là source of truth → nhưng docs là nơi engineers/PMs đọc trước khi tìm plan. Stale docs = dev bắt đầu phase-01 với sai assumptions (vd "1 Oracle user" → wire connection string sai cho phase-05 hangfire isolation).

## Technical details — fix approach

1. **In-place edits** với explicit "[Session 2 D3]" / "[Session 2 F2]" markers — preserve audit trail mà không cần version-control archive folder.
2. **Session 1 entry kept** ở `project-changelog.md` (line 156-176, sau Session 2+2.5 entry mới) với inline notes "[→ Session 2 D3 changed to 2 users]" — historical audit trail intact.
3. **Branch references**: 6 hits `feat/oracle-19c-migration` → 5 sites updated, 3 still in journals (intentional — historical record không nên rewrite).
4. **Memory project-state.md**: 2 spots updated (line 12-13 "Current Branch" + line 76-77 Session Hints) — single edit miss → user-noticed drift này.

## Root cause + lesson

**Root cause:** "Plan revision" task scope không tự động bao trùm "doc sync". Subagent `planner` chỉnh plan, nhưng `docs-manager` không được spawn ở Session 2 hoặc 2.5 finalize. → 5 docs drift.

**Lesson — escalate vào primary-workflow.md Section 7 verification table:**
> Khi plan revision (không phải feature ship) lock decisions thay đổi locked decisions trước, **PHẢI** spawn `docs-manager` với explicit list:
> - `docs/project-roadmap.md` (overview row)
> - `docs/project-changelog.md` (new entry, không overwrite cũ)
> - `docs/dependency-map.md` (nếu branch/RDBMS đổi)
> - `backlogs/backlog.md` (per-phase scope)
> - `docs/tech-stack.md` (Future State section)
> - `memory/project-state.md` (current branch + session hints)

**Hiện tại Section 7 đã list 7 artifacts nhưng "tech-stack.md" KHÔNG có trong list** — gap này là root cause Session 2+2.5 docs trượt 1 ngày trước khi user catch.

## Next steps (deferred — không action ngay)

- [ ] **Soft TODO:** Khi finalize phase nào thay đổi tech-stack (Oracle phase-01 ship), update `docs/tech-stack.md` Database table line 41-43 — đổi primary từ SqlServer → dual (`main` SqlServer / `oracle-19c` Oracle) hoặc giữ nguyên + Future State section đã có đầy đủ.
- [ ] **Optional:** Add `docs/tech-stack.md` vào Section 7 verification table của primary-workflow.md — nhưng đây là user-level rule, em không tự edit.

## Unresolved questions

- (none) — toàn bộ drift đã fix + commit + pushed. Cả 2 branches `main` + `oracle-19c` synced @ `f7c2dc2a`. Session ready to close.
