# Skill Maintenance — aqt-runapp FE/BE Scope Clarification

**Date:** 2026-04-13 13:05 | **Type:** Tooling (global skills, no AqtCoreFW code change)

## Session Goal
Sửa wording trong `~/.claude/skills/aqt-runapp/SKILL.md` để phân tách rõ scope:
- BE = .NET only (`dotnet run`)
- FE = bất kỳ Node-based dev server (React+Vite, Next, Vue, Svelte, Remix, npm-script)

Trigger: lo ngại câu "Other BE stacks (Node, Python, Go) are out of scope" gây hiểu nhầm rằng FE ReactJS (Node-based runtime) cũng bị loại.

## What Worked
- Cập nhật `description` YAML field — rõ "BE: .NET ... FE: any Node-based dev server — React+Vite, Next.js, Vue, Svelte, Remix, generic npm script"
- Rewrite intro paragraph thành "Scope (locked)" block với 2 bullet tách Backend/Frontend
- Bump version 3.1.0 → 3.2.0 (wording-only patch)

## What Didn't / Lessons
- `~/.claude` không phải git repo → không thể commit skill changes vào VCS. Chấp nhận trade-off (skills là personal config, không version-control).

## Decisions Made
- Giữ detection code không đổi — chỉ sửa wording. YAGNI.
- Không thêm Layout Example mới — Layout A đã có Vite, đủ phản ánh stack hiện tại.

## Test Status
- N/A — chỉ markdown edit, không có test runner.

## Files Touched
- `C:\Users\thien\.claude\skills\aqt-runapp\SKILL.md` (description + intro + version bump)

## Next Session
- AqtCoreFW dirty state cleanup: nhiều `web/test-results/*` artifacts cần gitignore hoặc clean.
- Untracked dirs cần kiểm: `.config/`, `backlogs/Incoming/`, `jenkins-shared-library/`, `src/host/AqtCoreFW.Api/logs/` — quyết định track hay ignore.

## Unresolved Questions
- Có nên duplicate `kill_port()` helper sang skill thứ 3, hay extract ra shared script trong `~/.claude/skills/_lib/`? (3 lần dùng = không còn YAGNI nữa, nhưng skill files cần self-contained để portable.)
