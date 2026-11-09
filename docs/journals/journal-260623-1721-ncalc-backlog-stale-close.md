# Journal — 2026-06-23 17:21 ICT — `BE-NUGET-NCALCSYNC-NU1902` stale-close

## Session goal
`/aqt-next` → user chọn `/cook BE-NUGET-NCALCSYNC-NU1902` (T3 row, filed 2026-06-20: NCalcSync 5.12.0 carries `NU1902`/`GHSA-3w5p-95mh-gq75` factorial-DoS advisory).

## What happened
Không phải implementation — INVARIANT 260522-A audit-at-slice-start bắt row STALE:
- Probe: `NCalcSync` pin NOW = **6.1.1** (`src/Directory.Packages.props:20`), comment cite đúng `GHSA-3w5p-95mh-gq75`; commit `e6c22a7c` (bump 5.12.0→6.1.1) IS ancestor of HEAD → đã ship 2026-06-23 như 1 slice của `AqtCoreFW.Api` arch re-audit, **trước khi** row được mở để hành động.
- Cả 2 concern của row đã giải quyết: (a) "minimal patched without major" superseded — 5.x không có bản patched, 6.1.1 chính là minimal patched; (b) "major-bump drift risk" (`NCalc.Expression`/`EvaluateParameter`/`EvaluateFunction`) verify empirically non-breaking — build 0E, Forms.Infra 33/33 + Forms.App 317/317 green, 1 consumer `FormulaEvaluationService` + 4 API surface đều support v6.

## Action
Row rewrite in-place → `✅ CLOSED-STALE 2026-06-23` + evidence. **0 production code.** Commit `ac1c88ba` `docs(backlog)`, pushed origin+bitbucket.

## Lessons
- **Backlog-as-cache-invalidation:** row = snapshot, repo = source-of-truth. Khi phân kỳ, audit ground-truth (version-pin + commit-ancestor) rẻ hơn nhiều so với mở scope rồi mới phát hiện đã xong. Cùng họ với tránh stale-read trong distributed cache — đừng tin TTL (row date).
- Đây là lần INVARIANT 260522-A cứu false-action thứ-N trong tuần: row claim chưa-fix, công việc đã ship bởi 1 slice unrelated giữa author↔execute.

## Test status
N/A — docs-only stale-close, không chạm code. Bằng chứng version-pin + commit-ancestor authoritative.

## Next session
- 22 ships hôm nay; session đóng sạch (tree clean, all pushed).
- CLOSED-STALE row để in-place; sẽ gom archive ở optimization pass kế (cùng các `✅ RESOLVED` rows hôm nay) — chưa trigger (540L<600).
- Mở scope mới: ứng viên T3 đáng-plan nhất = `WF-TRANSITION-NOTIFY-UNCONFIGURED` (cần `/plan` trước: seed config + cross-module role-lookup).
