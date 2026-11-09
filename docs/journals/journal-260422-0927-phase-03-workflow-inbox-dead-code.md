# Journal — Phase 03: Dead Code Sweep (workflow-inbox-page)

**Date:** 2026-04-22 09:27+07
**Plan:** `plans/260421-1752-cases-workflow-finalize/phase-03-dead-code-sweep-workflow-inbox.md`
**Scope:** FE-only dead-code deletion (1 file, 1 barrel line)
**Version tag:** v2.127p1 (partial — 1 of 3 phases of Cases Workflow Finalize v2)

## Scope

- Deleted `web/src/features/workflow/workflow-inbox-page.tsx` (114 lines) — unreachable dead code, no external consumers.
- Edited `web/src/features/workflow/index.ts` — removed `export { WorkflowInboxPage } from './workflow-inbox-page';`
- Route `/workflow/inbox` remains live via independent `web/src/routes/workflow-inbox-page.tsx` → `@/features/inbox/inbox-page.tsx`.

## Key Finding — plan v1 was partially wrong about i18n

Plan v1 claimed `workflow.inbox.{title, pageTitle, filterStatus, filterPriority}` were orphan i18n keys to delete. Audit revealed:

1. **3 of 4 keys don't exist** in `web/src/core/i18n/locales/{en,vi}.json` — plan named them from code fallback behaviour, not from JSON structure.
2. **`workflow.inbox.*` namespace is LIVE** — owned by `task-detail-drawer.tsx` (kept), not the deleted file. Drawer references 10+ keys under this namespace (`complete`, `reassign`, `reason`, `status`, `priority`, `assignee`, `dueDate`, `created`, `description`, `completed`, `reassigned`, `newAssignee`).
3. Deleting the namespace per plan v1 would have broken the live drawer — vitest wouldn't catch missing i18n strings.

**Action taken:** Skipped i18n cleanup entirely. Updated `phase-03-...md` to strike the i18n deletion section + flag the finding for future agents.

## Verification

| Gate | Result |
|---|---|
| `pnpm tsc --noEmit` | exit=0 (0 type errors) |
| `pnpm vite build` | clean, 1m27s |
| `pnpm test` | 755 pass / 20 fail (pre-existing vitest v4+antd Form, unrelated) |
| Grep `features/workflow/workflow-inbox-page` in `web/src` | 0 hits |
| Grep `WorkflowTaskDto` in `web/src` | 8 hits (preserved) |
| Code-reviewer | DONE_WITH_CONCERNS — concern was plan doc correction, addressed |

## Rules Reinforced

- **`feedback-red-team-before-implement.md`** fired: red-team of plan v1 + during-implementation audit both caught layers of error. Red-team surfaced 15 findings; audit during Phase 03 surfaced the i18n structure mismatch that red-team missed.
- **`feedback-auto-scout-no-ask.md`** fired: auto-grepped `features/workflow/workflow-inbox-page`, `WorkflowTaskDto`, i18n consumers — no permission asks.
- **`feedback-tests-pass-feature-fails.md`** applies indirectly: if i18n was deleted per plan v1, vitest would still pass 755/775 but drawer would render raw keys in prod. Structure audit > test green.

## Context Engineering Note

Phase 03 execution followed context-audit session earlier today (09:10). Memory index cleanup saved ~440 tokens/session; Phase 03 audit leveraged grep-first pattern (4 parallel greps at each decision point) → avoided scout subagent spawn entirely. Net effect: small phase done in ~30min with no context bloat.

## Files Changed

- `web/src/features/workflow/workflow-inbox-page.tsx` — DELETED
- `web/src/features/workflow/index.ts` — barrel −1 line
- `docs/project-changelog.md` — added v2.127p1 entry
- `docs/journals/journal-260422-0927-phase-03-workflow-inbox-dead-code.md` — this file
- `plans/260421-1752-cases-workflow-finalize/plan.md` — Phase 03 marked complete + session log (gitignored)
- `plans/260421-1752-cases-workflow-finalize/phase-03-dead-code-sweep-workflow-inbox.md` — i18n section corrected (gitignored)
- `~/.claude/projects/C--AqtCoreFW/memory/project-state.md` — updated (not in repo)
- `~/.claude/projects/C--AqtCoreFW/memory/MEMORY.md` — updated (not in repo)

## Next Steps

- **Phase 02** (WorkflowDefinition API + validation + RBAC) — P0, effort 1.5-2.5d.
- **Phase 01** (Outbox dispatcher MassTransit EF Outbox) — P0, effort 3-5d.
- Before Phase 02 start: confirm 6 open questions from plan review (seeder casing scan, MassTransit DbContext scope, Option A/B stub, PR strategy, CI broker-kill commitment, H4 escalation path).
