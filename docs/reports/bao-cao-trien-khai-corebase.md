# BÁO CÁO TRIỂN KHAI DỰ ÁN CORE FRAMEWORK (AqtCoreFW)

> **Trình:** Ban lãnh đạo · **Đơn vị thực hiện:** Công ty Aequitas — Khối GOV · **Ngày báo cáo:** 18/06/2026
> **Tham chiếu:** Đề xuất triển khai Core Framework v1.0 (16/03/2026) · Báo cáo module `docs/reference/project-modules-overview.md`
> **Phạm vi nền tảng:** nhánh `main`, .NET 10 + React 19, SQL Server (multi-tenant, RLS)

---

## 1. Tóm tắt cho lãnh đạo (Executive Summary)

Dự án **AqtCoreFW** — nền tảng backend .NET dùng chung cho Khối GOV — đã hoàn thành giai đoạn xây dựng nền tảng và **vượt phạm vi đề xuất ban đầu**:

- **Đề xuất ban đầu:** 7 module lõi + các năng lực mở rộng, ước tính **439 giờ** (không có AI) hoặc **258 giờ** (senior solo + AI support).
- **Thực tế bàn giao:** **16 module nghiệp vụ**, **375+ API endpoint**, **~169 thực thể dữ liệu**, kèm khung tuân thủ pháp lý sâu (QĐ 742 cấp độ 4, Luật 91/2025 PDPL, chữ ký số, AI chủ quyền dữ liệu).
- **Tổng số giờ thực hiện trên Jira: 304 giờ.**

**Kết luận chính:** với **304 giờ**, đội ngũ đã bàn giao **gấp ~2,3 lần số module** so với đề xuất gốc (16 vs 7) nhưng vẫn **thấp hơn 135 giờ** so với phương án baseline không dùng AI (439 giờ) cho phạm vi nhỏ hơn. Đây là minh chứng định lượng cho hiệu quả của việc **áp dụng AI có kiểm soát** trong xây dựng tài sản kỹ thuật dùng chung.

---

## 2. Thông tin chung về đề xuất

| Hạng mục | Nội dung |
|----------|----------|
| Tên | Xây dựng AqtCoreFW GOV .NET Backend Framework |
| Đơn vị đề xuất | Khối GOV |
| Chủ đầu tư / Đơn vị sử dụng | Công ty Aequitas |
| Phiên bản đề xuất | 1.0 — ngày ban hành 16/03/2026 |
| Mô hình thực hiện | Senior solo + AI support (Claude Code Max 20x, 2 tháng) |
| Đơn giá nhân công | 250.000 VNĐ/giờ |

**Mục tiêu đề xuất:** hình thành một *production-ready blueprint* cho các dự án GOV backend .NET theo nguyên lý **zero-reinvention** (tái sử dụng thay vì phát triển lặp lại), chuẩn hóa 7 module lõi + năng lực mở rộng, chuyển thời gian khởi tạo nền từ "vài tháng" xuống "vài phút" và giảm trên 200 giờ phát triển lặp lại cho mỗi dự án kế tiếp.

**7 module lõi theo đề xuất:** Identity, Audit, Cases, Files, Notifications, Integration, Master Data.

**Phân rã WBS đề xuất (439 giờ baseline):**

| Nhóm công việc | Số task | Effort (giờ) | Đầu ra chính |
|----------------|:-------:|:------------:|--------------|
| Foundation | 13 | 82 | Scaffold solution, SharedKernel, API standards, test infra, multi-tenancy |
| Identity | 7 | 52 | OpenIddict, MFA, RBAC/ABAC, delegation, access review, consent |
| Core Services | 23 | 105 | Audit, gateway, rate limit, jobs, messaging, webhook, cache, CLI scaffold |
| Core Modules | 16 | 68 | Cases, notifications, master data, feature flags, org/file, ký số stub |
| Advanced Modules | 17 | 91 | Reporting, dashboard, workflow, dynamic forms, AI/RAG, OCR, export |
| Operations | 9 | 41 | CI/CD, security scan, blue-green, runbook, test stack, tài liệu |
| **Tổng** | **85** | **439** | |

---

## 3. Các module & tính năng hiện có trên CoreBase

Nền tảng đã đạt **16 module** (so với 7 lõi đề xuất). Khái niệm "Cases" trong đề xuất được hiện thực hóa và mở rộng thành bộ ba **Workflow + Forms + BusinessProcess**.

### 3.1. Bảng tổng hợp module

| # | Module | Nhóm | Trạng thái |
|---|--------|------|------------|
| 1 | **Identity** — Định danh, phân quyền (RBAC+ABAC+DataScope), MFA, VNeID, đồng thuận | Bảo mật & danh tính | ✅ Production-ready |
| 2 | **Audit** — Nhật ký bất biến HMAC chuỗi, Quyền được xóa (RTBF) | Bảo mật & danh tính | ✅ Production-ready |
| 3 | **Files** — Lưu trữ, quét virus, ký số VGCA/Viettel, **xem/tải PDF đã ký** | Bảo mật & danh tính | ✅ Production-ready |
| 4 | **Forms** — Biểu mẫu động kéo-thả (32 loại trường) | Nghiệp vụ low-code | ✅ Production-ready |
| 5 | **Workflow** — Máy trạng thái cấu hình động, trình thiết kế trực quan | Nghiệp vụ low-code | ✅ Production-ready |
| 6 | **BusinessProcess** — Đóng gói Workflow + Forms thành quy trình | Nghiệp vụ low-code | ✅ MVP (Production-ready) |
| 7 | **Notifications** — Email / SMS / thời gian thực (SignalR) | Cộng tác & thông báo | ✅ Production-ready |
| 8 | **Collaboration** — Chat/trao đổi thời gian thực | Cộng tác & thông báo | 🟦 MVP |
| 9 | **AI** — LLM (Ollama/Azure), tìm kiếm ngữ nghĩa, OCR, tác tử ReAct | AI & báo cáo | ✅ Production-ready (lõi) |
| 10 | **Reporting** — Dashboard KPI, báo cáo bất đồng bộ, xuất Excel/PDF | AI & báo cáo | ✅ Production-ready (lõi) |
| 11 | **Organization** — Cây tổ chức, cấp độ mật theo đơn vị | Dữ liệu & cấu hình | ✅ Production-ready |
| 12 | **MasterData** — Danh mục dùng chung (Tỉnh/Huyện/Xã, enum) | Dữ liệu & cấu hình | ✅ Production-ready |
| 13 | **SystemParams** — Tham số hệ thống động, feature flags | Dữ liệu & cấu hình | ✅ Production-ready |
| 14 | **Integration** — Cổng API (YARP), webhook engine, API key M2M | Tích hợp & quy tắc | ✅ Production-ready |
| 15 | **Rules** — Đánh giá biểu thức quy tắc nghiệp vụ | Tích hợp & quy tắc | ⚠️ Cần rà soát phạm vi |
| 16 | **Search** — Tìm kiếm (đã chuyển sang SQL FTS theo module) | Nền tảng | ⛔ Legacy (đã gỡ khỏi UI) |

> Chi tiết từng module + use case thực tế: `docs/reference/project-modules-overview.md`.

### 3.2. Năng lực nền tảng xuyên suốt (cross-cutting)

- **Kiến trúc:** Clean/Onion + CQRS + DDD; EF Core (ghi) + Dapper (đọc); abstraction `IDbProvider` (lộ trình PostgreSQL/Oracle); sẵn sàng tách microservices (strangler pattern).
- **Đa tổ chức (multi-tenant):** cách ly dữ liệu ở tầng CSDL bằng **Row-Level Security**.
- **DevSecOps:** SAST/DAST/SCA-SBOM trong pipeline, signing, blue-green deployment, runbook, test pyramid (unit + architecture + integration Testcontainers + load k6 + security).
- **DX:** CLI scaffold sinh module theo chuẩn, i18n vi/en, chuẩn lỗi/API thống nhất, tài liệu onboarding.

### 3.3. Tuân thủ pháp lý (compliance-by-design)

| Quy định | Năng lực thực thi trên CoreBase |
|----------|---------------------------------|
| **QĐ 742** (ATTT cấp độ 4) | Mật khẩu ≥12 ký tự, khóa tài khoản, MFA, chống dò tài khoản, phân loại dữ liệu 4 cấp, chủ quyền dữ liệu AI — **ship 7/8 phase** (40 MET + 1 PARTIAL) |
| **Luật 91/2025/QH15** (PDPL) | Đồng thuận dữ liệu cá nhân, Quyền được xóa (RTBF), tối thiểu hóa dữ liệu |
| **NĐ 53** | Nhật ký an ninh mạng bất biến (HMAC chỉ-ghi), phát hiện giả mạo |
| **NĐ 59** | Liên thông định danh VNeID |
| **NĐ 68** | Ký số tài liệu & xác minh chữ ký (VGCA/Viettel) |
| **NĐ 85 / TT 12** | Kiểm thử ATTT trong CI/CD (SAST/DAST/SCA/SBOM) |
| **OWASP Top 10** | Chống dò tài khoản (A04/A07), bảo mật đầu vào |

---

## 4. Giá trị mang lại

1. **Tài sản kỹ thuật dùng chung** — một code base chuẩn, clone-and-go cho mọi dự án GOV kế tiếp; rút ngắn thời gian khởi tạo nền từ "vài tháng" xuống "vài phút" (CLI scaffold + 16 module + extension point).
2. **Tuân thủ pháp lý sẵn sàng từ thiết kế** — audit chống sửa đổi, sinh bằng chứng kiểm toán, phân loại dữ liệu, ký số — giúp các dự án GOV qua kiểm định ATTT cấp độ 3–4 nhanh, không phải làm lại từ đầu.
3. **Giảm chi phí phát triển lặp lại** — mỗi dự án kế tiếp tái sử dụng 16 module thay vì viết lại; tiết kiệm hàng trăm giờ/dự án (đề xuất ước tính >200 giờ/dự án).
4. **Sẵn sàng cho quy mô lớn** — kiến trúc stateless, scale-out ngang, mục tiêu 1.000–3.000 người dùng đồng thời, p95 ≤ 2–5 giây, sẵn sàng AI-native + enterprise integration.
5. **Đồng nhất chất lượng** — chuẩn kỹ thuật, test pyramid, security gate áp dụng thống nhất giữa các dự án.

---

## 5. Hiệu quả khi áp dụng AI

Mô hình triển khai: **một kỹ sư senior + AI support (Claude Code Max 20x)**, AI chỉ tăng tốc tác vụ lặp lại — mọi kết quả đều qua **code review + test + security gate**, quyết định thiết kế và phê duyệt cuối do kỹ sư phụ trách.

### 5.1. So sánh giờ thực hiện

| Chỉ tiêu | Đề xuất — không AI | Đề xuất — senior solo + AI | **Thực tế (Jira)** |
|----------|:------------------:|:--------------------------:|:------------------:|
| Tổng giờ nhân công | 439 giờ | 258 giờ | **304 giờ** |
| Phạm vi bàn giao | 7 module lõi + mở rộng | 7 module lõi + mở rộng | **16 module + tuân thủ sâu** |
| Chi phí nhân công (250k/giờ) | 109.750.000 VNĐ | 64.500.000 VNĐ | **76.000.000 VNĐ** |
| Chi phí AI support | — | 10.400.000 VNĐ | 10.400.000 VNĐ |

### 5.2. Phân tích hiệu quả (trung thực)

- **So với baseline không AI (439 giờ):** thực tế **304 giờ** → **tiết kiệm 135 giờ (~30,8%)**, tương đương **33,75 triệu VNĐ** nhân công. Sau khi trừ chi phí AI 10,4 triệu, **tiết kiệm ròng ~23,35 triệu VNĐ**; **ROI khoản đầu tư AI ≈ 324%**.
- **Đòn bẩy phạm vi:** 304 giờ đã bàn giao **16 module** (gấp ~2,3 lần con số 7 module của đề xuất) — tức **năng suất trên mỗi module tăng đáng kể**, không chỉ là rút ngắn thời gian cho cùng một khối lượng.
- **Lưu ý minh bạch:** con số thực tế 304 giờ **cao hơn ước tính lạc quan 258 giờ** của phương án AI khoảng 46 giờ. Nguyên nhân hợp lý: **phạm vi thực tế mở rộng** (16 vs 7 module) và **chiều sâu tuân thủ** (QĐ 742 7/8 phase, PDPL, chuỗi ký số USB/remote, AI chủ quyền dữ liệu) vượt khung blueprint ban đầu. Vì vậy hiệu quả AI nên được đánh giá theo **giá trị bàn giao** chứ không chỉ theo số giờ tuyệt đối.

### 5.3. Hiệu quả định tính của AI

- Tăng tốc tác vụ lặp lại: scaffold module, sinh test, chuẩn hóa tài liệu, rà soát nhanh.
- Giữ chất lượng nhờ quy trình bắt buộc: AI đề xuất → kỹ sư review → test + security gate → mới hợp nhất.
- Cho phép một kỹ sư senior bao quát phạm vi lớn (16 module) mà mô hình truyền thống thường cần nhiều người.

---

## 6. Kiến nghị

1. **Ghi nhận hoàn thành giai đoạn xây dựng nền tảng** AqtCoreFW (16 module, 304 giờ Jira) làm tài sản kỹ thuật dùng chung cho Khối GOV.
2. **Tiếp tục mô hình AI support có kiểm soát** cho các dự án GOV kế tiếp — ROI khoản đầu tư AI ~324% đã được kiểm chứng trên thực tế.
3. **Hoàn thiện các hạng mục mở** theo ưu tiên nghiệp vụ: xác minh mật mã chữ ký (Phase 6), KPI Threshold & Alerting (Reporting), rà soát module Rules.
4. **Đo lường chính thức** "mức độ hoàn thành" theo bộ tiêu chí use case thay vì con số % gộp (xem khuyến nghị `docs/reference/project-modules-overview.md` mục 5).

---

*Báo cáo lập ngày 18/06/2026, tổng hợp từ tài liệu đề xuất v1.0, báo cáo module dự án và dữ liệu giờ Jira (304 giờ). Các con số tài chính theo đơn giá đề xuất 250.000 VNĐ/giờ.*
