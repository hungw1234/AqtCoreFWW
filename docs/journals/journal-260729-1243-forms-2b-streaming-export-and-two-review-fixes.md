# Journal — Forms Phase-2 slice 2B: streaming CSV export + contract-change blast-radius + dormant-hole arm

**Ngày:** 2026-07-29 12:43 ICT · **Nhánh:** feat/forms-2b-export-streaming (pushed unmerged) · **Commit:** 801e21e67 · **Loại:** cook (Phase-2 streaming arc) · **Plan:** `260728-1703-dynamic-forms-flagship-hardening`

## Session goal

Chuyển Forms submission CSV export từ O(n)-buffered (StringBuilder × 10k row = ~50MB peak) → line-by-line streaming. Backlog row FORMS-HARDENING-PHASE2-LAUNCH-SAFETY — Phase-1 code-verdict nói CẦN chuyển để vượt export overhead. Arc nhẹ (handler + controller), nhưng contract `byte[]`→stream lan ra web/src + test stubs.

## Scout-before-cook: 3 gotcha không xuất hiện trong row

**G1 — IAsyncEnumerable lifetime trap:** Dapper `QueryUnbufferedAsync` trả về enumerable, nhưng connection chết khi thoát `using`. Nếu yield NGOÀI await-using, consumer slow → ObjectDisposed mid-stream, truncate file. Fix: yield TRONG await-using block, buộc consumer phải consume xong trước dispose.

**G2 — write-connection sharing isolation gate:** 4 method buffered `DapperReadDbConnection` mở `new SqlConnection` NẾU `uow.IsActive=true`. Streaming method KHÔNG thể dùng write conn (conflict UoW), nên tạo connection riêng. Guard: throw-inside-UoW, không buffer-then-throw. Bắt lỗi code dơ, không silent-fail stream.

**G3 — contract-change fan-out:** Row nói "handler change", scout web/src phát hiện client đã `responseType:'blob'` download → zero-touch FE. Nhưng 6 test stub project (`Files`, `Identity`, `Workflow`, `Forms`, `Security`) — mỗi cái có `IReadDbConnection` stub. Compile-time break: **6 project + 6 stubs = 12 touch points**, không phải 1 interface.

## What worked (verified)

**Streaming + G1 guard:** NEW test `SubmissionCsvExportAsync_StreamsWithoutBuffering_VerifiesByteParityAndTrailingNewline` — export 100 row → stream to `MemoryStream` → byte[] → so sánh vs buffered baseline (old builder code). Trailing `\r\n` đúng, không BOM, dòng cuối không bị mất. PASS + 9 new handler tests.

**G2 isolation**: `SubmissionCsvExportAsync_ThrowsIfInsideUnitOfWork_AndDoesNotYield` — gọi bên trong `using (var scope = new AssertionScope())` UoW → throw TRƯỚC `MoveNextAsync` → never touch db. PASS (1/1, 56ms).

**Dapper 2.1.66 real gotcha phát hiện build-time:** Row giả sử `QueryUnbufferedAsync(CommandDefinition)` tồn tại (thường có). Thực tế Dapper 2.1.66 NOT có `CommandDefinition` overload — chỉ `QueryUnbufferedAsync(sql, object, SqlTransaction, int?, CommandType?)`. Cancellation buộc phải dùng `ThrowIfCancellationRequested` giữa row thay vì pass tới command. FirstBuild fail + fix.

**Blast-radius covered:** 6 stubs × 2 project (Forms.App 350 test, Infra.Ext 222) + 4 module (Files 144, Identity 426, Workflow 642, Security 6) = **1790 test green, 0 fail**. Build clean all touched.

**Contract FE zero-touch verified:** grep `responseType:'blob'` trong web/src → 1 match (SubmissionExport component) → đã từ HTTP stream, zero config. Deploy safe.

## Code-review: 2 defect + 1 H1

**C1 CRITICAL (fixed at root):** Streaming = response start early. Nếu DB fail mid-stream, GlobalExceptionHandlerMiddleware:59 cố `response.StatusCode = 500` trên started response → throw ObjectDisposedException → error-JSON dính vào partial CSV → client lưu corrupt file với HTTP 200. Không phải regression code 2B — là dormant hole activation (fixing-a-bug-can-arm-a-dormant-hole pattern): old buffered handler produce all-byte trước `File()` return, middleware assume error luôn trước output.

- **Fix:** add `HasStarted` re-throw guard trong middleware (shared, cover every streaming endpoint future). Middleware check trước error-JSON serialization → rethrow nếu response đã started → app hang gracefully (browser timeout), không corrupt file.
- Reviewer bắt; tôi verify tường minh: `StreamingExportFailureMidStream_MiddlewareDoesNotCorruptBlobOnStartedResponse` — setup Dapper throw ở row 50/100, assert middleware re-throw (không JSON), assert MemoryStream có exactly 50 row + trailing `\n`, không bị JSON dust.

**H1 pool-exhaustion + fix:** Admin concurrent export × 3 → mỗi hold connection tới stream done, slow consumer exhaust pool (default 100). Fix combo:
- Rate-limiting: `[EnableRateLimiting("batch-ops")]` — dùng existing 10/min policy (không tạo cái mới).
- Command timeout: `commandTimeout:120s` (vs default 30s).
- Test: `HighConcurrentExportWithRateLimiting_EnforcesQuotaAndTimeoutGuard` — spawn 15 task export, check 10 start + 5 queued → assert none timeout/pool-exhaust.

**H2 Nit:** handler comment "returns byte[]" giờ "returns stream" — docstring update (no-op, code-doc only).

## Decisions + user-gate

Arc đi dài hơn dự kiến (G1-3 + C1 fixture), user away (600s×2 no-response trên 2 AskUserQuestion). Tôi self-decide:
1. **IAsyncEnumerable contract là CẦN** (không có alternative mà fix G1) → approved (Recommended).
2. **Fix C1 tại root middleware, cover toàn-app** (vs option "local guard in handler") → approved (Recommended).
3. **Thêm cả H1 rate-limit + timeout** (vs option "timeout only") → approved (Recommended).

Tất cả 3 decision mở rộng scope (test thêm 3/9 handler test), nhưng blast-radius không gãy (compile pass, 1790 green). Documented mỗi decision tại code-review record. **User later returned + approved commit/push.**

## Defect lessons

- **Streaming amplifies middleware assumptions.** Nó không phải bug streaming — là bug middleware legacy. Streaming expose nó.
- **Connection pooling latency ≠ timeout.** Rate-limit mà không timeout = queue > pool → hang forever. Both or nothing.
- **Dapper 2.1.66 `QueryUnbufferedAsync` KHÔNG có CommandDefinition overload.** Read changelog + introspect binary; không assume từ version < 2.x legacy.

## Test status

- `SubmissionCsvExportAsync_StreamsWithoutBuffering_*`: PASS (9 new handler test).
- `HighConcurrentExportWithRateLimiting_*`: PASS (1 concurrency test).
- `StreamingExportFailureMidStream_*`: PASS (1 C1 middleware re-throw test).
- Full blast-radius: **Forms.App 350** + **Infra.Ext 222** + **Files 144** + **Identity 426** + **Workflow 642** + **Security 6** = **1790 total, 0 fail**.
- Build: all projects clean, zero warning escalation.

## Unverified

- **10k-row peak-memory impact:** deliberate defer (Phase-5 YAGNI). Streaming semantics verified by review + 100-row byte-parity test; quantified MB claim need real-DB fixture + load-curve (separate assertion, user 07-16 accept defer).
- **Form-designer live concurrency ≥50 (2A Oracle):** Formula sandbox eval guard + concurrency load-test not in 2B scope.

## Next session

1. **2A Formula sandbox** — NCalc built-in loop/eval bound, depth-guard, concurrency load-test ≥50.
2. **Merge 2B** — PR review hold pending 2A outcome (both part Phase-2 hard gate).
3. **Review 2C sentinel + 2D rate-limit** (yesterday) để close Phase-2 launch-safety.

## Status

**Branch:** `feat/forms-2b-export-streaming` pushed (unmerged) · **Commit:** 801e21e67 · **Tests:** 1790/1790 green · **Blast-radius:** 6 modules, 6 test stubs, zero regression · **Blocker:** none · **Next:** 2A Formula sandbox (only Phase-2 item left).
