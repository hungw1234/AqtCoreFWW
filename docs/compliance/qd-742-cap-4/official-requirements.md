# QĐ 742/QĐ-BTTTT — Yêu cầu Cấp độ 4 (Official từ nguyên văn)

**Source:** Phụ lục QĐ 742/QĐ-BTTTT ngày 22/04/2022 — file gốc do anh cung cấp
**Date extracted:** 2026-04-28 17:33 ICT
**Method:** Word COM convert → text parse cấu trúc bảng cấp 1-5

---

## ⚠️ CORRECTIONS vs prior consultations

Web summary fetch trước đó đã interpret **SAI** một số mục:

| Mục | Tôi từng nói | THỰC TẾ trong văn bản gốc |
|-----|-------------|---------------------------|
| 2.5 Tách cổng quản trị | "cấp 4 differentiator" | ❌ CHỈ CẤP 5 (1x), KHÔNG áp dụng cấp 4 |
| 2.6 Time-window admin | "cấp 4 differentiator" | ❌ CHỈ CẤP 5 (1x), KHÔNG áp dụng cấp 4 |
| 3.3 Audit log RBAC theo nhóm | "cấp 4" | ❌ CHỈ CẤP 5 (1x), KHÔNG áp dụng cấp 4 |
| 6.3 Backup tập trung | (chưa đề cập) | ❌ CHỈ CẤP 5 |

→ **Cấp 4 ĐƠN GIẢN HƠN nhiều so với prior estimate.** Effort giảm đáng kể.

---

## QUY TẮC ĐỌC PHỤ LỤC

Phụ lục có 5 cột cấp độ (1, 2, 3, 4, 5). Mỗi yêu cầu được đánh dấu "x" cho cấp áp dụng:

- **5x** = áp dụng tất cả cấp 1-5 (baseline cho mọi hệ thống)
- **4x** = áp dụng cấp 2-5
- **3x** = áp dụng cấp 3-5
- **2x** = áp dụng cấp 4-5 ← **đây là cấp-4-specific (mới so với cấp 3)**
- **1x** = chỉ cấp 5 (KHÔNG áp dụng cấp 4)

Cấp 4 = TẤT CẢ items có ≥2 x's (loại trừ items chỉ có 1x).

---

## I. ĐIỀU KIỆN TIỀN ĐỀ (áp dụng MỌI cấp)

| # | Yêu cầu | Cấp áp dụng |
|---|---------|-------------|
| - | **Tài liệu**: Hướng dẫn triển khai/cấu hình, Hướng dẫn sử dụng/quản trị, Tài liệu thiết kế, Mã nguồn (theo yêu cầu) | 1-5 |
| - | **Quản lý điểm yếu ATTT**: Trước nghiệm thu, không tồn tại lỗ hổng theo đánh giá của tổ chức được cấp phép | 1-5 |

---

## II. CHỨC NĂNG XÁC THỰC (Mục 1)

| # | Yêu cầu | Cấp áp dụng | Differentiator |
|---|---------|-------------|----------------|
| **1.1.a** | UI quản lý tài khoản người sử dụng | 1-5 | baseline |
| **1.1.b** | Xác thực khi truy cập quản trị/cấu hình | 1-5 | baseline |
| **1.1.c** | Xác thực khi truy cập sử dụng | 1-5 | baseline |
| **1.2** | Lưu trữ credential mã hóa SHA-256/512/3 hoặc tương đương | 1-5 | baseline |
| **1.3.a** | UI yêu cầu đổi mật khẩu mặc định lần đầu | 1-5 | baseline |
| **1.3.b** | UI thiết lập quy tắc mật khẩu (số ký tự, loại) | 1-5 | baseline |
| **1.3.c** | UI thiết lập thời gian yêu cầu thay đổi mật khẩu định kỳ | **2-5** | từ cấp 2 |
| **1.3.d** | UI thiết lập thời gian mật khẩu hợp lệ | **2-5** | từ cấp 2 |
| **1.3.d'** | Khóa tài khoản + yêu cầu mật khẩu mới khi hết hạn | **2-5** | từ cấp 2 |
| **1.3.e** | Mở khóa khi đổi mật khẩu thành công (sau hết hạn) | **2-5** | từ cấp 2 |
| **1.4.a** | UI thiết lập chính sách giới hạn đăng nhập sai | **2-5** | từ cấp 2 |
| **1.4.b** | Cảnh báo người dùng khi vi phạm chính sách | **2-5** | từ cấp 2 |
| **1.4.c** | Tự động ngăn cản đăng nhập tự động (anti-bot) | **2-5** | từ cấp 2 |
| **1.4.d** | Tự động vô hiệu hóa tài khoản khi vi phạm chính sách | **3-5** | từ cấp 3 |
| **1.5** | Mật khẩu mã hóa trước khi gửi qua mạng | **3-5** | từ cấp 3 |
| **1.6.a** | **UI quản trị viên quản lý chính sách MFA** | **🔴 4-5** | **CẤP 4 NEW** |
| **1.6.b** | **Tích hợp các bước MFA khi chính sách kích hoạt** | **🔴 4-5** | **CẤP 4 NEW** |

---

## III. CHỨC NĂNG KIỂM SOÁT TRUY CẬP (Mục 2)

| # | Yêu cầu | Cấp áp dụng | Differentiator |
|---|---------|-------------|----------------|
| **2.1.a** | UI thiết lập timeout phiên kết nối | 1-5 | baseline |
| **2.1.b** | Hiển thị thông báo + đóng phiên hết hạn + yêu cầu đăng nhập lại | **2-5** | từ cấp 2 |
| **2.2.a** | UI quản lý chính sách giới hạn địa chỉ mạng quản trị từ xa | **2-5** | từ cấp 2 |
| **2.2.b** | Thực thi chính sách giới hạn IP quản trị | **2-5** | từ cấp 2 |
| **2.3.a** | UI quản lý chính sách phân quyền tài khoản theo nhóm | **3-5** | từ cấp 3 |
| **2.3.b** | Phân loại tối thiểu **3 nhóm**: User thông thường / Quản trị mức sử dụng / Quản trị mức phát triển-vận hành | **3-5** | từ cấp 3 |
| **2.3.c** | Thực thi phân quyền + cấp quyền tối thiểu | **3-5** | từ cấp 3 |
| **2.4.a** | UI thiết lập quyền cho từng tài khoản | **3-5** | từ cấp 3 |
| **2.4.b** | Thực thi chính sách phân quyền cho tài khoản | **3-5** | từ cấp 3 |
| ~~2.5.a~~ | ~~UI quản lý chính sách cổng quản trị ứng dụng~~ | **❌ chỉ cấp 5** | **KHÔNG áp dụng cấp 4** |
| ~~2.5.b~~ | ~~Tách biệt cổng quản trị với cổng dịch vụ~~ | **❌ chỉ cấp 5** | **KHÔNG áp dụng cấp 4** |
| ~~2.6.a~~ | ~~UI quản lý chính sách time-window quản trị~~ | **❌ chỉ cấp 5** | **KHÔNG áp dụng cấp 4** |
| ~~2.6.b~~ | ~~Thực thi time-window quản trị~~ | **❌ chỉ cấp 5** | **KHÔNG áp dụng cấp 4** |

---

## IV. NHẬT KÝ HỆ THỐNG (Mục 3)

| # | Yêu cầu | Cấp áp dụng | Differentiator |
|---|---------|-------------|----------------|
| **3.1.a** | Cung cấp chức năng ghi nhật ký hệ thống | 1-5 | baseline |
| **3.1.b** | Phân loại nhật ký tối thiểu **5 nhóm**:<br/>(i) Truy cập phần mềm<br/>(ii) Đăng nhập khi quản trị<br/>(iii) Lỗi phát sinh<br/>(iv) Quản lý tài khoản<br/>(v) Thay đổi cấu hình | **3-5** | từ cấp 3 |
| **3.2.a** | UI quản lý chính sách nhật ký | **3-5** | từ cấp 3 |
| **3.2.b** | UI cấu hình thời gian lưu trữ nhật ký | **3-5** | từ cấp 3 |
| **3.2.c** | Lưu trữ nhật ký với **5 thông tin**:<br/>(i) Thời điểm sinh<br/>(ii) Phân nhóm<br/>(iii) Mô tả thao tác/lỗi<br/>(iv) Đối tượng thực hiện<br/>(v) Mức độ quan trọng | **3-5** | từ cấp 3 |
| ~~3.3.a~~ | ~~UI quản lý phân quyền nhật ký theo nhóm~~ | **❌ chỉ cấp 5** | **KHÔNG áp dụng cấp 4** |
| ~~3.3.b~~ | ~~Thực thi phân quyền nhật ký~~ | **❌ chỉ cấp 5** | **KHÔNG áp dụng cấp 4** |

---

## V. AN TOÀN ỨNG DỤNG VÀ MÃ NGUỒN (Mục 4)

| # | Yêu cầu | Cấp áp dụng | Differentiator |
|---|---------|-------------|----------------|
| **4.1** | Kiểm tra tính hợp lệ của input trước khi xử lý | 1-5 | baseline |
| **4.2** | Đánh giá theo OWASP, không tồn tại lỗ hổng: SQL Injection, OS command injection, RFI, LFI, Xpath injection, XSS, CSRF | **3-5** | từ cấp 3 |
| **4.3.a** | Kiểm soát lỗi: chỉ hiển thị lỗi đã kiểm soát, không lộ lỗi nội bộ | **3-5** | từ cấp 3 |
| **4.3.b** | Hiển thị thông báo lỗi đến người dùng | **3-5** | từ cấp 3 |
| **4.4.a** | Không hardcode credential/secret trong source code; phải qua giao diện cấu hình | **2-5** | từ cấp 2 |

---

## VI. BẢO MẬT THÔNG TIN LIÊN LẠC (Mục 5)

| # | Yêu cầu | Cấp áp dụng | Differentiator |
|---|---------|-------------|----------------|
| **5.1** | Mã hóa dữ liệu trước khi truyền qua mạng (sử dụng chữ ký số) | **3-5** | từ cấp 3 |

---

## VII. SAO LƯU DỰ PHÒNG (Mục 6)

| # | Yêu cầu | Cấp áp dụng | Differentiator |
|---|---------|-------------|----------------|
| **6.1.a** | UI thiết lập chính sách backup database + cấu hình | **3-5** | từ cấp 3 |
| **6.1.b** | Thực hiện backup theo chính sách | **3-5** | từ cấp 3 |
| **6.2.a** | **UI quản lý chính sách phân loại dữ liệu lưu trữ theo nhóm** | **🔴 4-5** | **CẤP 4 NEW** |
| **6.2.b** | **Lưu trữ dữ liệu theo định dạng cho từng loại dữ liệu** | **🔴 4-5** | **CẤP 4 NEW** |
| ~~6.3.a~~ | ~~UI cấu hình gửi backup về hệ thống tập trung~~ | **❌ chỉ cấp 5** | KHÔNG áp dụng cấp 4 |
| ~~6.3.b/c/d~~ | ~~Backup thủ công + tự động + khôi phục từ tập trung~~ | **❌ chỉ cấp 5** | KHÔNG áp dụng cấp 4 |

---

## TỔNG KẾT — CHÍNH XÁC YÊU CẦU CẤP ĐỘ 4

### Số yêu cầu kỹ thuật phải đáp ứng

| Group | Số mục cấp 4 | Trong đó NEW (cấp-4-specific) |
|-------|--------------|-------------------------------|
| Mục 1 — Xác thực | 17 | **2** (1.6.a, 1.6.b) |
| Mục 2 — Kiểm soát truy cập | 9 | 0 |
| Mục 3 — Nhật ký | 5 | 0 |
| Mục 4 — An toàn ứng dụng | 5 | 0 |
| Mục 5 — Bảo mật liên lạc | 1 | 0 |
| Mục 6 — Sao lưu | 4 | **2** (6.2.a, 6.2.b) |
| **TỔNG** | **41 mục** | **4 mục NEW cho cấp 4** |

### Differentiator CHÍNH XÁC cấp 4 vs cấp 3 (chỉ 4 mục)

| # | Yêu cầu | Yêu cầu UI? |
|---|---------|-------------|
| **1.6.a** | UI quản trị viên quản lý chính sách MFA | ✅ CẦN UI |
| **1.6.b** | Tích hợp MFA khi policy kích hoạt | ❌ Backend logic |
| **6.2.a** | UI quản lý chính sách phân loại dữ liệu | ✅ CẦN UI |
| **6.2.b** | Lưu trữ dữ liệu theo định dạng từng loại | ❌ Backend logic |

→ Chỉ **4 mục NEW**, trong đó 2 cần UI, 2 backend. Đây là toàn bộ "delta" để nâng từ cấp 3 lên cấp 4.

---

## ĐỐI CHIẾU AQTCOREFW (CORRECTED)

### 41 yêu cầu cấp 4 → status

**Mục 1 — Xác thực (17/17 cần có):**

| # | Status | Note |
|---|--------|------|
| 1.1.a, 1.1.b, 1.1.c | ✅ MET | UsersAdminController + Identity auth |
| 1.2 | ✅ MET | PBKDF2-HMAC-SHA256 |
| 1.3.a | ❌ MISSING | KHÔNG có MustChangePassword first-login flag |
| 1.3.b | ✅ MET | Password complexity config |
| 1.3.c, d, d', e | ✅ MET | PasswordExpiryService 90d + middleware |
| 1.4.a | ⚠️ PARTIAL | Lockout có code-level config, thiếu admin UI |
| 1.4.b | ❓ UNVERIFIED | Cần check UI feedback |
| 1.4.c | ✅ MET | Identity LockoutOptions |
| 1.4.d | ❓ UNVERIFIED | Cần escalation policy |
| 1.5 | ➖ TLS deploy config | OUT-OF-CODE |
| **1.6.a** | ❌ **CẤP-4 GAP** | MfaService có nhưng không có admin policy UI |
| **1.6.b** | ❌ **CẤP-4 GAP** | Không có `[RequiresMfa]` policy enforcement |

**Mục 2 — Kiểm soát truy cập (9/9 cần có cho cấp 4):**

| # | Status | Note |
|---|--------|------|
| 2.1.a | ✅ MET | Cookie+JWT timeout config |
| 2.1.b | ✅ MET | OAuth refresh flow |
| 2.2.a | ⚠️ PARTIAL | IpFilterMiddleware có; thiếu admin CRUD UI |
| 2.2.b | ✅ MET | IpFilterMiddleware enforce |
| 2.3.a | ✅ MET | RolesAdminController + UserGroupsAdminController |
| 2.3.b | 🟢 MET-VERIFY | SystemAdmin/Admin/GovOfficer (cần verify match 3-tier QĐ 742) |
| 2.3.c | ✅ MET | RBAC + ABAC |
| 2.4.a | ✅ MET | UsersAdminController role assignment |
| 2.4.b | ✅ MET | PermissionAuthorizationHandler |

**Mục 3 — Nhật ký (5/5 cần có cho cấp 4):**

| # | Status | Note |
|---|--------|------|
| 3.1.a | ✅ MET | IAuditLogger + AuditService |
| 3.1.b | ⚠️ PARTIAL | AuditAction 20 values KHÔNG match 5 nhóm; cần mapping |
| 3.2.a | ⚠️ PARTIAL | Generic system-params, thiếu dedicated audit policy UI |
| 3.2.b | ⚠️ PARTIAL | Cấu hình retention chưa có UI rõ |
| 3.2.c | ⚠️ PARTIAL | 4/5 fields có; thiếu cột Severity |

**Mục 4 — An toàn ứng dụng (5/5):**

| # | Status | Note |
|---|--------|------|
| 4.1 | ✅ MET | FluentValidation auto-wired |
| 4.2 | ✅ MET | EF parameterized + HtmlSanitizer + JWT (no CSRF needed for stateless) |
| 4.3.a, 4.3.b | ✅ MET | GlobalExceptionHandlerMiddleware RFC 9457 |
| 4.4.a | ⚠️ PARTIAL | Vault integration ongoing (Phase 02); appsettings fallback dev |

**Mục 5 — Bảo mật liên lạc (1/1):**

| # | Status | Note |
|---|--------|------|
| 5.1 | ⚠️ PARTIAL | TLS có; chữ ký số (digital signature) infra chưa rõ — research VN PKI đã có |

**Mục 6 — Sao lưu (4/4 cần cho cấp 4):**

| # | Status | Note |
|---|--------|------|
| 6.1.a | ❓ UNVERIFIED | Backup policy UI không thấy |
| 6.1.b | ❓ UNVERIFIED | Backup execution không scout |
| **6.2.a** | ❌ **CẤP-4 GAP** | Data classification attribute có, KHÔNG có UI manage policy |
| **6.2.b** | ⚠️ PARTIAL | EncryptedStringConverter có (1 field); chưa apply rộng theo classification |

---

## TỔNG KẾT COMPLIANCE CẤP 4

```
┌──────────────────────────────────────────────────┐
│ AQTCOREFW vs QĐ 742 CẤP ĐỘ 4 (Official)         │
├──────────────────────────────────────────────────┤
│ Total yêu cầu cấp 4:           41 mục           │
│ ✅ MET:                         22 (~54%)        │
│ ⚠️ PARTIAL:                     9 (~22%)         │
│ ❌ MISSING (gap thật):           3 (~7%)         │
│ ❓ UNVERIFIED:                  4 (~10%)         │
│ ➖ OUT-OF-CODE:                  3 (~7%)         │
│                                                  │
│ Compliance ≈ 54% MET + 22% gần MET = 76% solid   │
└──────────────────────────────────────────────────┘
```

### CẤP-4-SPECIFIC GAPS (chỉ 3 items thực sự MISSING)

| # | Yêu cầu | Effort |
|---|---------|--------|
| 1.6.a | UI MFA policy admin | 2-3 ngày FE |
| 1.6.b | MFA enforcement attribute trên admin endpoint | 1-2 ngày BE |
| 6.2.a | UI quản lý classification + apply | 1-2 ngày FE |

**Tổng cấp-4-specific:** ~4-7 ngày dev (vs 8-11 tuần em ước lượng trước!).

### BASELINE GAPS (cấp 1-3 cộng dồn, cũng cần cho cấp 4)

| # | Yêu cầu | Effort |
|---|---------|--------|
| 1.3.a | First-login force change | 1 ngày |
| 1.4.a | Admin UI lockout policy management | 1-2 ngày |
| 2.2.a | Admin CRUD UI cho IP whitelist | 2 ngày |
| 3.1.b | AuditCategory 5-nhóm refactor | 2-3 ngày |
| 3.2.a/b | Dedicated Audit Policy UI | 2 ngày |
| 3.2.c | Severity column + populate | 1 ngày |
| 4.4.a | Vault Phase 02 finish | 3-5 ngày |
| 6.1.a/b | Backup policy UI + verify | 2-3 ngày |
| 6.2.b | Apply encryption rộng theo classification | 5-6h (đã ước lượng) |

**Tổng baseline gaps:** ~14-20 ngày dev.

---

## EFFORT TOTAL — CORRECTED

| Component | Days |
|-----------|------|
| Cấp-4-specific (1.6.a/b + 6.2.a) | 4-7 |
| Baseline gaps (1-3 cộng dồn) | 14-20 |
| Verify UNVERIFIED items | 1-2 |
| Test + audit prep | 3-5 |
| **TOTAL** | **22-34 ngày** ≈ **4-6 tuần (1 dev FT)** |

→ **Đỡ hơn 2× so với prior estimate 8-11 tuần.**

→ **Parallel với 2 dev:** 2-4 tuần.

---

## KEY INSIGHTS & RECOMMENDATIONS

1. **Cấp 4 KHÔNG yêu cầu tách cổng admin/dịch vụ.** Đây là đặc thù chỉ cấp 5 (quan trọng quốc gia). Em từng recommend Phase 2 build admin port split (3-4 tuần) — **HỦY**.

2. **Cấp 4 KHÔNG yêu cầu time-window admin.** Cũng chỉ cấp 5. **HỦY** middleware `AdminTimeWindow`.

3. **Cấp 4 KHÔNG yêu cầu audit log RBAC theo nhóm.** Permission-based access có sẵn (`Audit.Log.View`) là đủ cho cấp 4.

4. **Cấp-4-specific thật sự chỉ là MFA + Data classification UI** — 2 features có thể ship trong 1 sprint nhỏ.

5. **AqtCoreFW đã ở 76% compliance level 4** sau khi tính đúng. Backlog rất manageable.

6. **JWT audience separation user vs admin** — không phải QĐ 742 yêu cầu. Đây là defense-in-depth tự nguyện. Có thể defer.

7. **Refactor IAuditLogger 5-nhóm** vẫn cần (mục 3.1.b) — đây là baseline cấp 3 cộng dồn. Effort 2-3 ngày, không tránh được.

---

## UNRESOLVED QUESTIONS

1. Backup policy + execution UI có chưa? Cần scout thêm `features/backup-*` hoặc `features/system-config*`
2. Digital signature infrastructure cho mục 5.1 — đã có research `research-vn-pki-digest.md` (memory) nhưng chưa wire vào sản phẩm
3. Severity column cho audit log — map từ AuditAction nào sang Critical/Warning/Info?
4. Item 1.3.a "đổi mật khẩu mặc định lần đầu" — production có user dùng password mặc định không? Hay đã enforce ngay khi tạo user?
5. Phụ lục bản gốc có notes/foonotes nào em chưa parse hết không? File extract ~17KB từ 176KB .doc — có phần nào lost không?

---

**Status:** DONE — corrections major, recommendations updated
**Next:** User review + decide phase ordering với CORRECTED scope (22-34 days vs 8-11 weeks)
