# Journal — Forms Phase-1 finalize: slice A ship + part B code-verdict (benchmark YAGNI)

**Ngày:** 2026-07-29 10:15 ICT · **Nhánh:** main · **Loại:** cook (finalize Phase-1) · **Plan:** `260728-1703-dynamic-forms-flagship-hardening`

## Session goal

Nối tiếp phiên 07-28 (slice A telemetry đã code, chưa finalize). Chuỗi: `/aqt-next` → finalize slice A → sync plan → cook Phase-1 part B (load-test baseline).

## What worked (verified)

**Finalize slice A (part A telemetry):**
- Diff verified bằng mắt (không tin claim journal) — 3 increment site đúng ngữ nghĩa, no `tenant` label, histogram bounded. Khớp commit.
- Forms unit suite: Application 344 / Domain 153 / Infrastructure 33 = **530 green, 0 fail**.
- Integration.Tests: **KHÔNG có verdict** — testcontainer SQL chết giữa chừng (CPU test-host đứng im 8 phút, DB container biến mất khỏi `docker ps -a`), test-host hang chờ connection. Kill theo quyết định user. Build clean nhưng test chưa chạy. Bỏ qua vì không cover code path mới (metric emission thuần Application; unit đã cover).
- Commit `86f3eca95` → pushed `origin/main` (cite `FORMS-HARDENING-PHASE1-LOADTEST`).

**Sync plan (docs-only):** sửa 2 drift aspirational-vs-shipped trong plan-01 — tags telemetry (`tenant` đã loại khỏi mọi instrument, `template_code` khỏi histogram — plan gốc ghi `tenant`) + vị trí increment (sau ValidateAsync, không phải "sau AddAsync line 170"). Nếu để nguyên, Phase-3 UX polish sẽ query metric theo label không tồn tại.

**Part B — GATE đóng bằng CODE-VERDICT, không benchmark (user YAGNI):**
- User chọn "micro-benchmark localdb". Đọc code thật → harness thực = ~1-1.5 ngày: apply full Forms migrations (gồm RLS `RestorePhase06RlsPolicies` + carve-out) + construct `DapperReadDbConnection` (4 dep: `IOptionsMonitor<DatabaseOptions>` + `ITenantContext` + `ICrossModuleUnitOfWork` + connString) + set SESSION_CONTEXT tenant khớp seed (nếu không RLS FILTER lọc 10k→0).
- User chốt **bỏ benchmark, đóng GATE bằng bằng chứng code**. Verdict: **Phase-2B streaming CẦN** — `ExportSubmissionsQueryHandler` buffer O(n) ba tầng (`.ToList()` 10k line 71 + `StringBuilder` CSV line 74 + `Encoding.UTF8.GetBytes` line 75, giữ đồng thời ~hàng chục MB cho 10k submission ~1-3KB JSON).
- Verdict report → `reports/baseline-load-test-numbers.md`. Phase-1 = DONE. Phase-2 unblocked.

## What didn't / lessons

- **"Micro-benchmark" không micro khi handler dùng `IReadDbConnection` + `ModuleDbContext` + RLS.** Ước 0.5 ngày, code thật cho thấy 1-1.5 ngày (RLS session-context + 4-dep construct + raw-SQL seed khớp cột). Đọc code TRƯỚC khi cam kết scope là cái cứu khỏi 1 ngày phí — bài học `feedback-obvious-seam-may-never-have-worked` biến thể (seam tồn tại nhưng đắt hơn nhiều so với vẻ ngoài).
- **Code trả lời được GATE định tính mà không cần đo.** GATE hỏi "streaming có cần không" — cấu trúc `.ToList()+StringBuilder+GetBytes` là O(n) ba tầng hiển nhiên, verdict "CÓ" chắc chắn mà không cần con số MB. Đo chính xác chỉ đáng khi cần ngưỡng để assert regression (dời Phase-5 before/after).
- **Testcontainer chết giữa chừng ≠ chất lượng code.** Phân biệt bằng: CPU đứng im + container biến mất = hạ tầng chết, không phải test chạy chậm. Không đọc exit-0-do-kill như "test passed".
- **PowerShell here-string trong Bash tool = commit subject hỏng.** `@'...'@` không parse trong bash → literal `@` dính subject. Fix: `git commit -F -` heredoc. (Đã có trong memory syntax-split.)

## Decisions made

- Slice A finalize dù integration no-verdict (unit đủ cover Application-only change).
- Part B: bỏ benchmark harness, đóng GATE bằng code-verdict (YAGNI).
- Phase-2B streaming: xác nhận CẦN (không còn conditional trên Phase-1 số đo).
- Số MB chính xác: dời Phase-5 (before/after streaming) nếu cần assert định lượng.

## Test status

- Forms unit suite 530 green (07-29). Integration no-verdict (testcontainer died). Không có code mới ở part B → không test.

## Next session

1. **HARD GATE re-weigh** (plan.md line 72-77): Forms Phase-2 vs 5 `[blocks-v1.0]` blockers — Forms còn #1 không? User-owned.
2. Nếu Phase-2 mở: 2A Formula sandbox (red-team H2 — bound work + ≥50 concurrency load-test), 2B export streaming (verdict CÓ), 2C descope security-note, 2D public-path verify.
3. Deferred: `/metrics-otel` live series verify (cần app-up); exact export MB (Phase-5).
