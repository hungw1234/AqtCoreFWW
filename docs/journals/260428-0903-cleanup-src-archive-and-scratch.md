# Cleanup `src/` — archive plans + remove scratch dirs

**Date:** 2026-04-28 09:03 (Asia/Saigon)
**Branch:** main
**Commit:** `dfbb0fd7`
**Trigger:** User audit `/cook "Kiểm tra lại toàn bộ cấu trúc thư mục, file của /src để đảm bảo không có thư mục, file nào dư thừa"`

## Context

Sau v2.132p8 ship (Phase 05+06 ModuleClient extraction), user kiểm tra lại structure `src/` sau khi thấy 13 Contracts assemblies vẫn nằm dưới `src/shared/`. Ban đầu nghi ngờ Phase 05 chưa hoàn thành; điều tra xác nhận đó là intentional design (assembly rebrand only, không physical relocate vào `src/modules/{Module}/`). Sau đó audit tiếp toàn bộ `src/` để tìm artifact dư thừa.

## Findings (4-tier prioritization)

| # | Path | Tracked | Verdict |
|---|------|---------|---------|
| 1 | `src/modules/organization/AqtCoreFW.Organization/` (flat legacy, 18 files) | ✅ | NOT trivially dead — JwtOrgContext + handler split + DTO/Service schema divergence. Migration 5-7 ngày. **Defer to separate plan** |
| 2 | `src/tests/AqtCoreFW.Tests.Integration/` (32 files) | ✅ | NOT duplicate of `tests/integration/*` per-module — divergent scenarios + fixture infra (~110 vs ~110 test methods). **Defer; coverage audit required** |
| 3 | `src/plans/reports/` (6 báo cáo cũ) | ✅ | Move sang `plans/reports/archive/` ✅ done |
| 4 | `src/.claude/`, `src/modules/forms/AqtCoreFW.Forms.Infrastructure/.claude/` | ❌ | Untracked scratch — rm -rf ✅ done |

## Decisions

1. **Phase 3+4 → today** (~1 commit, zero risk): `git mv` 6 file + `rm -rf` 2 scratch dirs.
2. **Phase 1 (Organization legacy)** → defer to plan **A** (TBD plan dir). Blocking: phải port JwtOrgContext sang 4-layer NEW trước khi xóa flat.
3. **Phase 2 (test consolidation)** → defer to plan **B** (TBD plan dir). Cần coverage audit map OLD↔NEW trước.

## Scout reports (preserved)

- `plans/reports/Explore-260428-0857-org-legacy-migration.md` — 9 MIGRATED + 2 NOT_MIGRATED + 2 DUPLICATED, 7 test files cần update
- `plans/reports/Explore-260428-0857-integration-tests-migration.md` — 2 hệ thống divergent, 7 NEW-only modules đã hoàn tất, recommendation: DO NOT DELETE YET

## Lessons

- **Cook hard-gate đúng**: task nhỏ "xóa folder dư" che giấu migration debt 5-7 ngày. Scout trước khi action không lãng phí.
- **`src/` purity**: workspace root `.claude/` artifacts từ subagent runs có thể leak vào subfolders nếu CWD subagent set sai — nên kiểm tra định kỳ.
- **Solution-vs-filesystem drift**: project flat tồn tại trên disk + tracked trong git nhưng không có trong `.slnx` = dấu hiệu refactor chưa cleanup. Cần audit định kỳ với `comm -23 <(filesystem) <(slnx)`.

## Next

- Quyết định ưu tiên Phase 1 (Organization) hoặc Phase 2 (test consolidation) trước.
- Tạo skeleton plan dir cho phase được chọn.
- Recommend `/compact` để clear cache trước khi vào phase tiếp theo.
