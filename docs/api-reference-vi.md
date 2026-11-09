# Tài liệu API — AqtCoreFW
**Phiên bản:** 2.27 | **Cập nhật:** 2026-03-29 | **Base URL:** `https://localhost:5001`

> Tất cả endpoint trả về `ApiResponse<T>` (xem [api-design-standards.md](api-design-standards.md)).
> Phân trang: `page` (1-indexed), `pageSize` (1–100), trả về `meta.pagination`.
> Xác thực: Bearer JWT qua OpenIddict. API Key cho M2M (`X-Api-Key` header).

---

## Mục lục

1. [Quy ước chung](#1-quy-ước-chung)
2. [Identity — Quản lý người dùng](#2-identity)
2a. [Chữ ký số — QĐ 742](#27-chữ-ký-số)
3. [Files — Quản lý tệp tin](#3-files)
4. [Forms — Biểu mẫu động](#4-forms)
5. [Workflow — Quy trình phê duyệt](#5-workflow)
6. [Audit — Nhật ký kiểm toán](#6-audit)
7. [Notifications — Thông báo](#7-notifications)
8. [AI — Trí tuệ nhân tạo](#8-ai)
9. [Reporting — Báo cáo](#9-reporting)
10. [Organization — Tổ chức](#10-organization)
11. [MasterData — Danh mục dùng chung](#11-masterdata)
12. [Collaboration — Hội thoại](#12-collaboration)
13. [Integration — Tích hợp bên ngoài](#13-integration)
14. [Search — Tìm kiếm toàn văn](#14-search)
15. [Rules — Công cụ quy tắc](#15-rules)
16. [SystemParams — Tham số hệ thống](#16-systemparams)
17. [Admin — Quản trị hệ thống](#17-admin)
18. [Mã lỗi](#18-mã-lỗi)

---

## 1. Quy ước chung

### Xác thực (Authentication)
| Phương thức | Header | Mô tả |
|-------------|--------|-------|
| JWT Bearer | `Authorization: Bearer {token}` | Đăng nhập qua OpenIddict `/connect/token` |
| API Key | `X-Api-Key: {key}` | Dành cho hệ thống M2M (machine-to-machine) |

### Phân quyền (Roles)
| Role | Mô tả |
|------|-------|
| `SystemAdmin` | Quản trị toàn hệ thống, xem tất cả tenant |
| `Admin` | Quản trị viên cấp tenant |
| `GovOfficer` | Cán bộ xử lý hồ sơ |
| `Viewer` | Chỉ xem, không chỉnh sửa |
| `Citizen` | Công dân nộp hồ sơ |

### Rate Limiting (Giới hạn tần suất)
| Chính sách | Giới hạn | Áp dụng |
|------------|----------|---------|
| `anonymous` | 60 req/phút/IP | Endpoint công khai |
| `authenticated` | 600 req/phút/user | Endpoint đã đăng nhập |
| `write-ops` | 20 req/phút/IP | POST/PUT/DELETE |
| `public-form-submit` | 5 req/phút/IP | Nộp biểu mẫu công khai |
| `mfa-verify` | 5 req/phút/client | Xác minh MFA |

Khi vượt giới hạn → HTTP 429 + header `Retry-After` (giây) + `X-RateLimit-Limit` (giới hạn áp dụng).

### Tenant Isolation
- `TenantId` được lấy tự động từ JWT claim — **không truyền qua query param**
- `SystemAdmin` có thể xem cross-tenant

---

## 2. Identity

### 2.1 Quản lý người dùng — `api/v1/admin/users`
> Yêu cầu: Admin hoặc SystemAdmin

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách người dùng (tìm kiếm, phòng ban, trạng thái, phân trang) |
| `GET` | `/{id}` | Chi tiết người dùng |
| `POST` | `/` | Tạo người dùng mới |
| `PUT` | `/{id}` | Cập nhật thông tin |
| `DELETE` | `/{id}` | Xóa mềm (vô hiệu hóa) |
| `POST` | `/{id}/lock` | Khóa tài khoản |
| `POST` | `/{id}/unlock` | Mở khóa tài khoản |
| `POST` | `/{id}/reset-password` | Đặt lại mật khẩu |
| `POST` | `/{id}/roles` | Gán thêm vai trò |
| `PUT` | `/{id}/roles` | Đồng bộ toàn bộ vai trò (thay thế) |
| `GET` | `/{id}/effective-permissions` | Quyền hiệu lực (có cache) |

### 2.2 Quản lý nhóm — `api/v1/admin/groups`
> Yêu cầu: Admin hoặc SystemAdmin

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách nhóm (lọc theo tenant) |
| `GET` | `/{id}` | Chi tiết nhóm + số thành viên |
| `POST` | `/` | Tạo nhóm |
| `PUT` | `/{id}` | Cập nhật nhóm |
| `POST` | `/{id}/members` | Thêm thành viên (idempotent) |
| `DELETE` | `/{id}/members/{userId}` | Xóa thành viên |
| `POST` | `/{id}/roles` | Gán vai trò cho nhóm |
| `DELETE` | `/{id}/roles/{roleId}` | Xóa vai trò khỏi nhóm |

### 2.3 Vai trò — `api/v1/admin/roles`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh mục vai trò + quyền đi kèm |

### 2.4 Tài khoản cá nhân — `api/v1/account`
> Yêu cầu: Đã đăng nhập

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/change-password` | Đổi mật khẩu (cần mật khẩu cũ + mới) |

### 2.5 Xác thực 2 yếu tố (MFA) — `api/v1/mfa`
> Rate limit: `mfa-verify` (5 req/phút)

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/setup` | Tạo URI cấu hình TOTP (quét QR code) |
| `POST` | `/verify` | Xác minh mã TOTP |
| `POST` | `/send-otp` | Gửi OTP qua email (trả 202 Accepted) |

### 2.6 Các module Identity khác
| Controller | Route | Chức năng |
|------------|-------|-----------|
| `AbacRulesAdmin` | `api/v1/admin/abac-rules` | Quy tắc phân quyền ABAC |
| `AccessReview` | `api/v1/admin/access-reviews` | Rà soát quyền truy cập |
| `Consent` | `api/v1/consent` | Quản lý đồng ý PDPL (Law 91/Decree 356) |
| `CredentialPolicies` | `api/v1/admin/credential-policies` | Chính sách mật khẩu |
| `DataScopeAdmin` | `api/v1/admin/data-scopes` | Phạm vi dữ liệu |
| `Delegation` | `api/v1/delegation` | Ủy quyền |
| `ExternalIdentities` | `api/v1/admin/external-identities` | Tài khoản liên kết bên ngoài |
| `Session` | `api/v1/admin/sessions` | Quản lý phiên đăng nhập |
| `SodRulesAdmin` | `api/v1/admin/sod-rules` | Quy tắc tách biệt nhiệm vụ |
| `Menu` | `api/v1/menus` | Menu động theo vai trò |

### 2.7 Chữ ký số — `api/v1/digital-signature`, `api/signing`
> QĐ 742 §5.1 | Chi tiết: xem [docs/signing/integration-guide.md](signing/integration-guide.md)

| Verb | Endpoint | Mô tả | Xác thực |
|------|----------|-------|----------|
| `POST` | `/api/v1/digital-signature/verify` | Xác minh chữ ký (format=Raw\|Pkcs7) | Bearer JWT |
| `POST` | `/api/v1/digital-signature/verify-xml` | Xác minh XMLDSig | Bearer JWT |
| `POST` | `/api/v1/digital-signature/verify-jws` | Xác minh JWS | Bearer JWT |
| `POST` | `/api/v1/digital-signature/confirm` | Xác nhận giao dịch async (transactionId) | Bearer JWT |
| `POST` | `/api/signing/docflow/initiate` | Khởi tạo quy trình ký tài liệu [RequiresMfa] | Bearer JWT |
| `GET` | `/api/signing/docflow/file` | Tải tệp đã ký (params: `docId`, `token`) | Anonymous + JWT token |
| `POST` | `/api/signing/docflow/file` | Upload tài liệu để ký (multipart) | Anonymous + JWT token |
| `POST` | `/api/signing/mysign-callback/auth` | Webhook xác thực Viettel (client_credentials) | Anonymous |
| `POST` | `/api/signing/mysign-callback/result` | Webhook kết quả Mysign (Bearer token) | Anonymous |

**Request/Response Ví dụ:**
```json
// POST /api/v1/digital-signature/verify
{
  "payload": "base64-encoded-hash-or-data",
  "signature": "base64-encoded-signature",
  "format": "Raw"  // Raw | Pkcs7 | XmlDSig | Jws
}
// Response: { success: true, data: { provider: "vgca-usb", format: "Raw", certSubject: "CN=User", outcome: "Valid" } }
```

**Định tuyến nhà cung cấp:** VGCA USB token (3 tiers), Viettel Mysign (3 async modes: sync/polling/callback). Xem [docs/signing/usage-cookbook.md](signing/usage-cookbook.md) cho code examples.

---

## 3. Files

### 4.1 Quản lý tệp — `api/v1/files`
> Giới hạn upload: 100 MB | Quét virus tự động (ClamAV)

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Upload tệp (trả 202 — quét virus nền) |
| `GET` | `/` | Danh sách tệp (phân trang) |
| `GET` | `/{id}` | Tải tệp (hỗ trợ Range header) |
| `GET` | `/{id}/metadata` | Metadata + trạng thái quét (Pending/Clean/Infected) |
| `DELETE` | `/{id}` | Xóa mềm |

### 4.2 Mẫu tài liệu — `api/v1/document-templates`
| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `GET` | `/` | Danh sách mẫu | Authenticated |
| `POST` | `/` | Tạo mẫu (Draft) | Admin |
| `PUT` | `/{id}` | Cập nhật (tạo phiên bản mới) | Admin |
| `POST` | `/{id}/publish` | Xuất bản mẫu | Admin |

### 4.3 Chính sách lưu giữ — `api/v1/retention-policies`
| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `GET` | `/` | Danh sách chính sách | Authenticated |
| `POST` | `/` | Tạo chính sách | Admin |

---

## 4. Forms

### 4.1 Mẫu biểu mẫu — `api/v1/forms/templates`
> Cache: 30 phút cho GET chi tiết

| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `GET` | `/` | Danh sách mẫu | Authenticated |
| `GET` | `/{id}` | Chi tiết mẫu + danh sách trường | Authenticated |
| `POST` | `/` | Tạo mẫu (Draft) | Admin |
| `PUT` | `/{id}` | Cập nhật tên, mô tả | Admin |
| `DELETE` | `/{id}` | Vô hiệu hóa | Admin |
| `POST` | `/{id}/publish` | Xuất bản (Draft → Active) | Admin |
| `POST` | `/{id}/duplicate` | Sao chép mẫu | Admin |
| `POST` | `/{id}/fields` | Thêm trường | Admin |
| `PUT` | `/{id}/fields/{fieldId}` | Cập nhật trường | Admin |
| `DELETE` | `/{id}/fields/{fieldId}` | Xóa trường | Admin |
| `PUT` | `/{id}/fields/reorder` | Sắp xếp lại thứ tự trường | Admin |

### 4.2 Nộp biểu mẫu — `api/v1/forms/submissions`
| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `POST` | `/` | Nộp dữ liệu biểu mẫu | Authenticated |
| `GET` | `/{id}` | Xem bài nộp (Json hoặc Table) | Authenticated |
| `GET` | `/` | Danh sách bài nộp (lọc theo mẫu, trạng thái, trường) | Authenticated |
| `GET` | `/export` | Xuất CSV | Admin |
| `POST` | `/{id}/approve` | Duyệt bài nộp | Admin |
| `POST` | `/{id}/reject` | Từ chối (bắt buộc ghi lý do) | Admin |
| `POST` | `/bulk-approve` | Duyệt hàng loạt (tối đa 100) | Admin |
| `POST` | `/bulk-reject` | Từ chối hàng loạt (tối đa 100) | Admin |
| `GET` | `/{id}/export-pdf` | Xuất PDF (QuestPDF) | Authenticated |

> **Bulk operations:** Tối đa 100 bài nộp mỗi lần. Rate limit: `write-ops` (20/phút).

### 4.3 Biểu mẫu công khai — `api/v1/public/forms`
> Không cần đăng nhập | Rate limit: `public-form-submit` (5/phút/IP)

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/{code}` | Lấy schema biểu mẫu (để nhúng) |
| `POST` | `/{code}/submit` | Nộp biểu mẫu ẩn danh |

### 4.4 Views — `api/v1/views`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách view hoặc lấy view mặc định theo entityType |
| `GET` | `/{id}` | Chi tiết view (cột + bộ lọc) |
| `POST` | `/` | Tạo view |
| `PUT` | `/{id}` | Cập nhật view |
| `DELETE` | `/{id}` | Xóa view |

---

## 5. Workflow

### 5.1 Định nghĩa quy trình — `api/v1/workflow/definitions`
| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `GET` | `/` | Danh sách định nghĩa (lọc isActive) | Authenticated |
| `GET` | `/{id}` | Chi tiết + các trạng thái + chuyển đổi | Authenticated |
| `POST` | `/` | Tạo định nghĩa | Admin |
| `PUT` | `/{id}` | Cập nhật metadata | Admin |
| `DELETE` | `/{id}` | Xóa mềm (lỗi nếu còn instance hoạt động) | Admin |
| `PUT` | `/{id}/graph` | Lưu đồ thị trạng thái (visual designer) | Admin |
| `POST` | `/{id}/activate` | Kích hoạt | Admin |
| `POST` | `/{id}/deactivate` | Vô hiệu hóa | Admin |
| `POST` | `/{id}/clone` | Sao chép thành phiên bản mới | Admin |

### 5.2 Phiên bản quy trình — `api/v1/workflow/instances`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Tạo instance mới |
| `GET` | `/{id}` | Chi tiết + lịch sử chuyển đổi |
| `POST` | `/{id}/transitions` | Thực hiện chuyển đổi (409 nếu xung đột) |
| `GET` | `/{id}/available-transitions` | Các chuyển đổi khả dụng (lọc theo role) |
| `POST` | `/{id}/branch-children/{childId}/resolve` | Giải quyết nhánh song song |
| `GET` | `/{id}/branch-status` | Trạng thái tất cả nhánh |

### 5.3 Quy tắc phân công — `api/v1/workflow/assignments`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách quy tắc (tenant) |
| `POST` | `/` | Tạo quy tắc phân công |
| `DELETE` | `/{id}` | Vô hiệu hóa quy tắc |
| `GET` | `/resolve` | Tìm định nghĩa phù hợp nhất (cache 24h) |

---

## 6. Audit

### 6.1 Nhật ký kiểm toán — `api/v1/audit`
> Yêu cầu: Admin/SystemAdmin/GovOfficer | Tuân thủ QĐ742

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/logs` | Truy vấn nhật ký (ngày, userId, hành động, module, phân trang) |
| `GET` | `/statistics` | Thống kê tổng hợp (ngày/tuần/tháng) |
| `GET` | `/logs/export` | Xuất CSV (UTF-8 BOM, tối đa 10.000 dòng) |
| `GET` | `/logs/verify-chain` | Xác minh chuỗi HMAC (chỉ SystemAdmin) |

### 6.2 Sự cố bảo mật — `api/v1/admin/incidents`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách sự cố (mức nghiêm trọng, trạng thái, ngày) |
| `GET` | `/{id}` | Chi tiết sự cố |
| `POST` | `/` | Báo cáo sự cố mới |
| `PATCH` | `/{id}/status` | Cập nhật trạng thái (Investigating/Resolved/Closed) |

### 6.3 Các module Audit khác
| Controller | Chức năng |
|------------|-----------|
| `LoginAudit` | Lịch sử đăng nhập/phiên |
| `Rtbf` | Quyền được lãng quên (PDPL — Law 91/Decree 356) |

---

## 7. Notifications

### 7.1 Thông báo — `api/v1/notifications`
| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `GET` | `/` | Danh sách thông báo (kênh, đã đọc, phân trang) | Authenticated |
| `GET` | `/unread-count` | Số thông báo chưa đọc (cho badge) | Authenticated |
| `PATCH` | `/{id}/read` | Đánh dấu đã đọc | Authenticated |
| `POST` | `/read-all` | Đánh dấu tất cả đã đọc | Authenticated |
| `POST` | `/` | Gửi thông báo (lập trình) | Admin |

### 7.2 Mẫu thông báo — `api/v1/admin/notification-templates`
> Yêu cầu: SystemAdmin/TenantAdmin

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách mẫu (lọc kênh) |
| `GET` | `/{id}` | Chi tiết mẫu |
| `POST` | `/` | Tạo mẫu (Liquid template) |
| `PUT` | `/{id}` | Cập nhật tiêu đề, nội dung |
| `DELETE` | `/{id}` | Xóa mềm |

---

## 8. AI

### 8.1 Chat — `api/v1/ai`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/chat` | Gửi tin nhắn (nhận phản hồi đầy đủ) |
| `POST` | `/chat/stream` | Chat streaming (SSE — `text/event-stream`, kết thúc `[DONE]`) |
| `GET` | `/chat/{sessionId}/history` | Lịch sử chat (phân trang) |
| `POST` | `/documents/ingest` | Nạp tài liệu vào vector store (chunk + embed) |

### 8.2 Trích xuất & Agent — `api/v1/ai`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/extract` | OCR tài liệu (ảnh → trường dữ liệu, max 20 MB) |
| `POST` | `/agent/execute` | Chạy ReAct agent (tối đa 5 vòng, read-only) |
| `POST` | `/flag` | Tự động phân loại nội dung nhạy cảm (fail-open) |

### 8.3 Model Profiles — `api/v1/ai/model-profiles`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách cấu hình AI model |
| `POST` | `/` | Tạo cấu hình |
| `PUT` | `/{id}` | Cập nhật cấu hình |

---

## 9. Reporting

### 9.1 Báo cáo — `api/v1/reports`
> Giới hạn: tối đa 3 báo cáo chạy đồng thời/tenant

| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `GET` | `/dashboard` | Dashboard KPI (cache 5 phút) | Admin/GovOfficer |
| `GET` | `/definitions` | Danh sách định nghĩa báo cáo | Admin/GovOfficer |
| `POST` | `/definitions` | Tạo định nghĩa | Admin |
| `PUT` | `/definitions/{id}` | Cập nhật định nghĩa | Admin |
| `POST` | `/run` | Chạy báo cáo (trả 202 + executionId) | Admin |
| `GET` | `/executions/{id}` | Kiểm tra trạng thái thực thi | Authenticated |
| `GET` | `/executions/{id}/download` | Tải báo cáo hoàn thành | Authenticated |
| `POST` | `/compliance/pdf` | Xuất PDF tuân thủ | Admin |

### 9.2 Dashboard — `api/v1/dashboards`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Tạo dashboard |
| `GET` | `/{id}` | Chi tiết dashboard |
| `GET` | `/` | Danh sách dashboard cá nhân |
| `PUT` | `/{id}` | Cập nhật bố cục, widget |

---

## 10. Organization

### 10.1 Đơn vị tổ chức — `api/v1/admin/org/units`
| Verb | Endpoint | Mô tả | Phân quyền |
|------|----------|-------|------------|
| `GET` | `/` | Cây tổ chức | Authenticated |
| `GET` | `/{id}` | Chi tiết đơn vị | Authenticated |
| `GET` | `/{id}/members` | Thành viên đơn vị | Authenticated |
| `POST` | `/` | Tạo đơn vị | Admin |
| `PUT` | `/{id}` | Cập nhật đơn vị | Admin |
| `DELETE` | `/{id}` | Xóa (có giải quyết kế nhiệm) | Admin |

---

## 11. MasterData

### 11.1 Danh mục dùng chung — `api/v1/masterdata`
> Endpoint tỉnh/huyện/xã: **không cần đăng nhập** | Cache: 30 phút

| Verb | Endpoint | Mô tả | Auth |
|------|----------|-------|------|
| `GET` | `/provinces` | Danh sách tỉnh/TP | Public |
| `GET` | `/provinces/{code}/districts` | Quận/huyện theo tỉnh | Public |
| `GET` | `/provinces/{code}/districts/{code}/wards` | Phường/xã | Public |
| `GET` | `/districts/{code}/wards` | Phường/xã theo huyện | Public |
| `GET` | `/admin-units` | Tra cứu đơn vị hành chính theo cấp/cha | Public |
| `GET` | `/admin-units/{code}/successor` | Mã kế nhiệm (sáp nhập) | Public |
| `GET` | `/case-types` | Loại hồ sơ (tenant) | Auth |
| `GET` | `/job-titles` | Chức danh (tenant) | Auth |

---

## 12. Collaboration

### 12.1 Hội thoại — `api/v1/conversations`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Tạo hội thoại |
| `GET` | `/` | Danh sách hội thoại cá nhân |
| `GET` | `/{id}/messages` | Tin nhắn (cursor: before/after) |
| `PATCH` | `/{id}/archive` | Lưu trữ hội thoại |
| `POST` | `/{id}/members` | Thêm thành viên |

### 12.2 Tin nhắn — `api/v1/messages`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Gửi tin nhắn |
| `DELETE` | `/{id}` | Xóa mềm |
| `POST` | `/{id}/read` | Đánh dấu đã đọc |
| `GET` | `/search` | Tìm kiếm toàn văn trong hội thoại |
| `GET` | `/unread-count` | Số tin chưa đọc theo hội thoại |

---

## 13. Integration

### 13.1 Đối tác — `api/v1/integration/partners`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách đối tác |
| `GET` | `/{id}` | Chi tiết |
| `POST` | `/` | Tạo đối tác |
| `PUT` | `/{id}` | Cập nhật |
| `DELETE` | `/{id}` | Xóa mềm |

### 13.2 Hợp đồng — `api/v1/integration/contracts`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Danh sách (lọc theo partnerId) |
| `GET` | `/{id}` | Chi tiết |
| `POST` | `/` | Tạo hợp đồng (Draft) |
| `PUT` | `/{id}` | Cập nhật (Draft/Active) |
| `DELETE` | `/{id}` | Xóa mềm |

---

## 14. Search

### 14.1 Tìm kiếm toàn văn — `api/v1/search`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `GET` | `/` | Tìm kiếm (q, type, facets, cursor pagination) |
| `GET` | `/facets` | Thống kê facet theo entity type |

### 14.2 Truy vấn lưu — `api/v1/search/saved-queries`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Lưu truy vấn |
| `GET` | `/` | Danh sách truy vấn đã lưu |
| `POST` | `/{id}/execute` | Thực thi truy vấn đã lưu |
| `PUT` | `/{id}` | Cập nhật |
| `DELETE` | `/{id}` | Xóa |

---

## 15. Rules

### 15.1 Bộ quy tắc — `api/v1/rulesets`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Tạo RuleSet (Draft) |
| `GET` | `/{id}` | Chi tiết + danh sách quy tắc |
| `PUT` | `/{id}/rules/{ruleId}` | Cập nhật quy tắc |
| `POST` | `/{id}/activate` | Kích hoạt (snapshot + version) |

### 15.2 Đánh giá quy tắc — `api/v1/rules`
| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/evaluate` | Đánh giá RuleSet (ghi audit) |
| `POST` | `/simulate` | Mô phỏng (không ghi audit, trả trace) |
| `POST` | `/{ruleSetId}/explain` | Giải thích chi tiết (quy tắc nào kích hoạt, tại sao) |
| `POST` | `/test-cases/{id}/run` | Chạy test case |

---

## 16. SystemParams

### 16.1 Feature Flags
| Verb | Endpoint | Mô tả | Auth |
|------|----------|-------|------|
| `GET` | `api/v1/admin/feature-flags` | Danh sách cờ tính năng | Admin |
| `PUT` | `api/v1/admin/feature-flags/{name}` | Bật/tắt cờ | Admin |
| `GET` | `api/v1/feature-flags/{key}` | Kiểm tra trạng thái cờ | Public |

### 16.2 Tham số hệ thống — `api/v1/admin/system-params`
> Yêu cầu: Admin

### 16.3 Thông báo hệ thống — `api/v1/announcements`
> Thông báo toàn hệ thống cho người dùng

---

## 17. Admin

### 17.1 API Keys — `api/v1/admin/api-keys`
> Yêu cầu: Admin policy

| Verb | Endpoint | Mô tả |
|------|----------|-------|
| `POST` | `/` | Tạo API key (trả plaintext **một lần duy nhất**) |
| `GET` | `/` | Danh sách keys (chỉ metadata) |
| `DELETE` | `/{id}` | Thu hồi key (Redis TTL 5 phút) |
| `POST` | `/{id}/rotate` | Xoay key (thu hồi + tạo mới nguyên tử) |

### 17.2 Các module Admin khác
| Controller | Chức năng |
|------------|-----------|
| `Alerting` | Cấu hình cảnh báo |
| `Backup` | Quản lý sao lưu (NĐ53) |
| `DeadLetters` | Hàng đợi tin nhắn lỗi |
| `EventCatalog` | Danh mục schema sự kiện |
| `Webhooks` | Quản lý endpoint webhook |

---

## 18. Mã lỗi

| Mã | Ý nghĩa |
|----|---------|
| `GOV_SYS_001` | Lỗi validation đầu vào |
| `GOV_SYS_002` | Không tìm thấy tài nguyên (404) |
| `GOV_SYS_003` | Không có quyền truy cập (403) |
| `GOV_SYS_004` | Lỗi xung đột dữ liệu (409) |
| `GOV_SYS_005` | Vượt giới hạn tần suất (429) |
| `GOV_SEC_*` | Lỗi bảo mật (xem runbooks/security-incident-runbook.md) |
| `GOV_FRM_*` | Lỗi module Forms |
| `GOV_WFL_*` | Lỗi module Workflow |
| `GOV_IDT_*` | Lỗi module Identity |
| `GOV_AUD_*` | Lỗi module Audit |
| `GOV_FIL_*` | Lỗi module Files |

> Chi tiết mã lỗi từng module: xem Error Catalog trong mã nguồn (`Domain/Errors/`).

---

## Ghi chú kỹ thuật

- **Soft delete**: Tất cả thao tác `DELETE` là xóa mềm (đặt cờ `IsDeleted`)
- **Concurrency**: Workflow transitions dùng optimistic locking (RowVersion), trả 409 khi xung đột
- **Tenant isolation**: Tất cả dữ liệu được phân tách theo TenantId (từ JWT)
- **Audit**: Mọi thao tác thay đổi dữ liệu đều được ghi nhật ký kiểm toán
- **PII**: Trường có đánh dấu `[DataClassification(Confidential)]` được mã hóa Always Encrypted
- **PDPL**: Tuân thủ Luật 91/2025/QH15 + Nghị định 356/2025 — đồng ý, quyền xóa, thông báo vi phạm 72h
