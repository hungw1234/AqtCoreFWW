# Journal — Dynamic Forms Flagship Hardening: advise → hard-plan → cook slice A

**Ngày:** 2026-07-28 19:09 ICT · **Nhánh:** main · **Loại:** advise + plan + cook (Phase-1 phần A)

## Session goal
User: "nâng cấp và làm mịn module biểu mẫu động". Chạy chuỗi `/ak-advise` → `/ak-plan --hard` → `/ak-cook` Phase 1.

## What worked (verified)

**Advise:** Scout BE+FE xác nhận Forms đã trưởng thành (BE 14.7k LOC, FE 16 field types, 0 TODO). User chốt qua phỏng vấn: Forms = **cốt lõi dùng nặng** v1.0 → rủi ro scout nêu là THẬT; đầu tư "toàn diện" 3-4 tuần; thứ tự **an toàn → UX → feature**; gate-per-tuần. Report `advise-260728-1644-*`.

**Plan (--hard):** 2 researcher song song + red-team. Plan 5 phase gated tại `plans/260728-1703-dynamic-forms-flagship-hardening/`. **Red-team cứu 2 lỗi High CONFIRMED** làm sập chính mục tiêu launch-safety:
- **H1**: `IIdentitySigningClient` là in-process (`IdentityInfrastructureRegistration.cs:438`), OCSP chưa wired (`IOcspClient` 0 DI) → Polly wrap vào hư không, "slow-CA hang" KHÔNG tồn tại. → 2C descope (user chốt: v1.0 không cần OCSP).
- **H2**: `Task.Run+CancellationToken` chỉ ABANDON không ABORT NCalc CPU-bound → threadpool starvation ẩn dưới concurrency; load-test single-shot false-green. → 2A viết lại: bound work + load-test concurrency.
- M1 orphan double-create + entity thiếu domain mutator (không compile); M2 QueryUnbufferedAsync không có trên IReadDbConnection; M3 gate mơ hồ → tất cả fold vào plan. Consistency gate PASS.

**Cook Phase-1 phần A (telemetry) — DONE, reviewed, build-verified:**
- `FormMetrics.cs` (mirror `AuditMetrics.cs`): 2 Counter + 1 Histogram, meter `AqtCoreFW.Forms`.
- Meter đăng ký `OpenTelemetryConfiguration.cs` (sau Audit).
- 3 increment site: `SubmitFormCommandHandler`, `SubmitPublicFormCommandHandler` (`is_public=true`), `GetPublicFormQueryHandler` (`FormOpened`).
- Code-review: 0 High, 2 Med, 2 Low — **tất cả fix**. M1+M2 (cardinality + PII-latent): bỏ `tenant` khỏi mọi label (one-tenant-per-deployment) + bỏ `template_code` khỏi histogram (bucket × user-code = series explosion). L1/L2: doc sửa.
- Build: Forms.Application 0 error (2 pre-existing NU1902 AngleSharp), Infrastructure 0W/0E.

## What didn't / lessons
- **Metric placement là quyết định ngữ nghĩa, không chỉ vị trí code**: `FormSubmitted` đặt sau `ValidateAsync` trước reject-return → đếm cả submit reject nhưng BỎ SÓT submit chết ở consent/signature (trước đó). Code-review L1 bắt đúng → completion-rate = "reach validation-stage", không phải raw intent. Doc đã ghi rõ.
- **`tenant` label = bẫy cardinality + PII**: cùng class rò rỉ mà trace pipeline đã strip `db.statement`. Forms suýt là module đầu tiên đưa Guid tenant + user-code vào Prometheus label. one-tenant-per-cluster → label thừa.
- Load-test single-process KHÔNG phải oracle cho DoS threadpool (red-team H2) — phải test dưới concurrency.

## Decisions made
- Formula compute CẦN có → sandbox là tiền đề bắt buộc (không ship compute unsandboxed).
- OCSP KHÔNG cần v1.0 → verify offline PKIX, 2C descope.
- Orphan-retry = auto Hangfire job; UX metric = completion-rate + validation-errors/submit BE-only; gate-per-phase.
- Cook dừng ở ranh giới sau slice A khi user away — KHÔNG tự boot full stack để chạy load-test phần B (rủi ro block/debug khi không có user quyết).

## Test status
- Build: Forms.Application + Infrastructure 0 error. Forms test suite CHƯA chạy lại (finalize gate chưa qua). Slice A chưa có test driving FormMetrics (YAGNI cho metrics slice, code-review chấp nhận).

## Next session
1. **Phase-1 phần B (load-test)** — user chọn cách: (1) script+runbook [khuyến nghị] / (2) full aqt-runapp / (3) micro-benchmark localdb. Cần app-up để verify metric trên `/metrics-otel`.
2. Chạy Forms test suite + commit slice A (finalize gate — cần user duyệt).
3. Phase 2 (launch-safety) — 2A Formula sandbox viết-lại theo red-team H2 (bound work, load-test concurrency); 2C descope + `OcspChecked=false` test; 2B export streaming (conditional); 2D public-path verify.
4. HARD GATE sau Phase 2: tiêu chí cụ thể trong plan.md.
