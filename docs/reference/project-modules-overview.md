# Báo cáo tổng quan dự án AqtCoreFW — Các module & mức độ hoàn thành

> **Ngày lập:** 2026-06-16 · **Cập nhật:** 2026-06-18 · **Phiên bản nền:** v2.137p119+ · **Phạm vi:** nhánh `main` (SQL Server)
> **Đối tượng:** quản lý / nghiệp vụ · **Nguồn dữ liệu:** `docs/codebase-summary*`, `docs/project-roadmap.md`, `docs/compliance-evidence-checklist.md`, `docs/project-changelog.md`, `backlogs/backlog.md`, mã nguồn `src/modules/` (16 module xác minh trực tiếp).
>
> **Cập nhật 2026-06-18:** module **Files** bổ sung luồng **xem + tải tài liệu đã ký trên UI** theo từng bước ký (Files + Forms, react-pdf viewer nhúng) — `/cook Phase A`. Xác minh mật mã chữ ký vẫn hoãn Phase 6 (xem mục 6).

---

## 1. Giới thiệu chung

**AqtCoreFW** là nền tảng lõi (modular monolith) trên .NET 10 + React 19, xây dựng cho các dự án **cơ quan nhà nước Việt Nam**. Kiến trúc Clean/Onion + CQRS + DDD, đa tổ chức (multi-tenant) với cách ly dữ liệu ở tầng cơ sở dữ liệu (Row-Level Security).

- **16 module nghiệp vụ** (backend), **375+ API endpoint**, **~169 thực thể dữ liệu**.
- **Bảo mật & tuân thủ là trục chính**: mã hóa PII, nhật ký bất biến chống giả mạo, phân loại dữ liệu 4 cấp, chữ ký số, xác thực đa yếu tố (MFA).
- **Khung pháp lý gắn kèm**: **QĐ 742** (an toàn thông tin cấp độ 4), **Luật 91/2025/QH15** (bảo vệ dữ liệu cá nhân — PDPL), **NĐ 53** (nhật ký an ninh mạng), **NĐ 59** (VNeID), **NĐ 68** (chữ ký số), **NĐ 85/TT 12** (kiểm thử ATTT trong CI/CD), chuẩn **OWASP**.

> **Lưu ý về "trạng thái":** dự án **không có chỉ số % hoàn thành chính thức** trong kho mã, nên báo cáo này dùng **nhãn trạng thái định tính** thay vì con số phần trăm (một con số % gộp lẫn "tính năng còn dở", "tính năng tương lai chưa làm" và "độ chắc chắn của người lập" sẽ gây hiểu nhầm). Định nghĩa nhãn ở **mục 5**. Phần lớn module headline được roadmap v2.137 tuyên bố **feature-complete**; các hạng mục còn mở được tách riêng ở mục 6.

---

## 2. Bảng tổng hợp module

| # | Module | Nhóm | Trạng thái | Hạng mục còn mở (nếu có) | Quy định chính |
|---|--------|------|------------|--------------------------|----------------|
| 1 | **Identity** (Định danh & phân quyền) | Bảo mật & danh tính | ✅ Production-ready | — | QĐ 742, Luật 91, NĐ 59 |
| 2 | **Audit** (Nhật ký & tuân thủ) | Bảo mật & danh tính | ✅ Production-ready | — | NĐ 53, Luật 91, QĐ 742 |
| 3 | **Files** (Tệp & chữ ký số) | Bảo mật & danh tính | ✅ Production-ready | Xác minh mật mã PDF đầy đủ (Phase 6) | NĐ 68, QĐ 742 |
| 4 | **Forms** (Biểu mẫu động) | Nghiệp vụ low-code | ✅ Production-ready | — | QĐ 742 (phân loại), NĐ 68 (ký) |
| 5 | **Workflow** (Quy trình/luồng xử lý) | Nghiệp vụ low-code | ✅ Production-ready | — | — (nền nghiệp vụ) |
| 6 | **BusinessProcess** (Gói quy trình) | Nghiệp vụ low-code | ✅ MVP (Production-ready) | Khôi phục hồ sơ mồ côi `P6-DEFER-1` (chờ nghiệp vụ) | — |
| 7 | **Notifications** (Thông báo) | Cộng tác & thông báo | ✅ Production-ready | — | — |
| 8 | **Collaboration** (Chat/Trao đổi) | Cộng tác & thông báo | 🟦 MVP | — | — |
| 9 | **AI** (Trợ lý/Trí tuệ nhân tạo) | AI & báo cáo | ✅ Production-ready (lõi) | — | QĐ 742 (chủ quyền dữ liệu) |
| 10 | **Reporting** (Báo cáo & KPI) | AI & báo cáo | ✅ Production-ready (lõi) | **Feature mới:** KPI Threshold & Alerting `T5-01` (~20d, chưa làm) | — |
| 11 | **Organization** (Cơ cấu tổ chức) | Dữ liệu & cấu hình | ✅ Production-ready | — | QĐ 742 (cấp độ mật theo đơn vị) |
| 12 | **MasterData** (Danh mục dùng chung) | Dữ liệu & cấu hình | ✅ Production-ready | — | — |
| 13 | **SystemParams** (Tham số hệ thống) | Dữ liệu & cấu hình | ✅ Production-ready | — | — |
| 14 | **Integration** (Tích hợp & cổng API) | Tích hợp & quy tắc | ✅ Production-ready | — | NĐ 85/TT 12 (gián tiếp) |
| 15 | **Rules** (Bộ quy tắc) | Tích hợp & quy tắc | ⚠️ Cần rà soát phạm vi | Mức độ tài liệu hóa thấp — cần xác minh trạng thái thực | — |
| 16 | **Search** (Tìm kiếm) | Nền tảng | ⛔ Đã gỡ khỏi UI (legacy) | — | — |

> **Phân biệt quan trọng:** "Hạng mục còn mở" tách làm 2 loại — **(a) tính năng dở của scope hiện tại** (vd. Files: xác minh PDF) vs **(b) tính năng MỚI chưa làm** (vd. Reporting: KPI-Alerting — đây là *thêm scope tương lai*, không phải module "chưa xong"). Module không có hạng mục mở = feature-complete theo roadmap v2.137.

---

## 3. Chi tiết từng module

### Nhóm A — Bảo mật & danh tính

#### 1. Identity — Định danh, phân quyền & đồng thuận
- **Dùng làm gì:** trung tâm xác thực (OIDC/OAuth2 qua OpenIddict) và phân quyền cho toàn hệ thống. Kết hợp 3 mô hình: RBAC (theo vai trò) + ABAC (theo thuộc tính) + Data Scope (theo phạm vi dữ liệu: bản thân/đơn vị/cây tổ chức/toàn bộ).
- **Usecase thực tế:**
  - Cán bộ đăng nhập có MFA (TOTP); tài khoản bị khóa tạm theo chính sách từng tổ chức sau N lần sai.
  - Quản trị cấp quyền theo nhóm, ủy quyền có thời hạn (delegation) — tự thu hồi khi hết hạn, chặn ủy quyền vòng.
  - Công dân **đồng thuận** (consent) cho xử lý dữ liệu cá nhân và có thể rút lại.
  - Liên thông định danh **VNeID**; định danh ký số gắn **CCCD** của người ký.
- **Gắn quy định:** QĐ 742 (mật khẩu ≥12 ký tự, khóa tài khoản §1.4, MFA, chống dò tài khoản theo OWASP A04/A07, rà soát quyền định kỳ, phân tách vai trò, quản lý phiên); Luật 91/2025 (đồng thuận, tối thiểu hóa dữ liệu); NĐ 59 (VNeID).

#### 2. Audit — Nhật ký bất biến & quyền được xóa
- **Dùng làm gì:** ghi nhật ký truy cập/thao tác dữ liệu nhạy cảm dưới dạng **chuỗi HMAC chỉ-ghi (append-only)** chống giả mạo; xử lý yêu cầu **Quyền được lãng quên (RTBF)**.
- **Usecase thực tế:**
  - Mọi thao tác nhạy cảm sinh một dòng nhật ký được ký móc xích; kiểm toán viên có thể **chứng minh không bị sửa** bằng kiểm tra chuỗi.
  - Công dân gửi yêu cầu xóa dữ liệu cá nhân → hệ thống ẩn danh PII và lưu vết kiểm toán (xử lý bất đồng bộ, trả 202).
  - Tự rà & ký các chuỗi nhật ký "ngủ đông" định kỳ để không tồn đọng (job quét chuỗi 5 phút/lần).
- **Gắn quy định:** NĐ 53 (nhật ký an ninh mạng, phát hiện giả mạo, lưu trữ); Luật 91/2025 (RTBF); QĐ 742 (phân loại dữ liệu trên mọi thực thể).

#### 3. Files — Lưu trữ tệp, quét virus & chữ ký số
- **Dùng làm gì:** lưu tệp đính kèm (đối tượng MinIO/S3), **quét virus** trước khi cho tải, mã hóa PII, và **ký số tài liệu**.
- **Usecase thực tế:**
  - Người dùng tải tệp lên → tệp ở trạng thái "cách ly/đang quét" → ClamAV quét; **fail-closed** (quét chưa xong/lỗi thì giữ cách ly + thử lại, không bao giờ cho tải tệp chưa quét).
  - Ký số văn bản qua **VGCA** (USB token, 3 tầng) hoặc **Viettel MySign** (3 chế độ: đồng bộ / poll / callback).
  - **(Mới 2026-06-18)** Sau khi ký, người dùng **xem + tải về** tài liệu đã ký ngay trên UI (Files + Forms); ký nhiều bước nhiều người → lưu + xem + tải file đã ký của **từng bước** (viewer react-pdf nhúng, có cảnh báo "chưa xác minh chữ ký").
- **Gắn quy định:** NĐ 68 (ký số & xác minh chữ ký); QĐ 742 Phase-05b (hạ tầng ký số VGCA/Viettel). *Xác minh mật mã đầy đủ cho PDF đã ký đang hoãn sang Phase 6.*

### Nhóm B — Nghiệp vụ low-code

#### 4. Forms — Trình tạo biểu mẫu động
- **Dùng làm gì:** quản trị tự thiết kế biểu mẫu kéo-thả với **32 loại trường** (text, ngày, file, chữ ký, bảng con, địa chỉ, công thức, tham chiếu…) mà không cần lập trình.
- **Usecase thực tế:**
  - Tạo biểu mẫu tiếp nhận hồ sơ, công bố (publish) → hệ thống tự sinh bảng dữ liệu/khung lưu trữ.
  - Trường điều kiện (ẩn/hiện theo logic), trường công thức (tính lúc đọc), trường tham chiếu danh mục/đơn vị khác.
  - Trường **chữ ký số** xác thực ngay khi nộp; quy trình duyệt 3 trạng thái (chờ → duyệt/từ chối).
- **Gắn quy định:** QĐ 742 (phân loại dữ liệu trường nhạy cảm); NĐ 68 (trường ký số).

#### 5. Workflow — Quy trình & luồng xử lý
- **Dùng làm gì:** máy trạng thái (state machine) cấu hình động cho luồng phê duyệt/xử lý; trình thiết kế trực quan **React Flow**.
- **Usecase thực tế:**
  - Thiết kế luồng nhiều bước (vẽ trạng thái + chuyển tiếp), điều kiện rẽ nhánh (11 toán tử), nhánh song song (AND/OR/N-of-M).
  - Giao việc theo 7 cơ chế (người cụ thể, người xử lý trước, theo quyền/vai trò/nhóm/đơn vị…), hộp thư công việc (inbox), nhận việc (claim), **ủy quyền có hạn**, cảnh báo SLA, thu hồi.
  - Bảng phân tích (analytics) 6 chỉ số; **di trú phiên bản quy trình** có phê duyệt 4-mắt.
- **Gắn quy định:** nền nghiệp vụ, không gắn quy định cụ thể (hỗ trợ thực thi các luồng tuân thủ).

#### 6. BusinessProcess — Gói quy trình
- **Dùng làm gì:** "đóng gói" Workflow + Forms thành một **quy trình nghiệp vụ** để người dùng chọn và khởi chạy; tạo đồng thời WorkflowInstance + FormSubmission một cách nguyên tử.
- **Usecase thực tế:** quản trị định nghĩa BP (chọn 1 quy trình + 1 biểu mẫu tiếp nhận); người dùng bấm "Tạo" → hệ thống dựng phiên xử lý + hồ sơ đầu vào cùng lúc.
- **Trạng thái:** MVP (P0–P8) đã ship; **khôi phục hồ sơ mồ côi (form nộp nhưng không gắn được workflow)** — các phase 04–07 đang **chờ quyết định nghiệp vụ** (cơ chế retry + UI khắc phục).

### Nhóm C — Cộng tác & thông báo

#### 7. Notifications — Thông báo đa kênh
- **Dùng làm gì:** gửi thông báo Email (MailKit), SMS (webhook), và **trong ứng dụng theo thời gian thực** (SignalR).
- **Usecase thực tế:** sự kiện nghiệp vụ (được giao việc, hồ sơ được duyệt, tài khoản bị khóa…) tự sinh thông báo theo **mẫu Liquid** đã thương hiệu hóa; nhận bất đồng bộ qua hàng đợi sự kiện.

#### 8. Collaboration — Trao đổi/Chat
- **Dùng làm gì:** nhắn tin thời gian thực giữa cán bộ (hội thoại nhóm/1-1, luồng trả lời, trạng thái đã đọc, hiện diện online).
- **Usecase thực tế:** trao đổi nhanh trong xử lý hồ sơ; thanh chat kiểu Telegram hiển thị tin chưa đọc, xem trước tin cuối.

### Nhóm D — Trí tuệ nhân tạo & báo cáo

#### 9. AI — Trợ lý & xử lý thông minh
- **Dùng làm gì:** tích hợp mô hình ngôn ngữ (LLM qua Ollama/Azure OpenAI), tìm kiếm ngữ nghĩa (vector Qdrant), **trích xuất tài liệu OCR**, tác tử ReAct.
- **Usecase thực tế:** trợ lý Copilot hỏi-đáp; trích xuất nội dung từ ảnh/scan; gắn cờ rủi ro tự động.
- **Gắn quy định:** QĐ 742 — **chủ quyền dữ liệu 4 tầng** (cách ly yêu cầu, mã hóa vector, ẩn danh metadata, ghi nhật ký), định tuyến theo quyền cho tài liệu mật.

#### 10. Reporting — Báo cáo & KPI
- **Dùng làm gì:** dashboard chỉ số, chạy báo cáo bất đồng bộ (Hangfire), xuất **Excel/PDF** theo mẫu (định dạng chuẩn cơ quan), kho truy vấn (query catalog) an toàn SQL.
- **Usecase thực tế:** xem dashboard KPI (cache 5 phút), chạy & tải báo cáo, xuất PDF tuân thủ có bằng chứng kiểm toán.
- **Trạng thái:** lõi đã ship; **tính năng chủ lực kế tiếp**: KPI Threshold & Alerting (cảnh báo theo ngưỡng KPI — `T5-01`, ~20 ngày, **đang chờ chốt hướng**).

### Nhóm E — Dữ liệu & cấu hình

#### 11. Organization — Cơ cấu tổ chức
- **Dùng làm gì:** quản lý cây tổ chức/đơn vị, gán **cấp độ mật** theo đơn vị, duyệt cây phân cấp.
- **Usecase thực tế:** dựng cơ cấu Bộ/Cục/Phòng nhiều cấp; data-scope phân quyền dựa trên cây này.
- **Gắn quy định:** QĐ 742 (gán cấp độ mật theo đơn vị).

#### 12. MasterData — Danh mục dùng chung
- **Dùng làm gì:** danh mục hành chính (Tỉnh/Huyện/Xã), các bảng enum, cache Redis.
- **Usecase thực tế:** chọn địa danh chuẩn trong biểu mẫu/hồ sơ; nguồn dữ liệu tham chiếu cho Forms.

#### 13. SystemParams — Tham số hệ thống
- **Dùng làm gì:** cấu hình toàn cục động (giới hạn tần suất, cờ tính năng, timeout) quản trị chỉnh được không cần deploy.
- **Usecase thực tế:** bật/tắt tính năng theo môi trường (vd. cờ quét virus), điều chỉnh tham số vận hành.

### Nhóm F — Tích hợp & quy tắc

#### 14. Integration — Tích hợp & cổng API
- **Dùng làm gì:** cổng API (YARP), **engine webhook** gọi hệ thống ngoài, xác thực máy-máy bằng API Key (SHA-256), danh mục mã lỗi chuẩn.
- **Usecase thực tế:** đẩy sự kiện sang hệ thống bên ngoài qua webhook; cấp API key cho tích hợp M2M.
- **Gắn quy định:** NĐ 85/TT 12 gián tiếp (chuẩn hóa biên tích hợp & mã lỗi).

#### 15. Rules — Bộ quy tắc
- **Dùng làm gì:** đánh giá biểu thức quy tắc nghiệp vụ (điều kiện động) phục vụ phân quyền/luồng.
- **Trạng thái:** ít tài liệu hóa → ước lượng dè dặt; cần rà soát phạm vi thực tế.

### Nhóm G — Nền tảng

#### 16. Search — Tìm kiếm (đã gỡ khỏi UI)
- **Hiện trạng:** menu tìm kiếm đã ẩn (v2.128p2); mã ở trạng thái legacy. Tìm kiếm toàn văn hiện do SQL Server FTS đảm nhận trong từng module. **Không phải hướng phát triển hiện tại.**

---

## 4. Tuân thủ pháp lý (ánh xạ quy định → module)

| Quy định | Yêu cầu chính | Module thực thi |
|----------|---------------|-----------------|
| **Luật 91/2025/QH15** (PDPL) | Đồng thuận dữ liệu cá nhân, Quyền được xóa (RTBF), tối thiểu hóa dữ liệu | Identity, Audit |
| **NĐ 53** | Nhật ký an ninh mạng bất biến (HMAC chỉ-ghi), phát hiện giả mạo, lưu trữ ≥3 năm | Audit, Files |
| **NĐ 59** | Liên thông định danh công dân VNeID | Identity / AuthServer |
| **NĐ 68** | Ký số tài liệu & xác minh chữ ký (VGCA/Viettel) | Files |
| **NĐ 85 / TT 12** | Kiểm thử ATTT trong CI/CD (SAST/DAST/SCA/SBOM) | Integration / CI-CD |
| **QĐ 742** (ATTT cấp độ 4) | Mật khẩu, khóa tài khoản, MFA, chống dò tài khoản, rà soát quyền, phân tách vai trò, **phân loại dữ liệu 4 cấp**, truy cập theo độ mật, quản lý phiên, **chủ quyền dữ liệu AI** | Identity, Audit, AI, SharedKernel |
| **OWASP Top 10** | Chống dò tài khoản (A04/A07), bảo mật đầu vào | AuthServer, toàn hệ thống |

> Hồ sơ bằng chứng tuân thủ chi tiết: `docs/compliance-evidence-checklist.md` và `docs/compliance/qd-742-cap-4/`. QĐ 742 đã ship **7/8 phase** (Phase-05b ký số shipt; bằng chứng dossier mục 5.1 = 40 MET + 1 PARTIAL — chờ seed chứng thư gốc NEAC).

---

## 5. Định nghĩa nhãn trạng thái

Báo cáo dùng **nhãn định tính** thay vì % (xem lý do ở mục 1). Ý nghĩa từng nhãn:

| Nhãn | Ý nghĩa |
|------|---------|
| ✅ **Production-ready** | Đã hoàn thiện tính năng theo tuyên bố roadmap v2.137; sẵn sàng dùng. Có thể vẫn còn polish/hardening thường xuyên (BAU) nhưng không có hạng mục dở được ghi nhận. |
| ✅ **Production-ready (lõi)** | Phần lõi production-ready; có thể có **tính năng MỚI** (mở rộng scope tương lai) đang chờ — ghi rõ ở cột "Hạng mục còn mở". |
| ✅ **MVP (Production-ready)** | Bản tối thiểu đã chạy được trên production; có hạng mục nâng cao đang chờ quyết định. |
| 🟦 **MVP** | Bản tối thiểu đã chạy; chưa được tuyên bố production-ready toàn diện. |
| ⚠️ **Cần rà soát phạm vi** | Báo cáo **chưa đủ dữ liệu** để khẳng định trạng thái (không phải module "kém") — cần đội ngũ xác minh. |
| ⛔ **Đã gỡ khỏi UI (legacy)** | Không còn trong giao diện/hướng phát triển hiện tại. |

**Tại sao không dùng %:** một con số phần trăm gộp lẫn ba thứ bản chất khác nhau — *(a)* tính năng còn dở của scope hiện tại, *(b)* tính năng mới chưa làm (scope tương lai), *(c)* độ chắc chắn của người lập báo cáo — nên dễ gây hiểu nhầm "module chưa xong" trong khi thực tế đã feature-complete.

**Khuyến nghị:** nếu cần thước đo định lượng chính thức, đội ngũ nên định nghĩa tiêu chí "hoàn thành" cho từng module (vd. chấm theo **73 use case Cockburn** trong `docs/srs/`) và đo theo checklist đó, thay vì một con số ước lượng.

---

## 6. Hạng mục đang mở / kế tiếp

| Hạng mục | Loại | Trạng thái |
|----------|------|------------|
| **KPI Threshold & Alerting** (`T5-01`, Reporting) | Tính năng chủ lực | Chờ chốt hướng + lập kế hoạch (~20d) |
| SDK NuGet (`T5-06`) | Quick-win | Sẵn sàng (~2d) |
| Khôi phục hồ sơ mồ côi Forms→Workflow (`P6-DEFER-1`) | Tính năng | Chờ quyết định nghiệp vụ (phase 04–07) |
| Giao việc đa-instance (`WF-MULTI-INSTANCE-ASSIGNMENT`) | Tính năng | Hoãn (cần thiết kế lại schema + ca nghiệp vụ) |
| Xem/tải PDF đã ký trên UI (per-step) | Tính năng | ✅ Đã ship 2026-06-18 (Files + Forms) |
| Ký số Phase 1+ (xác minh mật mã đầy đủ chữ ký) | Bảo mật | Phase 6 (sandbox) — gỡ được cảnh báo "chưa xác minh" sau khi xong |
| Hạ tầng CI/test | Nền tảng | **Parked** phase này (ưu tiên phát triển tính năng) |

---

*Báo cáo lập tự động từ tài liệu & mã nguồn dự án ngày 2026-06-16. Trạng thái dùng nhãn định tính (xem mục 5), không dùng % để tránh hiểu nhầm.*
