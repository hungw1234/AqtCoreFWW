---
title: Test Cases - Identity Module (Core)
snapshot_commit: 387b6254
snapshot_version: v2.137p113
date: 2026-05-26
ucs_covered: 13
tcs_total: 66
trace_source: docs/srs/use-cases/uc-identity-core.md
---

# Test Cases — Identity Module (Core)

Đặc tả test case (IEEE 829-style compact) cho 13 use case core của Identity module. Mỗi TC trace ngược về UC source + step number, dùng cho QA execution thủ công và làm input cho dev viết test code automation sau.

## Conventions

- **TC-ID format:** `TC-IDN-{UC#}-{TC#}` — Module-IDN, UC số 01-13, TC seq 01-NN
- **Priority:** P0=Critical (login/auth core) | P1=High (admin ops) | P2=Medium (self-service) | P3=Low (edge/cosmetic)
- **Type:** Positive (happy path) | Negative (error path) | Boundary (limit values) | Security (auth/authz/rate-limit) | Performance (latency)
- **Layer:** Unit | Integration (DB+HTTP) | E2E (FE+BE+infra) | Manual (UX/visual)
- **Trace:** UC-IDN-XX step Y-Z links back to source UC scenario step

## Coverage Summary

| UC | Title | TCs | P0 | P1 | P2 |
|----|-------|-----|----|----|----|
| UC-IDN-01 | Login (Local) | 6 | 5 | 1 | 0 |
| UC-IDN-02 | Login OIDC | 5 | 2 | 2 | 1 |
| UC-IDN-03 | Enable MFA | 5 | 1 | 3 | 1 |
| UC-IDN-04 | Verify MFA | 5 | 3 | 2 | 0 |
| UC-IDN-05 | Change Password | 5 | 2 | 2 | 1 |
| UC-IDN-06 | Forgot Password | 5 | 1 | 3 | 1 |
| UC-IDN-07 | Admin Create User | 6 | 2 | 3 | 1 |
| UC-IDN-08 | Admin Update User | 5 | 1 | 3 | 1 |
| UC-IDN-09 | Admin Disable User | 4 | 2 | 2 | 0 |
| UC-IDN-10 | Assign Role | 5 | 2 | 2 | 1 |
| UC-IDN-11 | Create Group + Members | 5 | 1 | 3 | 1 |
| UC-IDN-12 | Grant Permission to Role | 5 | 2 | 2 | 1 |
| UC-IDN-13 | View Me + Effective Perms | 5 | 1 | 3 | 1 |
| **Total** | | **66** | **25** | **31** | **10** |

---

## UC-IDN-01: Login bằng mật khẩu (Local)

Source: `uc-identity-core.md#uc-idn-01` — Endpoint `POST /api/v1/auth/login`.

#### TC-IDN-01-01: Login thành công (no MFA, no expire)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User `submitter01` exists, IsActive=true, IsLocked=false, MfaEnabled=false, tenant=`tenant-a`, password=`Valid@Pass123`
- **Data:** `POST /api/v1/auth/login` body `{ "username": "submitter01", "password": "Valid@Pass123", "tenantId": "tenant-a" }`
- **Expected:** 200 OK; response.accessToken là JWT non-empty với claim `sub=submitter01`, `tenant_id=tenant-a`; `expiresIn=3600`; `refreshToken` present; subsequent `GET /me` với `Authorization: Bearer <token>` → 200 + user profile; audit row `LoginAttempt/Success/IP/UA` created in `AuditLog`

#### TC-IDN-01-02: Login fail — user không tồn tại
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** No user named `ghost_user` in tenant `tenant-a`
- **Data:** `POST /api/v1/auth/login { "username": "ghost_user", "password": "anything", "tenantId": "tenant-a" }`
- **Expected:** 401 Unauthorized; message generic `"Invalid credentials"` (no enumeration leak); `accessToken` absent; audit row `LoginAttempt/Failure/reason=user_not_found`

#### TC-IDN-01-03: Login fail — sai mật khẩu, increment counter
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** User `submitter01` exists, IsActive=true, `LoginFailureCount=0`
- **Data:** `POST /api/v1/auth/login` với `password="WrongPass"`
- **Expected:** 401 Unauthorized; DB `LoginFailureCount` incremented to 1; user NOT locked yet; audit `LoginAttempt/Failure/reason=invalid_password`

#### TC-IDN-01-04: Login lockout — 6th attempt locks account
- **Pri:** P0 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 3a (lockout branch)
- **Pre:** User `submitter01` `LoginFailureCount=5`
- **Data:** 6th `POST /api/v1/auth/login` với wrong password
- **Expected:** 401 Unauthorized; DB `IsLocked=true`, `LockedAt` set; unlock email sent (verify outbox table); audit row `LoginAttempt/Failure/reason=account_locked_now`

#### TC-IDN-01-05: Login fail — account đã lock
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2b
- **Pre:** User `submitter01` IsLocked=true
- **Data:** `POST /api/v1/auth/login` với credentials đúng
- **Expected:** 403 Forbidden; message `"Account locked. Please contact admin."`; audit `LoginAttempt/Failure/reason=account_locked`

#### TC-IDN-01-06: Login với MFA → trả về mfaToken
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 4
- **Pre:** User `auditor01` MfaEnabled=true, MfaMethod='totp', password correct
- **Data:** `POST /api/v1/auth/login` với credentials đúng
- **Expected:** 200 OK; response `{ requiresMfa: true, mfaToken: "<temp-jwt>" }`; `accessToken` absent; mfaToken exp ≤5min; chuẩn bị cho UC-IDN-04

---

## UC-IDN-02: Login OIDC (Google/Azure AD/VNeID)

Source: `uc-identity-core.md#uc-idn-02` — Endpoint `GET /api/v1/auth/oidc/{provider}/callback`.

#### TC-IDN-02-01: OIDC Google callback — user mapped exists
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User `user@gov.vn` mapped to Google sub `1234567890`, IsActive=true, tenant inferred from email domain
- **Data:** `GET /api/v1/auth/oidc/google/callback?code=<valid_code>&state=<csrf>`
- **Expected:** 200 OK + redirect 302 tới FE dashboard; accessToken set in HttpOnly cookie; refreshToken issued; audit `LoginAttempt/Success/provider=google`

#### TC-IDN-02-02: OIDC VNeID callback — auto-provision user mới
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** ext 2a (auto-provision)
- **Pre:** No user mapped to VNeID `vneid_sub_99`; tenant policy `auto_provision_vneid=true`
- **Data:** `GET /api/v1/auth/oidc/vneid/callback?code=<valid>&state=<csrf>`
- **Expected:** 201 + redirect; new user created với `OidcSub=vneid_sub_99`, default role `EMPLOYEE`, IsActive=true; audit `UserCreated/source=oidc_vneid` + `LoginAttempt/Success`

#### TC-IDN-02-03: OIDC callback — invalid CSRF state
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 1a
- **Pre:** N/A
- **Data:** `GET /api/v1/auth/oidc/google/callback?code=<valid>&state=tampered`
- **Expected:** 400 Bad Request; message `"Invalid state parameter"`; no token issued; audit `LoginAttempt/Failure/reason=csrf_mismatch`

#### TC-IDN-02-04: OIDC callback — provider returns error
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 1b
- **Pre:** N/A
- **Data:** `GET /api/v1/auth/oidc/google/callback?error=access_denied&state=<valid>`
- **Expected:** 401; FE redirect to login page với error banner; audit `LoginAttempt/Failure/reason=oidc_access_denied`

#### TC-IDN-02-05: OIDC token introspection failure
- **Pri:** P2 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** Provider down hoặc returns malformed id_token
- **Data:** Mock provider trả response không có `sub` claim
- **Expected:** 502 Bad Gateway; message `"OIDC provider error"`; no user provisioned; audit + alert dispatched

---

## UC-IDN-03: Bật MFA (TOTP/SMS)

Source: `uc-identity-core.md#uc-idn-03` — Endpoints `GET /me/mfa/setup` + `POST /me/mfa/confirm`.

#### TC-IDN-03-01: Setup TOTP — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-12
- **Pre:** User authenticated, MfaEnabled=false, tenant has TOTP provider configured
- **Data:** (1) `GET /api/v1/me/mfa/setup?method=totp` (2) Generate TOTP từ secret bằng test util (3) `POST /api/v1/me/mfa/confirm { method:"totp", verificationCode:"<6-digit>", backupCodesAcknowledged:true }`
- **Expected:** Step 1 returns `{ secret, qrCode (data:image/png), backupCodes:[8 codes] }`; Step 3 returns 200 OK; DB `MfaEnabled=true`, `MfaSecret` encrypted (not plaintext); 8 hashed rows in `MfaBackupCode`; audit `MfaEnforced/user_id/method=totp`

#### TC-IDN-03-02: Setup TOTP — verification code sai
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 7a
- **Pre:** Setup initiated (step 1 done), `MfaEnabled` still false
- **Data:** `POST /me/mfa/confirm { verificationCode:"000000" }` (random)
- **Expected:** 400 Bad Request; message `"Invalid verification code"`; counter incremented; MfaEnabled remains false

#### TC-IDN-03-03: Setup TOTP — clock drift tolerance ±30s
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 6b
- **Pre:** Setup initiated
- **Data:** Generate TOTP code from `now-30s` (previous step); submit
- **Expected:** 200 OK (accepted within ±1 step tolerance); MFA enabled

#### TC-IDN-03-04: Setup MFA — already enabled (idempotency)
- **Pri:** P2 | **Type:** Negative | **Layer:** Integration | **Trace:** precondition violation
- **Pre:** User already has MfaEnabled=true
- **Data:** `GET /me/mfa/setup?method=totp`
- **Expected:** 409 Conflict; message `"MFA already enabled. Disable first."`; existing secret not overwritten

#### TC-IDN-03-05: Setup — rate-limit 4th attempt in 5min returns 429
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** NFR rate-limit
- **Pre:** 3 setup attempts done within 5min
- **Data:** 4th `GET /me/mfa/setup`
- **Expected:** 429 Too Many Requests; `Retry-After` header set; audit `RateLimited`

---

## UC-IDN-04: Verify MFA challenge

Source: `uc-identity-core.md#uc-idn-04` — Endpoint `POST /api/v1/auth/mfa/verify`.

#### TC-IDN-04-01: Verify TOTP — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** UC-IDN-01-06 đã trả mfaToken; user MfaMethod=totp
- **Data:** `POST /api/v1/auth/mfa/verify { mfaToken:"<from-login>", code:"<valid-totp>" }`
- **Expected:** 200 OK; full `accessToken` issued; mfaToken invalidated (single-use); audit `MfaVerified/Success`

#### TC-IDN-04-02: Verify — backup code accepted (one-time)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** mfaToken issued, backup code `ABCD1234` still active in DB
- **Data:** `POST /auth/mfa/verify { mfaToken, code:"ABCD1234", method:"backup" }`
- **Expected:** 200 OK + accessToken; backup code marked `IsUsed=true` in DB; cannot reuse same code (verify TC-IDN-04-03)

#### TC-IDN-04-03: Verify — reused backup code rejected
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** ext 3a (replay protection)
- **Pre:** Backup code `ABCD1234` đã IsUsed=true
- **Data:** `POST /auth/mfa/verify` với same code
- **Expected:** 401 Unauthorized; audit `MfaVerify/Failure/reason=backup_code_reused`

#### TC-IDN-04-04: Verify — mfaToken expired
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** NFR exp
- **Pre:** mfaToken issued >5min ago
- **Data:** `POST /auth/mfa/verify` với expired mfaToken
- **Expected:** 401; message `"MFA token expired. Please log in again."`; FE redirect to login

#### TC-IDN-04-05: Verify — wrong code rate-limit (5 attempts)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 4a
- **Pre:** mfaToken valid, 4 wrong attempts done
- **Data:** 5th wrong TOTP submission
- **Expected:** 5th attempt: account temporarily locked 15min; 401 + lockout message; audit `MfaVerify/Failure/reason=max_attempts`

---

## UC-IDN-05: Đổi mật khẩu (self-service)

Source: `uc-identity-core.md#uc-idn-05` — Endpoint `POST /api/v1/me/change-password`.

#### TC-IDN-05-01: Change password — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User authenticated; current password = `OldPass@123`; new password meets policy
- **Data:** `POST /me/change-password { currentPassword:"OldPass@123", newPassword:"NewPass@456", confirmPassword:"NewPass@456" }`
- **Expected:** 200 OK; DB `PasswordHash` updated (bcrypt different); `PasswordChangedAt` set to now; `LoginFailureCount` reset to 0; audit `PasswordChanged`; old refresh tokens revoked (verify via re-login required)

#### TC-IDN-05-02: Change password — current sai
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** User authenticated
- **Data:** `POST /me/change-password { currentPassword:"WRONG", newPassword:"NewPass@456" }`
- **Expected:** 401 Unauthorized; message `"Current password incorrect"`; no DB change; audit `PasswordChangeFailed/reason=current_invalid`

#### TC-IDN-05-03: Change password — new policy violation (length<12)
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** User authenticated
- **Data:** `POST /me/change-password { newPassword:"short" }`
- **Expected:** 400 Bad Request; validation errors list (length, complexity, no-reuse rules); no DB change

#### TC-IDN-05-04: Change password — reuse last 5
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3b
- **Pre:** User has 5 historical password hashes
- **Data:** New password matches one of last 5 (test util reuses past)
- **Expected:** 400; message `"Cannot reuse one of last 5 passwords"`; no DB change

#### TC-IDN-05-05: Change password — confirm mismatch
- **Pri:** P2 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 1a (FE-side, BE re-validates)
- **Pre:** User authenticated
- **Data:** `newPassword="A"`, `confirmPassword="B"`
- **Expected:** 400; field error `"Passwords do not match"`

---

## UC-IDN-06: Quên mật khẩu

Source: `uc-identity-core.md#uc-idn-06` — Endpoints `POST /auth/forgot-password` + `POST /auth/reset-password`.

#### TC-IDN-06-01: Forgot password — email gửi thành công
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** User `user@gov.vn` exists, IsActive=true
- **Data:** `POST /auth/forgot-password { email:"user@gov.vn", tenantId:"tenant-a" }`
- **Expected:** 200 OK (always — no enumeration leak); reset token row in `PasswordResetToken` table (exp 1h); email queued to outbox với link `/reset-password?token=<jwt>`; audit `PasswordResetRequested`

#### TC-IDN-06-02: Forgot password — email không tồn tại
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 1a (anti-enumeration)
- **Pre:** No user `ghost@gov.vn`
- **Data:** `POST /auth/forgot-password { email:"ghost@gov.vn" }`
- **Expected:** 200 OK (same response, no leak); NO token created; NO email sent; audit `PasswordResetRequested/user_not_found` (internal only)

#### TC-IDN-06-03: Reset password — token hợp lệ
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 7-12
- **Pre:** Valid reset token from TC-IDN-06-01
- **Data:** `POST /auth/reset-password { token:"<jwt>", newPassword:"NewPass@789" }`
- **Expected:** 200 OK; password updated; token marked IsUsed=true (single-use); all sessions invalidated; audit `PasswordResetCompleted`

#### TC-IDN-06-04: Reset — token đã used
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 9a
- **Pre:** Token from TC-IDN-06-03 đã used
- **Data:** Re-submit same token
- **Expected:** 400; message `"Invalid or expired token"`; audit replay attempt

#### TC-IDN-06-05: Reset — token expired (>1h)
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 9b
- **Pre:** Token issued >1h ago
- **Data:** `POST /auth/reset-password` với expired token
- **Expected:** 400; message `"Invalid or expired token"`; password unchanged

---

## UC-IDN-07: Admin tạo người dùng

Source: `uc-identity-core.md#uc-idn-07` — Endpoint `POST /api/v1/admin/users`.

#### TC-IDN-07-01: Admin create user — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-8
- **Pre:** Admin authenticated với perm `USER_CREATE`; tenant active
- **Data:** `POST /admin/users { username:"newuser01", email:"new@gov.vn", fullName:"Nguyễn Văn A", deptCode:"DEPT001", roles:["EMPLOYEE"], tempPassword:"Temp@Pass1" }`
- **Expected:** 201 Created + Location header; DB user row + AspNetUserRoles row; `PasswordChangeRequired=true` flag; welcome email queued; audit `UserCreated/by=admin/target=newuser01`

#### TC-IDN-07-02: Admin create — username trùng
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** User `submitter01` exists
- **Data:** `POST /admin/users { username:"submitter01", ... }`
- **Expected:** 409 Conflict; message `"Username already taken"`; no DB row created

#### TC-IDN-07-03: Admin create — missing perm
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** Caller authenticated nhưng KHÔNG có `USER_CREATE`
- **Data:** Same body as TC-07-01
- **Expected:** 403 Forbidden; message `"Insufficient permissions"`; audit `AccessDenied/perm=USER_CREATE`

#### TC-IDN-07-04: Admin create — email validation fail
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 2b
- **Pre:** Admin authenticated
- **Data:** `email:"not-an-email"` invalid format
- **Expected:** 400; field error `email: "Invalid email format"`

#### TC-IDN-07-05: Admin create — role không tồn tại
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2c
- **Pre:** Admin authenticated
- **Data:** `roles:["NON_EXISTENT_ROLE"]`
- **Expected:** 400; message `"Role 'NON_EXISTENT_ROLE' does not exist in tenant"`; transaction rolled back

#### TC-IDN-07-06: Admin create — temp password policy violation
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 2d
- **Pre:** Admin authenticated
- **Data:** `tempPassword:"abc"` (too short)
- **Expected:** 400; validation errors per password policy; no DB write

---

## UC-IDN-08: Admin cập nhật người dùng

Source: `uc-identity-core.md#uc-idn-08` — Endpoint `PUT /api/v1/admin/users/{id}`.

#### TC-IDN-08-01: Admin update user — full update
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** User `submitter01` exists, admin có perm `USER_UPDATE`
- **Data:** `PUT /admin/users/{id} { fullName:"Updated Name", deptCode:"DEPT002", phoneNumber:"0901234567" }`
- **Expected:** 200 OK; DB row updated; `ModifiedAt` set; audit `UserUpdated/by=admin/target=submitter01/changes={fullName,deptCode,phoneNumber}`

#### TC-IDN-08-02: Admin update — user không tồn tại
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** No user with id `non-existent-guid`
- **Data:** `PUT /admin/users/non-existent-guid {...}`
- **Expected:** 404 Not Found; message `"User not found"`

#### TC-IDN-08-03: Admin update — cross-tenant attempt
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (memory anchor 260525-E)
- **Pre:** Admin in `tenant-a`; user `submitter01` in `tenant-b`
- **Data:** `PUT /admin/users/{submitter01-id}` từ tenant-a admin
- **Expected:** 404 (not 403 — avoid info leak across tenants); audit `AccessDenied/cross_tenant`

#### TC-IDN-08-04: Admin update — concurrent edit (optimistic lock)
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** User `submitter01` `RowVersion=5`; admin1 fetched at v5
- **Data:** Admin2 updated to v6; admin1 submits với `If-Match: "5"` header
- **Expected:** 409 Conflict; message `"Resource modified by another user"`; admin1 forced to re-fetch

#### TC-IDN-08-05: Admin update — change email triggers verification
- **Pri:** P2 | **Type:** Positive | **Layer:** Integration | **Trace:** ext 4a
- **Pre:** Admin authenticated
- **Data:** `PUT /admin/users/{id} { email:"new-email@gov.vn" }`
- **Expected:** 200; DB `EmailVerified=false`; verification email queued; audit `EmailChangeRequested`

---

## UC-IDN-09: Admin vô hiệu hóa người dùng

Source: `uc-identity-core.md#uc-idn-09` — Endpoint `POST /api/v1/admin/users/{id}/disable`.

#### TC-IDN-09-01: Disable user — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User `submitter01` IsActive=true; admin có perm `USER_DISABLE`
- **Data:** `POST /admin/users/{id}/disable { reason:"Resigned" }`
- **Expected:** 200 OK; DB `IsActive=false`, `DisabledAt`, `DisabledReason`; all sessions revoked (Redis cleared + refresh tokens invalidated); subsequent login attempts → 403; audit `UserDisabled/by=admin/reason=Resigned`

#### TC-IDN-09-02: Disable — self-disable blocked
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Admin authenticated as own user
- **Data:** `POST /admin/users/{own-id}/disable`
- **Expected:** 400; message `"Cannot disable own account"`

#### TC-IDN-09-03: Disable — last admin in tenant blocked
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 2b
- **Pre:** Only 1 user với role `TENANT_ADMIN` left
- **Data:** Super-admin attempts to disable that user
- **Expected:** 400; message `"Cannot disable last tenant admin"`

#### TC-IDN-09-04: Disable user có active workflow tasks
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 5a (impact analysis)
- **Pre:** User assigned 3 active WF tasks
- **Data:** `POST /admin/users/{id}/disable`
- **Expected:** 200 (disable proceeds); domain event `UserDisabledEvent` published; workflow module re-assigns tasks (eventual via Outbox); audit + alert to PM

---

## UC-IDN-10: Gán vai trò

Source: `uc-identity-core.md#uc-idn-10` — Endpoint `POST /api/v1/admin/users/{id}/roles`.

#### TC-IDN-10-01: Assign role — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** User `submitter01`; role `APPROVER_L1` exists; admin có `ROLE_ASSIGN`
- **Data:** `POST /admin/users/{id}/roles { roles:["APPROVER_L1"] }`
- **Expected:** 200; row in `AspNetUserRoles` created; user's effective perms recomputed (cache invalidated); audit `RoleAssigned/target=submitter01/role=APPROVER_L1`

#### TC-IDN-10-02: Assign role — SoD violation
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** ext 3a (SoD)
- **Pre:** User has `REQUESTER`; SoD rule forbids both `REQUESTER`+`APPROVER_L1`
- **Data:** `POST /admin/users/{id}/roles { roles:["APPROVER_L1"] }`
- **Expected:** 409 Conflict; message `"SoD violation: REQUESTER and APPROVER_L1 are mutually exclusive"`; no DB change; audit `SoDViolation`

#### TC-IDN-10-03: Assign role — idempotent (already has role)
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** idempotency
- **Pre:** User already has `APPROVER_L1`
- **Data:** Same assign call
- **Expected:** 200 OK; no duplicate row created; no audit row (or audit `NoOp` if convention requires)

#### TC-IDN-10-04: Revoke role — happy path
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** alt — `DELETE /admin/users/{id}/roles/{roleCode}`
- **Pre:** User has `APPROVER_L1`
- **Data:** `DELETE /admin/users/{id}/roles/APPROVER_L1`
- **Expected:** 200; row removed; perm cache invalidated; audit `RoleRevoked`

#### TC-IDN-10-05: Assign — role không tồn tại trong tenant
- **Pri:** P2 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** No role `FAKE_ROLE` in tenant
- **Data:** `POST /admin/users/{id}/roles { roles:["FAKE_ROLE"] }`
- **Expected:** 400; message `"Role 'FAKE_ROLE' not found"`

---

## UC-IDN-11: Tạo nhóm + gán thành viên

Source: `uc-identity-core.md#uc-idn-11` — Endpoints `POST /admin/groups` + `POST /admin/groups/{id}/members`.

#### TC-IDN-11-01: Create group — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-4
- **Pre:** Admin có `GROUP_CREATE`
- **Data:** `POST /admin/groups { code:"FINANCE_REVIEWERS", name:"Tài chính - Reviewers", ouCode:"FIN" }`
- **Expected:** 201 + Location; DB row in `Groups`; audit `GroupCreated`

#### TC-IDN-11-02: Create group — code trùng
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Group `FINANCE_REVIEWERS` exists
- **Data:** Same code
- **Expected:** 409 Conflict; message `"Group code already exists in tenant"`

#### TC-IDN-11-03: Add members to group — happy path
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 5-8
- **Pre:** Group exists; 3 users exist
- **Data:** `POST /admin/groups/{id}/members { userIds:[u1, u2, u3] }`
- **Expected:** 200; 3 rows in `UserGroupMembership` với `IsActive=true`; workflow predicate-pool resolver picks new members on next dispatch (verify via UC-WF complementary TC); audit `GroupMembershipChanged`

#### TC-IDN-11-04: Add member — user khác tenant
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary
- **Pre:** Group in tenant-a; user in tenant-b
- **Data:** Cross-tenant user-id
- **Expected:** 404 (not 403, anti-leak); audit cross-tenant attempt

#### TC-IDN-11-05: Remove member — happy path
- **Pri:** P2 | **Type:** Positive | **Layer:** Integration | **Trace:** alt — `DELETE /admin/groups/{id}/members/{userId}`
- **Pre:** User u1 trong group
- **Data:** `DELETE`
- **Expected:** 200; row soft-deleted (`IsActive=false`) hoặc hard-deleted per design; perm cache invalidated cho u1

---

## UC-IDN-12: Cấp permission cho role

Source: `uc-identity-core.md#uc-idn-12` — Endpoint `POST /api/v1/admin/roles/{code}/permissions`.

#### TC-IDN-12-01: Grant perm — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** Role `APPROVER_L1` exists; perm `WF_TASK_APPROVE` canonical (uppercase)
- **Data:** `POST /admin/roles/APPROVER_L1/permissions { perms:["WF_TASK_APPROVE"] }`
- **Expected:** 200; row in `RolePermissions` với `PermCode=WF_TASK_APPROVE`; all users with role: perm cache invalidated; audit `PermissionGranted/role=APPROVER_L1/perm=WF_TASK_APPROVE`

#### TC-IDN-12-02: Grant perm — lowercase normalized to upper
- **Pri:** P0 | **Type:** Boundary | **Layer:** Integration | **Trace:** canonicalization (memory anchor I8 from p95)
- **Pre:** Role exists
- **Data:** `POST` với `perms:["wf_task_approve"]` (lowercase)
- **Expected:** 200; DB `PermCode=WF_TASK_APPROVE` (uppercase); contract test of canonicalization

#### TC-IDN-12-03: Grant perm — perm code không tồn tại
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** No perm code `FAKE_PERM`
- **Data:** `perms:["FAKE_PERM"]`
- **Expected:** 400; message `"Permission 'FAKE_PERM' is not registered"`

#### TC-IDN-12-04: Revoke perm — happy path
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** alt — `DELETE /admin/roles/{code}/permissions/{perm}`
- **Pre:** Role has perm
- **Data:** `DELETE`
- **Expected:** 200; row removed; cache invalidated; audit `PermissionRevoked`

#### TC-IDN-12-05: Grant — system role protected
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** ext 2b
- **Pre:** Role `SYSTEM` (IsSystem=true) — immutable
- **Data:** `POST /admin/roles/SYSTEM/permissions {...}`
- **Expected:** 403 Forbidden; message `"Cannot modify system role"`

---

## UC-IDN-13: Xem profile + effective permissions

Source: `uc-identity-core.md#uc-idn-13` — Endpoints `GET /api/v1/me` + `GET /api/v1/me/permissions`.

#### TC-IDN-13-01: Get me — basic profile
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-3
- **Pre:** User authenticated
- **Data:** `GET /api/v1/me`
- **Expected:** 200 OK; body `{ id, username, email, fullName, deptCode, tenantId, roles:[...], mfaEnabled, lastLoginAt }`; no sensitive fields (PasswordHash, MfaSecret) leaked

#### TC-IDN-13-02: Get effective permissions — direct role
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 4-7
- **Pre:** User has role `APPROVER_L1` with perm `WF_TASK_APPROVE`
- **Data:** `GET /api/v1/me/permissions`
- **Expected:** 200; body `{ permissions:[ {code:"WF_TASK_APPROVE", source:"role:APPROVER_L1"} ] }`; cached for 5min

#### TC-IDN-13-03: Get effective permissions — group-inherited
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** ext 5a (group inheritance)
- **Pre:** User in group with role `APPROVER_L2`; group has perm `WF_TASK_RECALL`
- **Data:** `GET /me/permissions`
- **Expected:** 200; response includes perm với `source:"group:FINANCE_REVIEWERS/role:APPROVER_L2"`; merged set distinct

#### TC-IDN-13-04: Effective perms — cache invalidation on role change
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** NFR caching
- **Pre:** Cached perms exist
- **Data:** (1) GET /me/permissions → cached (2) Admin grants new perm to role (3) GET /me/permissions immediately
- **Expected:** Step 3 reflects new perm (cache invalidated by domain event handler); no >1 second staleness

#### TC-IDN-13-05: Get me — unauthenticated
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** No Authorization header
- **Data:** `GET /api/v1/me`
- **Expected:** 401; no info leak

---

## Test Data Requirements

Để chạy TC suite trên cần seed:

| Entity | Records | Notes |
|--------|---------|-------|
| Tenants | tenant-a, tenant-b | Cross-tenant isolation tests |
| Users | submitter01, auditor01, admin01, ghost cases | Different roles + MFA states |
| Roles | EMPLOYEE, APPROVER_L1, APPROVER_L2, TENANT_ADMIN, SYSTEM | SoD rules registered |
| Permissions | WF_TASK_APPROVE, WF_TASK_RECALL, USER_CREATE, USER_UPDATE, USER_DISABLE, ROLE_ASSIGN, GROUP_CREATE | Canonical uppercase |
| Groups | FINANCE_REVIEWERS | At least 1 group with members |
| SoD Rules | REQUESTER ⊥ APPROVER_L1 | Mutually exclusive sample |

Tham chiếu `tests/AqtCoreFW.Tests.Integration/Infrastructure/Seeders/E2ETestDataSeeder.cs` để mở rộng seed nếu thiếu.

---

## Execution Notes

- **Manual run:** Postman collection import từ Swagger `/swagger/v1/swagger.json`, chạy TC theo thứ tự (TC-IDN-01-01 → TC-IDN-13-05)
- **Automation roadmap:** TC này là input cho dev viết xUnit integration tests trong Phase 2 — mỗi TC ID maps 1-1 sang `[Fact]` hoặc `[Theory]` method name
- **Cleanup:** Mỗi TC tự cleanup DB qua `IClassFixture<DatabaseFixture>` reset hoặc rollback transaction
- **Idempotency:** TC liên quan rate-limit/lockout cần reset counter trước; nhóm theo `[Collection]` để serialize

## Unresolved Questions

1. **TC-IDN-09-04** (Disable user có active WF tasks): Hành vi cuối — re-assign vs throw 409? Hiện UC nói "domain event published" nhưng không rõ FE/Admin có nhận được warning trước hay tự động re-assign. Cần product confirm.
2. **TC-IDN-10-03** (Idempotent role assign): Audit row có ghi `NoOp` hay không? Convention chưa thống nhất qua memory.
3. **TC-IDN-12-02** (Lowercase canonicalization): Đã có test I8 ship trong v2.137p95 ([[p7-cleanup-batches-1-and-2-260522]] memory) — có thể đã cover; verify trước khi viết duplicate test code Phase 2.
4. **TC count drift:** UC-IDN-13 step 5a (group-inherited) chỉ đề cập "merged set" mà không nêu thứ tự ưu tiên khi role + group cùng grant cùng perm. Hành vi expected có overlap row hay deduplicate? Cần kiểm tra `IEffectivePermissionService` source.
