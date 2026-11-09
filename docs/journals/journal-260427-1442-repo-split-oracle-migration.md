---
title: Repo Split — Oracle-19c Branch Migrated to AqtCoreFW_Oracle
date: 2026-04-27
session_window: 13:00–14:42 (afternoon)
branch: main (this repo); migration source = oracle-19c
type: architectural-pivot
related_plan: plans/260427-1236-multi-provider-unification/ (CANCELLED)
---

# Repo Split — Oracle-19c → AqtCoreFW_Oracle

## TL;DR

Cancelled multi-provider unification plan (594h, no customer demand) → executed cheap alternative: **dual-branch model collapsed into dual-REPO model**. `oracle-19c` branch (749 commits, HEAD v2.156) pushed as `main` to new repo `https://github.com/thiennd212/AqtCoreFW_Oracle`. AqtCoreFW now SQL Server-only; Oracle work continues at `C:/AqtCoreFW_Oracle/`. Old branch retained 2 weeks for safety.

## Decision sequence

### 1. Validation interview cancelled multi-provider plan

Prior session red-teamed plan 260427-1236 → status `revision-required` with 7 amendments pending. This session ran `/ck-plan validate`, asked 4 BLOCKING questions:

| # | Question | Answer |
|---|----------|--------|
| §F | Postgres/MySQL customer demand confirmed within 12mo? | "Tạm thời hủy kế hoạch này" |
| §A | Approve Alt D pivot (per-provider SQL siblings vs dialect-helper)? | "Tạm thời hủy kế hoạch này" |
| §B | Approve Hangfire abstraction phase additions? | "Tạm thời hủy kế hoạch này" |
| §C-G | Approve mechanical edits to phases? | "Tạm thời hủy kế hoạch này" |

Decisive 4/4 cancel. Drivers:
- 594h ≈ 5 months solo with Claude Code
- ~80h separate-repo path = 6× cheaper
- Postgres/MySQL speculation, no roadmap commitment
- Dual-branch already working (just expensive to maintain)

### 2. Pivoted to repo-split

User confirmed via `/cook` follow-up: migrate `oracle-19c` to NEW repo `AqtCoreFW_Oracle`. 4 follow-up choices:
- Push as `main` (not `oracle-19c`) on new repo ✓
- Full 749-commit history (not squash) ✓
- 2-week safety window for old branch ✓
- Fresh local clone at `C:/AqtCoreFW_Oracle/` ✓

### 3. Migration executed

```
# Pre-migration safety
git -C C:/AqtCoreFW commit -m "chore: pre-migration journal + gitignore"  # 5a751723
git -C C:/AqtCoreFW tag archived/oracle-19c-v2.156 d9c729d5

# Cross-repo push
git -C C:/AqtCoreFW remote add oracle https://github.com/thiennd212/AqtCoreFW_Oracle.git
git -C C:/AqtCoreFW push oracle oracle-19c:main          # 749 commits, ~172MB
git -C C:/AqtCoreFW push oracle archived/oracle-19c-v2.156

# Verify + finalize on new repo
gh repo edit thiennd212/AqtCoreFW_Oracle --default-branch main
git clone https://github.com/thiennd212/AqtCoreFW_Oracle.git C:/AqtCoreFW_Oracle
```

GitHub flagged 3 LFS warnings (>50MB): `logs/aqtcorefw-20260408.log` 95MB, 2× `baseline-cases-*.json` 76MB each. Pre-existing oracle-19c bloat, NOT introduced this session. Push succeeded; cleanup via `git filter-repo` deferred.

### 4. Docs sync on `main` (this repo)

docs-manager subagent updated 7 files to reflect SQL Server-only stance + redirect-to-AqtCoreFW_Oracle for Oracle:

| File | Change |
|------|--------|
| `README.md` | Dual-branch table → "see AqtCoreFW_Oracle for Oracle" note |
| `docs/developer-onboarding-guide.md` | Oracle path redirect to new repo |
| `docs/tech-stack.md` | Separate-repo + parallel-branch model description |
| `docs/project-roadmap.md` | SQL Server primary, v2.132p3 pinned |
| `docs/project-changelog.md` | New v2.132p4 entry (35 lines) |
| `docs/dependency-map.md` | SQL Server only, Oracle redirect |
| `backlogs/backlog.md` | Split note + version row |

Verified via Grep on each file; AqtCoreFW_Oracle URL present at expected line numbers.

### 5. Auto-memory sync

| File | Action |
|------|--------|
| `memory/project-state.md` | Added top entry: REPO SPLIT (2026-04-27 afternoon) |
| `memory/multi-provider-plan-pending.md` | Status `revision-required` → `cancelled` + revival triggers |
| `memory/dual-branch-mssql-oracle.md` | Marked DEPRECATED with pointer |
| `memory/dual-repo-split-2026-04-27.md` | NEW — current architectural state |
| `memory/MEMORY.md` | Index updated: new entry top, deprecated entry below |

## Post-migration state

| Repo | Default branch | DB | HEAD | Local |
|------|----------------|-----|------|-------|
| AqtCoreFW | `main` | SQL Server 2022 | v2.132p3 | C:/AqtCoreFW |
| AqtCoreFW_Oracle | `main` | Oracle 19c | v2.156 | C:/AqtCoreFW_Oracle |

Snapshot tag `archived/oracle-19c-v2.156` on both repos = rollback point.

## Pending follow-up

1. **2026-05-11 (~2 weeks)** — delete `oracle-19c` branch on origin AqtCoreFW: `git push origin :oracle-19c`. Tag preserves snapshot, branch deletion safe.
2. **LFS bloat cleanup** (AqtCoreFW_Oracle) — 3 files >50MB inherited from oracle-19c history. Future filter-repo session.
3. **Plan amendments doc** (`plan-amendments-260427-1236.md`) — kept as historical record; revive only if Postgres/MySQL customer materializes.

## Lessons

- **Validation interview saved 594h.** Red-team flagged risks; validate forced concrete demand check; user said no → cheap alternative surfaced. ROI of structured validation > implementation effort it gates.
- **Tag-before-delete is cheap insurance.** `archived/oracle-19c-v2.156` cost nothing, preserves rollback even if origin branch deleted.
- **Cross-repo push works for migration without re-cloning** — just `git remote add` + `git push <remote> <local>:<dest>` from existing working tree.
- **Trust-but-verify caught 0 drift here** but the discipline matters; subagent did claim and Grep confirmed 7/7 doc edits at correct line numbers.

## Unresolved questions

- Should AqtCoreFW_Oracle have its own auto-memory dir, or share C:/AqtCoreFW one? Currently sharing — may want split if Claude sessions per repo grow distinct.
- LFS migration timing — wait for next routine maintenance window or schedule explicit?
