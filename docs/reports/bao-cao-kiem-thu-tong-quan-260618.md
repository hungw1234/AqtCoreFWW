# Báo cáo Kiểm thử Tổng quan — AqtCoreFW

> **Ngày lập:** 2026-06-18 · **Phạm vi:** toàn repository · **Cơ sở số liệu:** đo trực tiếp trên mã nguồn (`grep`/`find` trên thư mục `tests/` và `web/`), KHÔNG lấy từ tài liệu cũ.
> **Lưu ý trung thực:** Báo cáo này **đếm** số lượng case và **ghi nhận** kết quả các lần chạy gần nhất. Nó **không** đại diện cho một lần chạy full-suite mới tại thời điểm lập (xem §5).

---

## 1. Tóm tắt điều hành

| Chỉ số | Giá trị (đo thực tế) |
|--------|----------------------|
| Loại test | **8** (Unit, Integration, Architecture, Contracts, Regression, FE component/wire, E2E, Performance) + 1 thủ công (Pentest) + 1 quality-gate |
| Test project (.NET xUnit) | **66** (38 unit + 24 integration + architecture + contracts + regression + shared fixtures) |
| Case BE (.NET) | **4,315** test methods (4,206 `[Fact]` + 109 `[Theory]`) → **~4,650** case thực thi khi tính `[InlineData]` (×444) |
| Case FE đơn vị/thành phần (Vitest) | **1,056** (`it/test`) trong 129 file (+11 `.each`) |
| Case E2E (Playwright) | **854** trong 124 spec (gồm `@smoke`) |
| Kịch bản tải (k6) | **21** script |
| **Tổng case tự động (xấp xỉ)** | **~6,560** + 21 kịch bản tải |
| Pentest | Thủ công theo NĐ85/TT12 (checklist + template, không tự động) |

---

## 2. Phân loại test & số lượng (chi tiết)

### 2.1 Backend — .NET / xUnit
Đo: `grep -rhoE "^\s*\[(Fact|Theory)" tests --include=*.cs`

| Nhóm | Vị trí | Project | Case (methods) | Cách test |
|------|--------|---------|----------------|-----------|
| **Unit** | `tests/unit/` | 38 | **3,467** | xUnit + NSubstitute, in-memory; kiểm Domain/Application/Infrastructure logic của từng module |
| **Integration** | `tests/integration/` | 24 | **688** | xUnit + SQL Server + MinIO **thật** (qua `WebAppFixture`, chạy login `db_owner` → mọi predicate phải tự scope `TenantId`) |
| **Architecture** | `tests/architecture/` | 1 | **110** | NetArchTest — khoá quy tắc layering, dependency, encrypted-field guard, route convention |
| **Contracts** | `tests/contracts/` | 1 | **38** | Pin hình dạng contract cross-module |
| **Regression** | `tests/regression/` | 1 | **12** | Bẫy lỗi cụ thể đã từng xảy ra |
| **BE cộng dồn** | | **66 proj / 626 file** | **4,315 methods** | (Theory mở rộng ~444 `[InlineData]` ⇒ ~4,650 case) |

Các integration suite chuyên biệt (nằm trong 24 project trên), đáng tên riêng:
`Security`, `TenantIsolation`, `Concurrency`, `Resilience`, `Startup.SmokeTests`, `Installation`, `Messaging`, `Cache`, `BackgroundJobs`.

### 2.2 Frontend — Vitest (đơn vị / thành phần / wire-contract)
Đo: `find web/src -name '*.test.ts*'` + `grep it/test`

| Chỉ số | Giá trị |
|--------|---------|
| File test | **129** (`web/src/test/**`) |
| Case (`it`/`test`) | **1,056** (+11 `it.each`/`test.each`) |
| Công cụ | Vitest + Testing Library + jsdom |
| Đặc thù | **wire-contract tests** pin chính xác URL/body ở ranh giới serialize (chống lớp lỗi "false-green" đã lặp lại trong dự án) |

### 2.3 End-to-End — Playwright
Đo: `web/e2e/*.spec.ts` (đã loại trừ bản sao trong `.claude/worktrees/` và `web/coverage/`)

| Chỉ số | Giá trị |
|--------|---------|
| Spec | **124** |
| Case | **854** (gồm các spec gắn `@smoke`) |
| Công cụ | Playwright (trình duyệt thật, cần stack UP) |

### 2.4 Performance / Load — k6
| Chỉ số | Giá trị |
|--------|---------|
| Script | **21** (`tests/performance/*.js`) |
| Bao phủ | db-performance, connection-pool, per-module load (ai/audit/collaboration/files/forms…) |

### 2.5 Pentest (thủ công — tuân thủ pháp lý VN)
- `tests/pentest/` — checklist **NĐ85/TT12** + template SAST/DAST + báo cáo pentest.
- **Không tự động**; là hồ sơ tuân thủ, chạy thủ công theo quy trình.

### 2.6 Quality-gate
- `tests/quality-gates/check-coverage.sh` — script kiểm ngưỡng coverage.

---

## 3. Đã test như thế nào (chiến lược)

1. **Test pyramid chuẩn:** đáy là unit (~75% case) → integration (persistence thật) → E2E mỏng. Phù hợp kiến trúc modular monolith.
2. **Integration chạy DB thật, không InMemory** cho các path nhạy cảm → bắt được lớp lỗi mà EF InMemory che giấu (vd: filtered-index `NOT IN`, thiếu `.Include`, casing serialize).
3. **db_owner bypass RLS** trong integration → buộc mọi truy vấn tự scope `TenantId` tường minh; đây là cơ chế phòng cross-tenant leak.
4. **Architecture tests (NetArchTest)** khoá layering + encrypted-field + route convention → drift làm **fail build**, không chỉ fail test.
5. **Wire-contract tests (FE + arch route tests BE)** pin contract tại ranh giới THẬT (URL/body/route), không mock một tầng phía trên → chống "false-green".
6. **Pentest thủ công** theo NĐ85/TT12 cho yêu cầu tuân thủ.
7. **Lệnh chạy chuẩn:**
   - BE: `dotnet test` (toàn solution) hoặc `--filter "FullyQualifiedName~<Class>"` cho subset. **Phải kill Auth(5002)+API(5001) host trước** (lock DLL khi rebuild).
   - FE đơn vị: `pnpm test` (Vitest) trong `web/`.
   - E2E: `pnpm playwright test` (cần stack UP).
   - Coverage gate: `tests/quality-gates/check-coverage.sh`.

---

## 4. Kết quả (trung thực)

### 4.1 Đã chạy & xanh trong phiên gần nhất (2026-06-18, fix signing multi-sign history)
| Suite | Kết quả |
|-------|---------|
| Identity Integration (`SigningStepReadSurfaceTests` + liên quan) | **13/13 PASS** |
| Files.Application Unit (`DownloadSignedStep` + `GetFileSigningSteps`) | **11/11 PASS** |
| FE broad (digital-signature + files + forms) | **332/332 PASS** (36 file) |
| API build | **0 Error / 0 Warning** |

### 4.2 Kết quả "full" theo từng slice (point-in-time, trích changelog — KHÔNG phải 1 lần chạy full mới)
| Mốc | Kết quả ghi nhận |
|-----|------------------|
| Phase 2c-A (2026-06-17) | BE **793/793** · FE **1,132/1,132** |
| Phase 2b-3 (2026-06-17) | Forms.Integration **21/21** · Identity.Integration **269/269** |
| Phase A viewer fix (2026-06-18) | digital-signature **120/120** · tsc 0 · eslint 0 |
| Architecture (gần nhất) | **110/110** |

### 4.3 Điều CHƯA xác minh trong phiên này
- **Chưa có một lần chạy full-suite (~6,560 case) mới** → không thể tuyên bố "tất cả pass ngay lúc này".
- **Coverage % thực chưa đo** (có script nhưng chưa chạy).

---

## 5. Rủi ro & phát hiện

| Mức | Phát hiện |
|-----|-----------|
| 🔴 Cao | **Tài liệu test-count đã stale ~3×:** `docs/codebase-summary.md` ghi "2,234+ / 1,808 BE / 559 FE" — thực tế **4,315 BE methods / 1,056 FE / 854 E2E**. Cần cập nhật. |
| 🟡 TB | **Đếm E2E dễ sai:** `find` toàn repo ra 614 spec vì 4 bản sao trong `.claude/worktrees/` + `web/coverage`. Số sạch dưới `web/e2e` = **124 spec / 854 case**. Luôn filter worktree khi báo cáo. |
| 🟡 TB | **Coverage % không rõ** — chưa chạy `check-coverage.sh`; không có con số thực để cam kết ngưỡng. |
| 🟢 Thấp | Pentest thủ công → phụ thuộc quy trình con người, không có bằng chứng tự động trong CI. |

---

## 6. Khuyến nghị (nếu cần "kết quả cứng")

1. Chạy **full BE suite** (`dotnet test` toàn solution; kill Auth+API host trước) → pass/fail thật của 4,315 methods.
2. Chạy **full FE** (Vitest 1,056 + Playwright 854; E2E cần stack UP).
3. Chạy **coverage gate** → % thực.
4. Cập nhật `docs/codebase-summary.md` về số liệu thực (sửa drift ở §5).

---

## Phụ lục A — Lệnh đo đã dùng (tái lập)

```bash
# BE: số test method
grep -rhoE "^\s*\[Fact"   tests --include=*.cs | wc -l   # 4206
grep -rhoE "^\s*\[Theory" tests --include=*.cs | wc -l   # 109
grep -rhoE "\[InlineData" tests --include=*.cs | wc -l   # 444

# BE: theo nhóm
grep -rhoE "^\s*\[(Fact|Theory)" tests/unit --include=*.cs | wc -l          # 3467
grep -rhoE "^\s*\[(Fact|Theory)" tests/integration --include=*.cs | wc -l   # 688

# FE Vitest
find web/src -name '*.test.ts' -o -name '*.test.tsx' | wc -l                 # 129
grep -rhoE "\b(it|test)\s*\(" web/src --include=*.test.ts --include=*.test.tsx | wc -l  # 1056

# E2E (sạch, chỉ web/e2e)
find web/e2e -name '*.spec.ts' | wc -l                                       # 124
grep -rhoE "\b(test|it)\s*\(" web/e2e --include=*.spec.ts | wc -l            # 854

# k6
find tests/performance -name '*.js' | wc -l                                  # 21
```

> Số liệu trong báo cáo là **snapshot 2026-06-18**; sẽ trôi theo mỗi lần thêm test. Chạy lại Phụ lục A để cập nhật.
