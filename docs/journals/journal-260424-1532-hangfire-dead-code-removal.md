# Journal — Hangfire Dead-Code Migration Removal (closes v2.131p3)

**Date:** 2026-04-24 15:32 ICT | **Branch:** `main` | **Session type:** Bugfix + cleanup

## Problem

Mỗi lần restart API → Serilog log warning:

```
Microsoft.Data.SqlClient.SqlException: Invalid column name 'Queue'.
at HangfireRegistration.MigrateHangfireQueues (HangfireRegistration.cs:85)
```

Non-fatal (try/catch swallow) nhưng pollute startup log + gây nhầm lẫn khi debug.

## Root Cause — 2-layer bug

### Layer 1: Schema drift — Hangfire 1.8 moved `Queue` column

Code target `hangfire.Job.Queue`. Config chạy `CompatibilityLevel.Version_180` → Hangfire.SqlServer 1.8+. Schema v9 **đã drop cột `Queue` khỏi `Job` table** — queue ownership giờ 100% trong `hangfire.JobQueue(JobId, Queue, FetchedAt)`. Migration target sai bảng → dù không có parser error thì UPDATE cũng chẳng làm gì.

### Layer 2: SQL Server compile-time column validation

Code có `IF EXISTS (SELECT 1 FROM sys.columns WHERE ...)` kỳ vọng bảo vệ nếu column không tồn tại. **Sai**: SQL Server deferred name resolution chỉ defer khi **object** chưa tồn tại, không defer khi **column** thiếu. `hangfire.Job` table có thật → parser validate `Queue` column ngay ở compile batch → báo lỗi trước khi `IF EXISTS` guard chạy runtime.

**Key learning:** `IF EXISTS` + `sys.columns` guard **vô nghĩa** khi column missing trong table đã tồn tại. Muốn defer column validation phải dùng `sp_executesql` wrap dynamic SQL.

## Grep evidence → dead from inception

`webhook:critical|webhook:normal` repo-wide → **3 matches, đều trong chính migration method**. Không code path nào từng enqueue job với tên colon-delimited → migration không có data để chạy kể từ commit đầu tiên.

## Fix — Option A (YAGNI)

Xoá hẳn:

| File | Delta |
|------|-------|
| `src/shared/AqtCoreFW.Infrastructure/BackgroundJobs/HangfireRegistration.cs` | -36 (method) -2 (orphan usings: `Microsoft.Data.SqlClient`, `Microsoft.Extensions.Logging`) |
| `src/host/AqtCoreFW.Api/Program.cs` | -4 (comment + 3-line call site) |

**Total: -38 lines, +0 lines.**

## Verification

| Check | Result |
|-------|--------|
| `dotnet build` Infrastructure | 0 warnings, 0 errors |
| `dotnet build` Api | 0 warnings, 0 errors |
| `code-reviewer` agent | DONE, 0 concerns |
| Grep `MigrateHangfireQueues` repo-wide | 0 matches (docs/tests/code) |
| Program.cs block coherence | Force-resolve `JobStorage` block still reads standalone |

## v2.131p3 bundle close

| # | Commit | Scope |
|---|--------|-------|
| 1 | `123801a5` | Sidebar menu Option C — 10 top-level flat restructure (BE seed + FE fallback) |
| 2 | `4dd6fc3d` | Test mock debt fix — NotificationBell + Topbar (pre-existing 11 fails → 789/789 pass) |
| 3 | `7aa4580e` | Journal for sidebar Option C |
| 4 | *(this session)* | Hangfire dead-code removal |

## Rollback

`git revert {hangfire-commit}` — 2 files, no schema change, no dependency change. Safe to revert standalone.

## Follow-ups

- [ ] Dev local cần purge + restart API để reseed BE menu (từ journal sidebar)
- [ ] Dev local restart sau commit v2.131p3 → xác nhận log Serilog hết warning "Invalid column name 'Queue'"
- [ ] Cân nhắc: nếu tương lai cần migrate Hangfire queue names, viết đúng: target `hangfire.JobQueue`, wrap trong `sp_executesql` để defer compile

## Unresolved

- [ ] Nếu có DB dev cũ nào chưa startup sau fix → warning sẽ biến mất trong runtime log, không có regression nào cần test thêm
