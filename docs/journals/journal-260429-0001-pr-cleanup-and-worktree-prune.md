---
title: PR Triage + Worktree/Branch Cleanup
date: 2026-04-29 00:01-00:15
author: ThienND
tags: [session, maintenance, cleanup, git-hygiene, worktree, pr-triage]
type: session
status: completed
---

# PR Triage + Worktree/Branch Cleanup

**Date**: 2026-04-29 00:01-00:15
**Severity**: Low (housekeeping, no production impact)
**Component**: Repo hygiene (PRs, worktrees, local branches)
**Status**: Completed

---

## Tóm tắt

Audit 2 PR mở (#27 docs sync 11 ngày tuổi, #51 scripts 6 ngày tuổi) — cả hai conflict với main do drift v2.105 → v2.133p6. Quyết định: cherry-pick journals từ #27 + close cả hai PR + xóa 21 local branches lửng + 3 worktrees stale.

---

## Trước cleanup

| Loại | Số lượng | Chi tiết |
|------|----------|----------|
| Open PRs | 2 | #27 (docs sync), #51 (per-service scripts) |
| Worktrees | 4 | main + great-villani, hopeful-engelbart, priceless-hamilton |
| Local branches | 22 | main + oracle-19c (archived) + docs/post-phase-01-merge-sync + 19 thiennd/* |

## Quyết định triage

### PR #27 — docs sync
- 14 files: 11 journal ADD + 3 docs MOD (roadmap/changelog v2.102→v2.105)
- Roadmap diff obsolete (main giờ v2.133p6, drift 28 versions)
- **Action**: cherry-pick 11 journal files → main (commit 61fccc50), close PR

### PR #51 — per-service scripts
- 1 file: `scripts/1_run-local-dev.ps1` +249/-126
- Conflict do main v2.132 (b4a9dea8, 2026-04-25) đã refactor cùng file theo Docker auto-start
- User confirm: feature không còn cần
- **Action**: close PR + xóa branch

### Worktree salvage
- `great-villani-27e5ac` chứa untracked `journal-260422-0907-signature-enrollment-pivot.md` (Signature Enrollment Pivot session, ~7.5KB) → salvage trước khi `--force` remove
- 2 worktrees còn lại sạch (no uncommitted)

---

## Thực thi

| Bước | Kết quả |
|------|---------|
| Salvage signature-enrollment journal | ✅ copied to docs/journals/ |
| Cherry-pick 11 journals từ PR #27 | ✅ `git checkout origin/docs/post-phase-01-merge-sync -- <11 files>` |
| Stage + commit (13 journals total: 11 + 1 salvage + 1 qd-742 untracked) | ✅ commit `61fccc50` |
| Push main | ✅ `e470400b..61fccc50` |
| Close PR #27 + #51 | ✅ với comment giải thích |
| Remove 3 worktrees | ✅ (great-villani cần `--force` do untracked file) |
| Delete 21 local branches | ✅ batch `git branch -D` |

## Sau cleanup

| Loại | Số lượng |
|------|----------|
| Open PRs | 0 |
| Worktrees | 1 (main) |
| Local branches | 1 (main) |

## Bonus journal commit

3 journals được salvage trong cùng commit:
1. **11 journals từ PR #27** (Phase-00/01 sessions 2026-04-16/17): chunk-01 → chunk-05 finalize, seed-caseprocess, techdebt-cleanup, shipped-phase-02-redteam
2. **journal-260422-0907-signature-enrollment-pivot**: từ great-villani worktree
3. **journal-260428-1845-qd-742-cap-4-plan-approval**: untracked từ session trước (QĐ 742 ATTT plan)

Tổng: 2,208 lines tài liệu lịch sử được preserve.

---

## Memory updates needed

- `memory/dual-repo-split-2026-04-27.md`: oracle-19c local branch xóa sớm (target was ~2026-05-11), tag `archived/oracle-19c-v2.156` vẫn intact
- `memory/project-state.md`: không thay đổi (cleanup không phải version bump)

---

## Lessons / patterns

- **Cherry-pick file > cherry-pick commit** khi commit gốc lẫn lộn nội dung tốt + outdated (như #27 lẫn journal mới + roadmap stale)
- **Worktree --force salvage order**: copy untracked files RA TRƯỚC, sau đó mới `worktree remove --force` — không phụ thuộc git để khôi phục
- **PR conflict ≠ merge worth retrying**: tuổi PR + drift versions là tín hiệu close hơn rebase
- **Branch reaper**: khi shipped via squash-merge, local branches có SHA khác main → `-d` sẽ refuse, dùng `-D` (force) — an toàn vì commits đã trong main dưới SHA mới

---

## Unresolved questions

Không có. Cleanup đóng gọn.
