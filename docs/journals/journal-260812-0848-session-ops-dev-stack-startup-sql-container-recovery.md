---
date: 2026-08-12
time: "08:18-08:48"
type: session-ops
scope: dev-stack-startup
commits: []
---

# Session ops — dev stack startup + SQL container recovery (2026-08-12 sáng)

## Session goal

Advisory (`what to do next?` cho branch `feat/forms-cascade-public-external-inrow`) + dựng dev stack local qua `/aqt-runapp` để tiếp tục Phase 06-07 (M1 slice).

## What worked

- Ground-truth scout xác nhận branch ahead main 9 commits (C1 3-phase + H2 2-phase đều DONE); scope còn lại = M1 in-row TableField cascade (~2.75d, plan `260805-1409-*` Phase 06-07). H1 user-DEFERRED (vacuous).
- Vault auto-seed qua `vault-init` container (5/5 secret keys) — dev-mode in-memory backend mất khi restart, confirm pattern skill xử lý đúng.
- MinIO + ClamAV cold-start OK (`clamd READY` sau ~30s do signature DB load RAM).
- Auth (5002) + API (5001) + FE Vite (5173) đều listening sau khi SQL container up.

## What didn't / lessons

- **SQL Server local không chạy khi start stack**: `docker-sqlserver-1` exited 8 ngày trước (exit 255) — không auto-detect/start trong `aqt-runapp` (skill hiện chỉ probe Vault/MinIO/ClamAV). Auth crash Error 258 (SQL timeout) tại `Migrator.MigrateAsync` (`Program.cs:445`). Ambiguity: Windows service `MSSQLSERVER22` cũng có nhưng là named instance (port dynamic), không match connection string `localhost,1433` → user chọn Docker path.
- **Backup job noisy warning**: `/backups\AqtCoreFW_...bak Access denied` — mount permissions container, không phải bug code, nhưng log spam khi start.
- **DocFlowOrphanBlobSweep skipped 18 tenants** với "candidates but zero referenced keys — skipping as a safety guard (possible reference-load/RLS gap)" — fail-safe hoạt động đúng, nhưng có thể là dấu hiệu seed data drift trên dev DB (candidates orphan không link được reference keys). Chưa investigate.
- BrowserPanel guest chưa ready (`guest_not_ready` retryable) sau khi FE up — bỏ qua auto-open, user tự mở.

## Decisions made

- **Ship M1 tiếp** (Phase 06-07) là recommendation → user confirm trước khi vào Phase 06.
- SQL source = Docker container (khớp connection string `localhost,1433 + sa/YourStrong!Passw0rd`), không dùng Windows named instance.

## Test status

- Không chạy test trong session này (ops-only, zero commits).

## Next session

1. **Phase 06 M1 BE** — `CascadeDependencyValidator` extend cho child-fields + `FormValidationService` option-membership guard (TDD-first, ~1.5d).
2. **Phase 07 M1 FE** — `row-ref-field-select.tsx` mới + wire vào `table-field-widget.tsx` (~1.25d).
3. Ship + merge branch main (theo pattern `04c75105a` cascade v1).
4. Decision cần confirm trước Phase 06: option-membership guard cho external DS = hard-reject 422 hay soft-log warn (plan lock hard, recommend soft cho external do latency + eventual consistency).

## Backlog carry-over

- **DocFlowOrphanBlobSweep 18 tenants skipped** — chưa file row; nếu là seed drift không phải bug, hạ ưu tiên; nếu reproduce liên tục thì file `SIGNING-DOCFLOW-ORPHAN-BLOB-REFERENCE-LOAD-GAP` T4.
- **aqt-runapp không auto-recover SQL container** — enhancement candidate cho skill (probe docker container state + start nếu exited, tương tự Vault path). Không file backlog row vì skill code không thuộc project scope.
