---
title: Test Cases - Identity Module (Advanced)
snapshot_commit: 387b6254
snapshot_version: v2.137p113
date: 2026-05-26
ucs_covered: 11
tcs_total: 55
trace_source: docs/srs/use-cases/uc-identity-advanced.md
---

# Test Cases — Identity Module (Advanced)

Đặc tả test case (IEEE 829-style compact) cho 11 use case nâng cao của Identity module. Mỗi TC trace ngược về UC source + step number, dùng cho QA execution thủ công và làm input cho dev viết test code automation sau.

## Conventions

- **TC-ID format:** `TC-IDN-A0X-NN` — Module-IDN, UC số A01-A11, TC seq 01-NN
- **Priority:** P0=Critical | P1=High (admin ops / security enforcement) | P2=Medium (self-service / stub)
- **Type:** Positive (happy path) | Negative (error path) | Boundary (limit values) | Security (auth/authz) | Performance (latency NFR)
- **Layer:** Unit | Integration (DB+HTTP) | E2E (FE+BE+infra) | Manual (UX/visual)
- **Trace:** UC-IDN-AXX step Y-Z or ext Xa links back to source UC scenario step

## Coverage Summary

| UC | Title | TCs | P0 | P1 | P2 |
|----|-------|-----|----|----|----|
| UC-IDN-A01 | Define ABAC Rule | 5 | 1 | 3 | 1 |
| UC-IDN-A02 | Evaluate Effective Permission (RBAC ∪ ABAC ∪ Group) | 5 | 2 | 2 | 1 |
| UC-IDN-A03 | Define SoD Rule | 5 | 1 | 3 | 1 |
| UC-IDN-A04 | Detect + Prevent SoD Violation | 5 | 2 | 2 | 1 |
| UC-IDN-A05 | Delegation (Temporary) | 5 | 1 | 3 | 1 |
| UC-IDN-A06 | Revoke Delegation | 5 | 1 | 3 | 1 |
| UC-IDN-A07 | Session Management + Force Logout | 5 | 2 | 2 | 1 |
| UC-IDN-A08 | Digital Signature (stub) | 5 | 1 | 2 | 2 |
| UC-IDN-A09 | Link External Identity (Federated) | 5 | 1 | 3 | 1 |
| UC-IDN-A10 | Configure JIT Provisioning | 5 | 1 | 3 | 1 |
| UC-IDN-A11 | Periodic Access Review Cycle | 5 | 2 | 2 | 1 |
| **Total** | | **55** | **15** | **27** | **11** |

---

## UC-IDN-A01: Định nghĩa quy tắc ABAC

Source: `uc-identity-advanced.md#uc-idn-a01` — Endpoint `POST /api/v1/admin/abac-rules`.

#### TC-IDN-A01-01: Create ABAC rule — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-9
- **Pre:** Admin `admin01` authenticated, has `IDN_ABAC_RULES_MANAGE`, tenant-a active, no rule named "ApproveHighValue"
- **Data:** `POST /api/v1/admin/abac-rules { "name":"ApproveHighValue", "description":"...", "attributes":{ "user.department":"Finance","document.amount":{"$lte":10000} }, "effect":"ALLOW" }`
- **Expected:** 201 Created; DB row `AbacPolicyRule` (Id=GUID, TenantId=tenant-a, Name="ApproveHighValue", Effect="ALLOW", AttributesJson non-empty); audit row `AbacRuleCreatedEvent(rule_id, creator_id=admin01)`; response body contains `id` + `createdDate`

#### TC-IDN-A01-02: Create ABAC rule — invalid attribute syntax
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 6a
- **Pre:** Admin authenticated, has perm
- **Data:** `POST /api/v1/admin/abac-rules { "attributes": { "user.department": { "$unknownOp": "x" } }, "effect":"ALLOW" }`
- **Expected:** 400 Bad Request; message `"Invalid attribute expression"`; no DB row created; no audit row

#### TC-IDN-A01-03: Create ABAC rule — missing permission (403)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz precondition
- **Pre:** Caller authenticated but does NOT have `IDN_ABAC_RULES_MANAGE`
- **Data:** Same valid body as TC-A01-01
- **Expected:** 403 Forbidden; message `"Insufficient permissions"`; audit `AccessDenied/perm=IDN_ABAC_RULES_MANAGE`

#### TC-IDN-A01-04: Create ABAC rule — duplicate name blocked
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** precondition (name unique)
- **Pre:** Rule "ApproveHighValue" already exists in tenant-a
- **Data:** `POST` with same `"name":"ApproveHighValue"`
- **Expected:** 409 Conflict; message `"Rule name already exists in tenant"`; no duplicate DB row

#### TC-IDN-A01-05: Create ABAC rule — performance NFR <200ms
- **Pri:** P2 | **Type:** Performance | **Layer:** Integration | **Trace:** NFR performance
- **Pre:** Admin authenticated, tenant has 0 existing ABAC rules
- **Data:** Valid rule body; measure wall-clock from request send to 201 response
- **Expected:** Response time <200ms (p99 on local infra); 201 with correct payload

---

## UC-IDN-A02: Đánh giá Effective Permission (RBAC ∪ ABAC ∪ Group)

Source: `uc-identity-advanced.md#uc-idn-a02` — Endpoint `GET /api/v1/me/permissions` (public); Internal `IEffectivePermissionService`.

#### TC-IDN-A02-01: Effective perms — RBAC ∪ Group ∪ ABAC all contribute
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User `certifier01` has direct role `APPROVER_L1` (perm=`WF_TASK_APPROVE`); is member of group `FINANCE_REVIEWERS` (perm=`WF_TASK_RECALL`); ABAC rule "ApproveHighValue" active with context `{user.department=Finance}`
- **Data:** Internal call `IEffectivePermissionService.GetUserEffectivePermissionsAsync(certifier01, tenant-a, context={dept:"Finance"})` OR `GET /api/v1/me/permissions` as certifier01
- **Expected:** 200; response `{ directPermissions:["WF_TASK_APPROVE"], groupPermissions:["WF_TASK_RECALL"], abacPermissions:["ABAC_APPROVE_HIGH_VALUE"], effectiveUnion:[...] }`; union is deduplicated; audit row for high-risk evaluation present

#### TC-IDN-A02-02: ABAC fail-safe — missing context attribute skips rule
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** ext 5a
- **Pre:** ABAC rule requires `document.amount ≤ 10000`; call context does NOT contain `document.amount`
- **Data:** Internal call with context `{ "user.department": "Finance" }` only (no amount)
- **Expected:** Rule skipped (not granted); response does NOT include abac permission; warning log entry present (observational); no 500; ABAC set = ∅ for that rule

#### TC-IDN-A02-03: RBAC only — no groups, no ABAC rules configured
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** User `submitter01` has role `EMPLOYEE` (perm=`FORM_SUBMIT`); no groups, no ABAC rules in tenant-a
- **Data:** `GET /api/v1/me/permissions` as submitter01
- **Expected:** 200; `{ directPermissions:["FORM_SUBMIT"], groupPermissions:[], abacPermissions:[], effectiveUnion:["FORM_SUBMIT"] }`; single source attribution `"role:EMPLOYEE"`

#### TC-IDN-A02-04: ABAC evaluation — ≤10 rules within <500ms
- **Pri:** P1 | **Type:** Performance | **Layer:** Integration | **Trace:** NFR performance (≤10 rules <500ms)
- **Pre:** Tenant has 10 ABAC rules; user has 2 roles; 1 group membership
- **Data:** `GET /api/v1/me/permissions`; measure response time
- **Expected:** Response <500ms (p99); all 10 rules evaluated; applicable rules' perms in response

#### TC-IDN-A02-05: Context attribute injection attempt rejected
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** NFR security (context validation)
- **Pre:** ABAC rule checks `user.department = "Finance"` for elevated perm
- **Data:** Craft request injecting `user.department=Finance` in query param or header when caller is not in Finance dept
- **Expected:** 200 but ABAC rule NOT triggered for injected value (system uses server-side user attributes, not caller-supplied); injected attribute ignored; no elevation

---

## UC-IDN-A03: Định nghĩa quy tắc Separation of Duties (SoD)

Source: `uc-identity-advanced.md#uc-idn-a03` — Endpoint `POST /api/v1/admin/sod-rules`.

#### TC-IDN-A03-01: Create SoD rule — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-8
- **Pre:** Admin `admin01` has `IDN_SOD_RULES_MANAGE`; roles `BTC_DEPT_DIRECTOR` + `BTC_AUDITOR` exist in tenant-a; no existing SoD rule with same name
- **Data:** `POST /api/v1/admin/sod-rules { "name":"DirectorAuditorSoD", "roleIds":["BTC_DEPT_DIRECTOR","BTC_AUDITOR"], "allowBoth":false, "reason":"Dept director cannot audit own dept" }`
- **Expected:** 201 Created; DB row `SodRule` (TenantId=tenant-a, ConflictingRoles includes both ids, AllowBoth=false); audit `SodRuleCreatedEvent(rule_id, creator_id=admin01, roles)`

#### TC-IDN-A03-02: Create SoD rule — invalid role ID
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 5a
- **Pre:** Admin authenticated, has perm
- **Data:** `POST /api/v1/admin/sod-rules { "roleIds":["NON_EXISTENT_ROLE","BTC_AUDITOR"], ... }`
- **Expected:** 400 Bad Request; message indicates invalid role ID; no DB row created

#### TC-IDN-A03-03: Create SoD rule — missing perm (403)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz precondition
- **Pre:** Caller does NOT have `IDN_SOD_RULES_MANAGE`
- **Data:** Valid body
- **Expected:** 403 Forbidden; audit `AccessDenied/perm=IDN_SOD_RULES_MANAGE`

#### TC-IDN-A03-04: Create SoD rule — duplicate name blocked
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** precondition (name unique)
- **Pre:** SoD rule "DirectorAuditorSoD" already exists in tenant-a
- **Data:** `POST` with same `"name":"DirectorAuditorSoD"`
- **Expected:** 409 Conflict; message `"SoD rule name already exists in tenant"`; no duplicate row

#### TC-IDN-A03-05: Create SoD rule — minimum 2 roles required
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 5 (validate role IDs)
- **Pre:** Admin authenticated
- **Data:** `POST /api/v1/admin/sod-rules { "roleIds":["BTC_DEPT_DIRECTOR"] }` (only 1 role)
- **Expected:** 400; message `"SoD rule requires at least 2 conflicting roles"`; no DB write

---

## UC-IDN-A04: Phát hiện + phòng chặn SoD violation

Source: `uc-identity-advanced.md#uc-idn-a04` — Endpoint integrated into `POST /api/v1/admin/users/{userId}/roles`.

#### TC-IDN-A04-01: SoD violation detected — assignment blocked
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** step 2-5
- **Pre:** SoD rule "DirectorAuditorSoD" exists (BTC_DEPT_DIRECTOR ⊥ BTC_AUDITOR); user `mgr01` already has `BTC_DEPT_DIRECTOR`; admin has `IDN_ROLE_UPDATE`
- **Data:** `POST /api/v1/admin/users/{mgr01-id}/roles { "roles":["BTC_AUDITOR"] }`
- **Expected:** 409 Conflict; message `"Cannot assign role: Conflicts with existing role BTC_DEPT_DIRECTOR (per SoD rule: DirectorAuditorSoD)"`; no `AspNetUserRoles` row created; audit `SodViolationDetectedEvent(user_id=mgr01, proposed_role=BTC_AUDITOR, conflicting_role=BTC_DEPT_DIRECTOR, rule_id=...)`

#### TC-IDN-A04-02: No SoD conflict — assignment proceeds normally
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 2-4 (no conflict branch)
- **Pre:** User `submitter01` has role `EMPLOYEE`; no SoD rule conflicts with `APPROVER_L1`
- **Data:** `POST /api/v1/admin/users/{submitter01-id}/roles { "roles":["APPROVER_L1"] }`
- **Expected:** 200 OK; role assigned; no SoD audit event; perm cache invalidated

#### TC-IDN-A04-03: Admin cannot override SoD block (design enforcement)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** NFR security (admin cannot override)
- **Pre:** Same setup as TC-A04-01; caller is TenantAdmin (highest privilege)
- **Data:** Same 409-inducing request; attempt with any admin-override header/flag
- **Expected:** 409 Conflict regardless of caller privilege level; no bypass mechanism exists; design enforces hard block

#### TC-IDN-A04-04: SoD evaluation performance <100ms
- **Pri:** P1 | **Type:** Performance | **Layer:** Integration | **Trace:** NFR performance (<100ms)
- **Pre:** Tenant has 5 SoD rules; user has 3 existing roles; no conflict in this assignment
- **Data:** `POST` valid role assignment; measure wall-clock
- **Expected:** Response time including SoD evaluation <100ms; assignment succeeds

#### TC-IDN-A04-05: SoD — multi-role assignment checks all candidates
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 3 (evaluate all proposed roles)
- **Pre:** User `submitter01` has `EMPLOYEE`; SoD rule: `APPROVER_L1 ⊥ AUDITOR`
- **Data:** `POST /api/v1/admin/users/{id}/roles { "roles":["APPROVER_L1","AUDITOR"] }` (both conflicting roles in one request)
- **Expected:** 409; message identifies the conflicting pair from proposed set; no partial assignment (transaction rollback)

---

## UC-IDN-A05: Ủy quyền tạm thời (Delegation)

Source: `uc-identity-advanced.md#uc-idn-a05` — Endpoint `POST /api/v1/delegations`.

#### TC-IDN-A05-01: Create delegation — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-11
- **Pre:** Delegator `mgr01` authenticated, has effective perms `IDN_TASK_APPROVE`+`FORM_DATA_APPROVE`; delegatee `staff01` exists, IsActive=true; dateRange 2026-06-01 to 2026-06-30 (future, valid)
- **Data:** `POST /api/v1/delegations { "delegateeId":"staff01-id", "permissionCodes":["IDN_TASK_APPROVE","FORM_DATA_APPROVE"], "startDate":"2026-06-01", "endDate":"2026-06-30", "reason":"Leave of absence" }`
- **Expected:** 201 Created; DB row `Delegation` (DelegatorId=mgr01, DelegateeId=staff01, PermissionCodes JSON, StartDate, EndDate, IsActive=true); audit `DelegationCreatedEvent(delegator_id=mgr01, delegatee_id=staff01, permissions, date_range)`; notification queued to staff01

#### TC-IDN-A05-02: Create delegation — delegator lacks permission to delegate
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 7a
- **Pre:** Delegator `submitter01` does NOT have `IDN_TASK_APPROVE` in effective perms
- **Data:** `POST /api/v1/delegations { "permissionCodes":["IDN_TASK_APPROVE"], ... }`
- **Expected:** 403 Forbidden; message `"You don't have permission to delegate IDN_TASK_APPROVE"`; no DB row

#### TC-IDN-A05-03: Create delegation — delegatee in different tenant (cross-tenant blocked)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** step 6 validate delegatee + tenant boundary (memory anchor 260525-E)
- **Pre:** Delegator in tenant-a; delegatee user-id belongs to tenant-b
- **Data:** `POST /api/v1/delegations { "delegateeId":"<tenant-b-user-id>", ... }`
- **Expected:** 404 Not Found (not 403 — anti-leak cross-tenant); no DB row; audit cross-tenant attempt

#### TC-IDN-A05-04: Create delegation — past end date rejected
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 8 (validate dateRange)
- **Pre:** Delegator authenticated
- **Data:** `POST /api/v1/delegations { "startDate":"2025-01-01", "endDate":"2025-01-31", ... }` (past)
- **Expected:** 400 Bad Request; message `"End date must be in the future"`; no DB row

#### TC-IDN-A05-05: Create delegation — start > end date rejected
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 8 (start < end invariant)
- **Pre:** Delegator authenticated
- **Data:** `POST /api/v1/delegations { "startDate":"2026-07-01", "endDate":"2026-06-01", ... }` (inverted)
- **Expected:** 400; message `"Start date must be before end date"`; no DB row

---

## UC-IDN-A06: Thu hồi ủy quyền

Source: `uc-identity-advanced.md#uc-idn-a06` — Endpoint `DELETE /api/v1/delegations/{delegationId}`.

#### TC-IDN-A06-01: Revoke delegation — delegator self-revokes
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-9
- **Pre:** Delegation from `mgr01` to `staff01` exists, IsActive=true, endDate in future
- **Data:** `DELETE /api/v1/delegations/{delegationId}` as mgr01
- **Expected:** 204 No Content; DB `Delegation.IsActive=false`, `RevokedDate=now`, `RevokedBy=mgr01`; notification queued to staff01; audit `DelegationRevokedEvent(delegator_id=mgr01, delegatee_id=staff01, revokedBy=mgr01)` ; delegatee immediately loses delegated perms on next permission eval

#### TC-IDN-A06-02: Revoke delegation — admin force-revokes (not delegator)
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 5 (admin secondary actor)
- **Pre:** Delegation exists; caller is TenantAdmin (not the delegator), has `IDN_SESSION_TERMINATE`
- **Data:** `DELETE /api/v1/delegations/{delegationId}` as admin01
- **Expected:** 204; `RevokedBy=admin01`; audit includes admin identity; notification to both parties

#### TC-IDN-A06-03: Revoke delegation — unauthorized third party blocked
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 5a
- **Pre:** Delegation from mgr01 to staff01; caller is `other_user` (neither delegator nor admin)
- **Data:** `DELETE /api/v1/delegations/{delegationId}` as other_user
- **Expected:** 403 Forbidden; message `"Not authorized to revoke"`; delegation unchanged

#### TC-IDN-A06-04: Revoke delegation — already expired (idempotent)
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 4 (validate not expired)
- **Pre:** Delegation endDate already passed (naturally expired, IsActive=false)
- **Data:** `DELETE /api/v1/delegations/{delegationId}` as mgr01
- **Expected:** 400 or 409; message `"Delegation already expired or revoked"`; no state change; no duplicate audit

#### TC-IDN-A06-05: Revoke delegation — ID not found
- **Pri:** P2 | **Type:** Negative | **Layer:** Integration | **Trace:** step 4 (validate exists)
- **Pre:** No delegation with given ID in tenant-a
- **Data:** `DELETE /api/v1/delegations/non-existent-guid`
- **Expected:** 404 Not Found; message `"Delegation not found"`

---

## UC-IDN-A07: Quản trị phiên (Session Management + Force Logout)

Source: `uc-identity-advanced.md#uc-idn-a07` — Endpoints `GET /api/v1/admin/sessions` + `DELETE /api/v1/admin/sessions/{sessionId}`.

#### TC-IDN-A07-01: List sessions + revoke — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-12
- **Pre:** Admin `admin01` has `IDN_SESSION_TERMINATE`; user `submitter01` has 2 active sessions in SessionToken table
- **Data:** (1) `GET /api/v1/admin/sessions?userId={submitter01-id}` (2) `DELETE /api/v1/admin/sessions/{sessionId}`
- **Expected:** Step 1: 200 + paginated list with SessionId, UserId, LoginTime, IpAddress, Status=active; Step 2: 204; DB `SessionToken.Status=revoked`, `RevokedDate=now`, `RevokedBy=admin01`; sessionId added to Redis blocklist; audit `SessionTerminatedEvent(session_id, user_id=submitter01, reason=admin_revoke)`

#### TC-IDN-A07-02: Revoked token rejected on next API call
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** step 13
- **Pre:** Session revoked per TC-A07-01; submitter01 still holds JWT from that session
- **Data:** `GET /api/v1/me` with revoked JWT in Authorization header
- **Expected:** 401 Unauthorized; message `"Session revoked"`; blocklist check fires before handler; no resource data returned

#### TC-IDN-A07-03: List sessions — missing perm (403)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz precondition
- **Pre:** Caller authenticated but does NOT have `IDN_SESSION_TERMINATE`
- **Data:** `GET /api/v1/admin/sessions`
- **Expected:** 403 Forbidden; audit `AccessDenied/perm=IDN_SESSION_TERMINATE`

#### TC-IDN-A07-04: Revoke session — cross-tenant admin attempt
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (memory anchor 260525-E)
- **Pre:** Admin in tenant-a; sessionId belongs to user in tenant-b
- **Data:** `DELETE /api/v1/admin/sessions/{tenant-b-session-id}` from tenant-a admin
- **Expected:** 404 Not Found (not 403 — anti-leak); session in tenant-b unchanged; audit cross-tenant attempt

#### TC-IDN-A07-05: List sessions — pagination works, performance <500ms
- **Pri:** P2 | **Type:** Performance | **Layer:** Integration | **Trace:** NFR performance (<500ms list)
- **Pre:** Admin authenticated; tenant has 200 active sessions
- **Data:** `GET /api/v1/admin/sessions?page=1&pageSize=20`; measure response time
- **Expected:** Response <500ms; body has 20 results + totalCount=200 + pagination metadata; no full scan without filter

---

## UC-IDN-A08: Ký số tài liệu (Digital Signature stub)

Source: `uc-identity-advanced.md#uc-idn-a08` — Endpoints `POST /api/v1/admin/signature-providers` + `POST /api/v1/signature/sign`.

#### TC-IDN-A08-01: Configure signature provider — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** Admin `admin01` has `IDN_SIGNATURE_CONFIG`; no provider "smartcard-01" configured yet
- **Data:** `POST /api/v1/admin/signature-providers { "name":"smartcard-01", "provider":"smartcard", "thumbprint":"AA:BB:CC:DD" }`
- **Expected:** 201 Created; DB row `SignatureProviderConfig`; audit `SignatureProviderConfiguredEvent`

#### TC-IDN-A08-02: Sign document — happy path
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 6-13
- **Pre:** User `approver01` authenticated with approval permission; provider "smartcard-01" configured; document "doc-guid-01" exists
- **Data:** `POST /api/v1/signature/sign { "documentId":"doc-guid-01", "providerId":"smartcard-01" }`
- **Expected:** 200 OK; DB row `DigitalSignature` (DocumentId, UserId=approver01, SignatureBytes non-empty, ProviderId, SignedDate, Thumbprint); audit `DocumentSignedEvent(user_id=approver01, document_id, provider, timestamp)`; response includes `{ signatureId, signedAt, thumbprint }`

#### TC-IDN-A08-03: Sign document — provider unavailable (503)
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 9a
- **Pre:** Provider configured but `ISignatureProvider` returns unavailable (mock/stub returns 503 scenario)
- **Data:** `POST /api/v1/signature/sign { "documentId":"doc-guid-02", "providerId":"smartcard-01" }`
- **Expected:** 503 Service Unavailable; message `"Signature provider unavailable"`; no `DigitalSignature` row created; error logged

#### TC-IDN-A08-04: Configure provider — missing perm (403)
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz (IDN_SIGNATURE_CONFIG)
- **Pre:** Caller does NOT have `IDN_SIGNATURE_CONFIG`
- **Data:** `POST /api/v1/admin/signature-providers { ... }`
- **Expected:** 403 Forbidden; audit `AccessDenied/perm=IDN_SIGNATURE_CONFIG`

#### TC-IDN-A08-05: Sign document — unauthenticated caller
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz precondition
- **Pre:** No Authorization header
- **Data:** `POST /api/v1/signature/sign { "documentId":"doc-guid-01", "providerId":"smartcard-01" }`
- **Expected:** 401 Unauthorized; no DigitalSignature row created; no provider called

---

## UC-IDN-A09: Liên kết External Identity (Federated Accounts)

Source: `uc-identity-advanced.md#uc-idn-a09` — Endpoints `GET /api/v1/external-identities/link-authorize` + `GET /api/v1/external-identities/link-callback`.

#### TC-IDN-A09-01: Link external account — Google happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-11
- **Pre:** User `user01` authenticated locally in tenant-a; no Google identity linked; Google sub `google-sub-999` not linked to any other user
- **Data:** (1) `GET /api/v1/external-identities/link-authorize?provider=google` → OIDC redirect (2) `GET /api/v1/external-identities/link-callback?code=<valid>&state=<csrf>` after IdP auth
- **Expected:** 200 OK; DB row `ExternalIdentity` (ApplicationUserId=user01, Provider=google, Subject=google-sub-999, LinkedDate=now); audit `ExternalIdentityLinkedEvent(user_id=user01, provider=google)`; user can now login via UC-IDN-02 with Google account

#### TC-IDN-A09-02: Link external account — already linked to different user
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 8a
- **Pre:** Google sub `google-sub-999` already linked to `user02` in tenant-a
- **Data:** `GET /api/v1/external-identities/link-callback?code=<valid_for_google-sub-999>&state=<csrf>` as user01
- **Expected:** 409 Conflict; message `"This account is already linked to another user"`; no new ExternalIdentity row; audit `ExternalIdentityLinkFailed/reason=already_linked`

#### TC-IDN-A09-03: Link external account — CSRF state mismatch
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** step 6 (OIDC validation, same as UC-IDN-02 ext 1a)
- **Pre:** N/A
- **Data:** `GET /api/v1/external-identities/link-callback?code=<valid>&state=tampered`
- **Expected:** 400 Bad Request; message `"Invalid state parameter"`; no ExternalIdentity row; audit `ExternalIdentityLinkFailed/reason=csrf_mismatch`

#### TC-IDN-A09-04: Link external account — cross-tenant attempt blocked
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** step 8 (tenant scoping; memory anchor 260525-E)
- **Pre:** User authenticated in tenant-a; IdP token's email belongs to a user domain mapped to tenant-b
- **Data:** Callback with id_token for email `@tenant-b-domain.vn`
- **Expected:** 403 or 400; link not created; audit cross-tenant link attempt; no ExternalIdentity row in tenant-a

#### TC-IDN-A09-05: List linked accounts — unauthenticated blocked
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz precondition
- **Pre:** No Authorization header
- **Data:** `GET /api/v1/external-identities/link-authorize?provider=google`
- **Expected:** 401 Unauthorized; OIDC flow not initiated

---

## UC-IDN-A10: Cấu hình JIT Provisioning

Source: `uc-identity-advanced.md#uc-idn-a10` — Endpoint `POST /api/v1/admin/jit-providers`.

#### TC-IDN-A10-01: Create JIT config — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-11
- **Pre:** Admin `admin01` has `IDN_JIT_CONFIG`; Azure AD OIDC provider configured; roles `BTC_STAFF`+`TENANT_USER` exist
- **Data:** `POST /api/v1/admin/jit-providers { "providerId":"azure-ad", "emailDomainAllowlist":["@btc.gov.vn"], "autoActivate":true, "defaultRoleIds":["BTC_STAFF","TENANT_USER"], "attributeMapping":{"givenName":"first_name","surname":"last_name","email":"email"} }`
- **Expected:** 201 Created; DB row `JitProviderConfig` (ProviderId=azure-ad, EmailDomainAllowlist=["@btc.gov.vn"], AutoActivate=true, DefaultRoleIds, AttributeMapping JSON); audit `JitProviderConfiguredEvent(config_id, provider=azure-ad, domain_allowlist)`

#### TC-IDN-A10-02: JIT runtime — user from allowed domain auto-created on login
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 13 (runtime, UC-IDN-02 callback)
- **Pre:** JIT config for azure-ad with domain `@btc.gov.vn`; user `newstaff@btc.gov.vn` has never logged in before
- **Data:** OIDC callback with id_token for `newstaff@btc.gov.vn` from Azure AD
- **Expected:** New `ApplicationUser` row created (IsActive=true, roles=[BTC_STAFF,TENANT_USER]); audit `UserAutoCreatedEvent(email=newstaff@btc.gov.vn, provider=azure-ad, jit_config_id)`; accessToken issued for new user; login completes

#### TC-IDN-A10-03: JIT runtime — unauthorized domain blocked
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** step 13 (domain not match branch)
- **Pre:** JIT config only allows `@btc.gov.vn`; user from `@external-vendor.vn` logs in via same Azure AD provider
- **Data:** OIDC callback with id_token for `consultant@external-vendor.vn`
- **Expected:** 403 Forbidden; message `"Domain not authorized"`; no ApplicationUser created; audit attempt

#### TC-IDN-A10-04: Create JIT config — invalid email domain format
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 8a
- **Pre:** Admin authenticated, has perm
- **Data:** `POST /api/v1/admin/jit-providers { "emailDomainAllowlist":["not-a-domain-format"], ... }`
- **Expected:** 400 Bad Request; message `"Invalid email domain format"`; no DB row

#### TC-IDN-A10-05: Create JIT config — missing perm (403)
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz precondition
- **Pre:** Caller does NOT have `IDN_JIT_CONFIG`
- **Data:** Valid JIT config body
- **Expected:** 403 Forbidden; audit `AccessDenied/perm=IDN_JIT_CONFIG`

---

## UC-IDN-A11: Chu kỳ Access Review định kỳ

Source: `uc-identity-advanced.md#uc-idn-a11` — Endpoints `GET /api/v1/access-review?cycleId=...` + `POST /api/v1/access-review/{cycleId}/approve`.

#### TC-IDN-A11-01: Access review cycle created by scheduled job
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** `AccessReviewCycleJob` configured (quarterly cron); users + departments exist; DeptDirectors assigned as reviewers
- **Data:** Trigger job manually (test harness) or await scheduled fire; verify DB
- **Expected:** DB row `AccessReviewCycle` (CycleId=GUID, StartDate=today, EndDate=+90d, Status=pending); `ReviewerAssignments` rows per department; email queued to each reviewer; audit `AccessReviewCycleCreatedEvent(cycle_id, reviewer_count)`

#### TC-IDN-A11-02: Reviewer submits decisions — REVOKE applied
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 7-16
- **Pre:** Cycle in Status=pending; reviewer `director01` assigned; user `staff02` has excessive perm `IDN_USER_CREATE`
- **Data:** (1) `GET /api/v1/access-review?cycleId={cycleId}` as director01 (2) `POST /api/v1/access-review/{cycleId}/approve { decisions:[{ userId:"staff02", decision:"REVOKE", permissionsToRevoke:["IDN_USER_CREATE"] }] }`
- **Expected:** Step 1: 200 + list of assigned users with current roles/permissions; Step 2: 200; `AccessReviewDecision` row persisted; `RolePermissions`/`AspNetUserRoles` row for `IDN_USER_CREATE` removed from staff02; cycle Status remains pending until all reviewers done; audit `AccessReviewApprovedEvent(cycle_id, reviewer_id=director01, decisions_count=1)`

#### TC-IDN-A11-03: Non-reviewer cannot submit decisions (403)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** NFR security (assigned reviewer only)
- **Pre:** Cycle exists; caller `other_director` is NOT assigned reviewer for this cycle
- **Data:** `POST /api/v1/access-review/{cycleId}/approve { decisions:[...] }` as other_director
- **Expected:** 403 Forbidden; message `"Not authorized for this review cycle"`; no decisions persisted

#### TC-IDN-A11-04: Reviewer reassigns review to another director
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** ext 11a
- **Pre:** Cycle pending; `director01` assigned; `director02` exists with same dept scope
- **Data:** `POST /api/v1/access-review/{cycleId}/reassign { "newReviewerId":"director02-id" }` (or equivalent reassign endpoint) as director01
- **Expected:** 200; `ReviewerAssignment` updated (old=director01, new=director02); email notification to director02; audit reassignment event

#### TC-IDN-A11-05: Load review page within <1s (pagination)
- **Pri:** P2 | **Type:** Performance | **Layer:** Integration | **Trace:** NFR performance (<1s load)
- **Pre:** Cycle has 150 users assigned to reviewer; admin authenticated
- **Data:** `GET /api/v1/access-review?cycleId={cycleId}&page=1&pageSize=20`; measure response time
- **Expected:** Response <1s; body paginated (20 items, totalCount=150); no full-scan timeout

---

## Test Data Requirements

Để chạy TC suite trên cần seed (bổ sung vào `E2ETestDataSeeder.cs`):

| Entity | Records | Notes |
|--------|---------|-------|
| Tenants | tenant-a, tenant-b | Cross-tenant isolation tests |
| Users | admin01, mgr01, staff01, staff02, certifier01, submitter01, approver01, director01, director02 | Various roles + ABAC contexts |
| Roles | BTC_DEPT_DIRECTOR, BTC_AUDITOR, APPROVER_L1, EMPLOYEE, BTC_STAFF, TENANT_USER | SoD rules + delegation + JIT |
| Permissions | IDN_ABAC_RULES_MANAGE, IDN_SOD_RULES_MANAGE, IDN_ROLE_UPDATE, IDN_SESSION_TERMINATE, IDN_SIGNATURE_CONFIG, IDN_JIT_CONFIG, IDN_TASK_APPROVE, FORM_DATA_APPROVE, WF_TASK_APPROVE, WF_TASK_RECALL, IDN_USER_CREATE | Exact canonical uppercase codes |
| ABAC Rules | "ApproveHighValue" with Finance dept + amount ≤ 10000 condition | A02 evaluation tests |
| SoD Rules | BTC_DEPT_DIRECTOR ⊥ BTC_AUDITOR | A03/A04 enforcement |
| Groups | FINANCE_REVIEWERS with certifier01 as member | A02 group phase |
| Delegations | mgr01→staff01 active, mgr01→staff02 expired | A05/A06 |
| SessionTokens | submitter01 active sessions (≥2) | A07 revoke tests |
| SignatureProviders | "smartcard-01" configured | A08 signing tests |
| JitProviderConfigs | azure-ad with domain @btc.gov.vn | A10 runtime tests |
| AccessReviewCycles | 1 pending cycle with director01 assigned | A11 review tests |

Tham chiếu `tests/AqtCoreFW.Tests.Integration/Infrastructure/Seeders/E2ETestDataSeeder.cs` để mở rộng seed.

---

## Execution Notes

- **Manual run:** Postman collection import từ Swagger `/swagger/v1/swagger.json`; chạy theo thứ tự UC-IDN-A01 → A11; dependency: A04 requires A03 seeded, A02 requires A01 seeded
- **Automation roadmap:** TC này là input cho dev viết xUnit integration tests; mỗi TC-ID maps 1-1 sang `[Fact]` hoặc `[Theory]` method name; A02 internal-service TCs map to unit tests in `EffectivePermissionService`
- **Cleanup:** Mỗi TC tự cleanup DB qua `IClassFixture<DatabaseFixture>` reset hoặc rollback transaction; ABAC/SoD/JIT config TCs cần reset tables post-run
- **Scheduling:** A11-01 (scheduled job) requires test harness job trigger; serialize with `[Collection]` to avoid concurrent cycle creation
- **Stub caveat:** A08 TCs depend on `ISignatureProvider` stub; production smartcard/HSM integration requires manual E2E test with real device

## Unresolved Questions

1. **UC-IDN-A02 ABAC indeterminate semantics:** UC ext 5a says "skip rule (fail-safe)" but does not specify if skip = deny-grant vs no-op. If two rules both require `document.amount` and one is indeterminate, does the other's grant still apply? Need `AbacPolicyEvaluator.cs` code review to confirm fail-safe semantics are per-rule, not per-evaluation-run.
2. **UC-IDN-A05 delegation + SoD interaction:** Can a delegator grant a perm to a delegatee that would put the delegatee in a SoD conflict (delegatee already holds a conflicting role)? UC-IDN-A05 and UC-IDN-A04 have no stated interaction; `CreateDelegationCommandHandler` may or may not call `ISodValidationService`. Need product + code confirm.
3. **UC-IDN-A07 Redis blocklist TTL:** UC says "<30 min for JWT grace period" but exact TTL value is not specified. TC-A07-02 depends on knowing this value for timing assertions. Confirm exact TTL in `RevokeSessionCommandHandler` + Redis config.
4. **UC-IDN-A11 partial-decision failure:** UC postcondition says "no rollback if some users fail". If `SubmitAccessReviewCommandHandler` applies decisions transactionally per-user, what status does the reviewer see for partially-applied cycles? TC-A11-02 assumes full success; boundary case for partial failure unspecified.
5. **UC-IDN-A08 stub vs real PKI:** UC notes "Full PKI deferred Phase 2+". Does the stub `ISignatureProvider` validate signature bytes against document hash, or does it accept any bytes? TC-A08-02 Expected assumes `SignatureBytes non-empty` — does test infra have a real provider mock, or only a pass-through stub?
