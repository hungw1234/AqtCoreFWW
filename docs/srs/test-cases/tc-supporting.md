---
title: Test Cases - Supporting Modules (Files, Notifications, Audit, Integration)
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-26
ucs_covered: 15
tcs_total: 60
trace_source: docs/srs/use-cases/uc-supporting.md
---

# Test Cases — Supporting Modules (Files, Notifications, Audit, Integration)

Đặc tả test case (IEEE 829-style compact) cho 15 use case của 4 supporting modules. Mỗi TC trace ngược về UC source + step number, dùng cho QA execution thủ công và làm input cho dev viết test code automation.

## Conventions

- **TC-ID format:** `TC-{MOD}-{UC#}-{TC#}` — MOD=FILE/NTF/AUD/INT, UC 01-04, TC seq 01-04
- **Priority:** P0=Critical | P1=High | P2=Medium | P3=Low
- **Type:** Positive (happy path) | Negative (error path) | Boundary (limit values) | Security (auth/authz/HMAC)
- **Layer:** Unit | Integration (DB+HTTP) | E2E (FE+BE+infra) | Manual (UX/visual)
- **Trace:** UC-{MOD}-NN step Y-Z links back to source UC scenario step
- **Cross-tenant:** Every module includes ≥1 cross-tenant boundary TC per memory anchor 260525-E

## Coverage Summary

| UC | Title | TCs | P0 | P1 | P2 |
|----|-------|-----|----|----|----|
| UC-FIL-01 | Upload File (Virus Scan) | 4 | 2 | 1 | 1 |
| UC-FIL-02 | Download File (Signed URL) | 4 | 1 | 2 | 1 |
| UC-FIL-03 | Sign File (Stub) | 4 | 1 | 2 | 1 |
| UC-FIL-04 | Delete File (Soft + Retention) | 4 | 1 | 2 | 1 |
| UC-NTF-01 | Send Notification (Multi-Channel) | 4 | 2 | 1 | 1 |
| UC-NTF-02 | Subscribe In-App (SignalR) | 4 | 1 | 2 | 1 |
| UC-NTF-03 | Manage Template (Scriban) | 4 | 1 | 2 | 1 |
| UC-AUD-01 | Record Audit Log (Interceptor) | 4 | 2 | 1 | 1 |
| UC-AUD-02 | Query Audit Log (HMAC Chain) | 4 | 1 | 2 | 1 |
| UC-AUD-03 | Export Audit Evidence (CSV/JSON) | 4 | 1 | 2 | 1 |
| UC-AUD-04 | Retention Policy (Archive+Purge) | 4 | 1 | 2 | 1 |
| UC-INT-01 | Configure Outbound Webhook | 4 | 1 | 2 | 1 |
| UC-INT-02 | Receive Inbound Webhook (HMAC) | 4 | 2 | 1 | 1 |
| UC-INT-03 | Dispatch Outbox Event (Retry+DLQ) | 4 | 2 | 1 | 1 |
| UC-INT-04 | View Webhook Logs (Admin Debug) | 4 | 1 | 2 | 1 |
| **Total** | | **60** | **20** | **25** | **15** |

---

## Files Module

### UC-FIL-01: Upload File (Virus Scan Boundary)

Source: `uc-supporting.md#uc-fil-01` — Endpoint `POST /api/v1/files`.

#### TC-FILE-01-01: Upload clean file — happy path (async scan + move)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User authenticated với `Files.Upload` perm; file size 2MB PNG; ClamAV daemon up; tenant quota not exceeded
- **Data:** `POST /api/v1/files` multipart: `file=test.png`, `documentId={validGuid}`, tenant=`tenant-a`
- **Expected:** 202 Accepted + `{ scanJobId }` returned immediately; staging bucket `tenant-a/{docId}/{guid}.tmp` created; after scan completes, file moved to primary bucket `tenant-a/docs/{guid}.dat`; `FileUploadedEvent` emitted; `DocumentFile.Status=Clean`; audit row created; download URL now available

#### TC-FILE-01-02: Upload — ClamAV detects virus (quarantine)
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** ClamAV returns FOUND for EICAR test string; staging bucket writable
- **Data:** `POST /api/v1/files` multipart with EICAR test file
- **Expected:** Staging file deleted; `DocumentFile.Status=Quarantined`; `FileQuarantinedEvent` emitted; notification dispatched to uploader (UC-NTF-01 triggered); download URL returns 404; audit row `FileQuarantined/reason=virus_detected`

#### TC-FILE-01-03: Upload — file size > 100MB boundary
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** precondition
- **Pre:** User authenticated; tenant quota OK
- **Data:** `POST /api/v1/files` with 101MB file
- **Expected:** 400 Bad Request; message `"File size exceeds 100MB limit"`; no staging write; ClamAV not invoked; audit `UploadRejected/reason=size_exceeded`

#### TC-FILE-01-04: Upload — cross-tenant documentId rejected
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (260525-E)
- **Pre:** User in `tenant-a`; `documentId` belongs to entity in `tenant-b`
- **Data:** `POST /api/v1/files` with valid file + cross-tenant `documentId`
- **Expected:** 404 (not 403 — avoid cross-tenant info leak); no file persisted; audit `AccessDenied/cross_tenant/operation=file_upload`

---

### UC-FIL-02: Download File (Signed URL + Permission Check)

Source: `uc-supporting.md#uc-fil-02` — Endpoint `GET /api/v1/files/{fileId}/download-url`.

#### TC-FILE-02-01: Download — happy path, signed URL returned
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** File `fileId` Status=Clean, tenant-a; user has `Files.Download` perm on parent document
- **Data:** `GET /api/v1/files/{fileId}/download-url`
- **Expected:** 200 OK; `{ url: "https://minio.../...", expiresAt: "+15min" }`; URL is MinIO presigned, valid for 15min; following presigned URL → MinIO returns file with `Content-Disposition: attachment`; audit row `FileDownloaded/userId/fileId`

#### TC-FILE-02-02: Download — file still scanning (202)
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** File Status=Scanning
- **Data:** `GET /api/v1/files/{fileId}/download-url`
- **Expected:** 202 Accepted; body `{ retryAfterSeconds: 30 }`; no presigned URL issued; no download audit row (scan incomplete)

#### TC-FILE-02-03: Download — signed URL expired after 15min
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** special req (expiry)
- **Pre:** Valid presigned URL obtained from TC-FILE-02-01; wait 16min (or manipulate expiry in test)
- **Data:** GET presigned URL after expiry
- **Expected:** MinIO returns 403 Forbidden (expired signature); user must call API again for fresh URL; no data leak

#### TC-FILE-02-04: Download — no permission on parent document
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** User lacks `Files.Download` perm on parent document; file Status=Clean
- **Data:** `GET /api/v1/files/{fileId}/download-url` from unauthorized user
- **Expected:** 403 Forbidden; no presigned URL issued; audit `AccessDenied/perm=Files.Download`

---

### UC-FIL-03: Sign File (Digital Signature Stub)

Source: `uc-supporting.md#uc-fil-03` — Endpoint `POST /api/v1/files/{fileId}/sign-request`.

#### TC-FILE-03-01: Sign file — happy path (stub response)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** File Status=Clean; user authenticated as document owner
- **Data:** `POST /api/v1/files/{fileId}/sign-request`
- **Expected:** 201 Created; `{ signatureJobId, signatureUrl: "https://ca-stubs.local/sign/{jobId}" }`; SHA256 hash computed of file content; `File.SignatureMetadata` JSONB updated with `{ algorithm:"SHA256", timestamp, certificateChain:null (stub) }`; audit row `FileSignRequested`

#### TC-FILE-03-02: Sign — file not in Clean status
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** precondition
- **Pre:** File Status=Quarantined
- **Data:** `POST /api/v1/files/{fileId}/sign-request`
- **Expected:** 409 Conflict; message `"Cannot sign file in Quarantined status"`; no signature job created; no DB change

#### TC-FILE-03-03: Sign — file already signed (idempotency)
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** idempotency
- **Pre:** File has existing `SignatureMetadata` with completed signature
- **Data:** `POST /api/v1/files/{fileId}/sign-request` again
- **Expected:** 200 OK (or 409 per design); existing signature NOT overwritten; response returns existing `signatureJobId`; audit `SignRequestDuplicate/jobId`

#### TC-FILE-03-04: Sign — cross-tenant file access blocked
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (260525-E)
- **Pre:** User in `tenant-a`; file belongs to `tenant-b`
- **Data:** `POST /api/v1/files/{tenantB-fileId}/sign-request` from tenant-a user
- **Expected:** 404 (not 403); no hash computed; no signature metadata written; audit `AccessDenied/cross_tenant/operation=file_sign`

---

### UC-FIL-04: Delete File (Soft Delete + Retention)

Source: `uc-supporting.md#uc-fil-04` — Endpoint `DELETE /api/v1/files/{fileId}`.

#### TC-FILE-04-01: Delete file — happy path (soft delete + schedule hard-delete)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** User has `Files.Delete` perm; file Status=Clean; retention_days=90; no legal hold active
- **Data:** `DELETE /api/v1/files/{fileId}`
- **Expected:** 204 No Content; DB `IsDeleted=true`, `DeletedAt=UtcNow`, `DeletedBy=userId`; hard-delete scheduled after 90 days; `FileDeletedEvent` emitted; audit row `FileDeleted/soft`; file no longer accessible via download API (404)

#### TC-FILE-04-02: Delete file — legal hold active → archive to cold storage
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 3 (legal hold branch)
- **Pre:** File has active legal hold flag; retention_days configured; user has `Files.Delete`
- **Data:** `DELETE /api/v1/files/{fileId}`
- **Expected:** 204; file soft-deleted; content archived to cold storage bucket (not immediate hard-delete); `FileArchivedEvent` emitted; hard-delete blocked while legal hold active; audit `FileArchived/reason=legal_hold`

#### TC-FILE-04-03: Delete — retention below 90-day compliance floor rejected
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** special req NĐ91
- **Pre:** Admin attempts to set `retention_days=30` (below compliance floor)
- **Data:** `PUT /api/v1/files/retention-policies { retentionDays: 30 }`
- **Expected:** 400; message `"Retention period must be at least 90 days (NĐ91 compliance)"`; policy not persisted

#### TC-FILE-04-04: Delete — no permission (Files.Delete missing)
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** User lacks `Files.Delete` perm
- **Data:** `DELETE /api/v1/files/{fileId}`
- **Expected:** 403 Forbidden; file unchanged; audit `AccessDenied/perm=Files.Delete`

---

## Notifications Module

### UC-NTF-01: Send Notification (Template + Multi-Channel)

Source: `uc-supporting.md#uc-ntf-01` — Endpoint `POST /api/v1/notifications/send` (internal).

#### TC-NTF-01-01: Send notification — happy path multi-channel (email + in-app)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-8
- **Pre:** Template `TASK_ASSIGNED` exists (vi + en); user prefs: email+in-app; SMTP available; Scriban 7.2.0
- **Data:** Domain event `WorkflowTaskAssignedEvent` fired with `{ task.Title:"Duyệt hợp đồng", assignedBy.FullName:"Nguyễn A" }`
- **Expected:** 202 Accepted; email queued to SMTP outbox with rendered subject + body (Scriban merged); notification row inserted in DB (`IsRead=false`); SignalR group `user-{userId}` receives `ReceiveNotification` push; `NotificationSentEvent` audit row created; Scriban renders `vi` variant when `user.PreferredLanguage=vi`

#### TC-NTF-01-02: Send notification — SMTP timeout → exponential retry
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 5a
- **Pre:** SMTP server simulated timeout; Scriban render succeeds
- **Data:** Trigger notification dispatch; SMTP returns timeout on 3 consecutive attempts
- **Expected:** First attempt fails after 5s; retry 1 at +2s, retry 2 at +4s, retry 3 at +8s; after 3rd failure, message moved to DLQ; in-app channel still delivered; audit `NotificationFailed/channel=email/reason=smtp_timeout`

#### TC-NTF-01-03: Send — template not found for event code
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** No template registered for event code `UNKNOWN_EVENT`
- **Data:** Domain event `UNKNOWN_EVENT` fires
- **Expected:** Channel skipped gracefully (no exception thrown); warning logged; in-app channel NOT attempted; audit `NotificationSkipped/reason=template_not_found/event=UNKNOWN_EVENT`

#### TC-NTF-01-04: Send — rate-limit 6th notification within 1min
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** special req rate-limit
- **Pre:** User received 5 notifications within 1min
- **Data:** 6th notification event for same user
- **Expected:** 429 Too Many Requests (or silent drop per design); `Retry-After` set; audit `NotificationRateLimited/userId`; no SMTP/SMS sent

---

### UC-NTF-02: Subscribe In-App Notification (SignalR Push)

Source: `uc-supporting.md#uc-ntf-02` — Hub `/hubs/notifications`.

#### TC-NTF-02-01: SignalR connect + receive push — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User `submitter01` authenticated, JWT valid; SignalR hub running
- **Data:** WebSocket connect `wss://.../hubs/notifications?access_token={validJwt}`
- **Expected:** Connection established; server joins client to groups `tenant-{tenantId}` + `user-{userId}`; upon `NotificationSentEvent` fire, client receives `ReceiveNotification` message with payload; `PUT /notifications/{id}/read` → 200, DB `IsRead=true`; idempotent re-read → 204

#### TC-NTF-02-02: SignalR connect — expired JWT rejected
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 1a
- **Pre:** JWT expired
- **Data:** WebSocket connect with expired token
- **Expected:** 401 response; connection closed immediately; no group joined; no messages delivered; audit `SignalRConnectionRejected/reason=jwt_expired`

#### TC-NTF-02-03: SignalR — cross-tenant message isolation
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** special req (tenant isolation, 260525-E)
- **Pre:** Two users: `user-A` in `tenant-a`, `user-B` in `tenant-b`; both connected
- **Data:** Notification event for `tenant-a` user fires
- **Expected:** `user-A` receives message; `user-B` receives NO message; SignalR groups properly scoped per tenantId; verified via mock hub group membership check

#### TC-NTF-02-04: SignalR — graceful reconnect within 30s
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** special req reconnect
- **Pre:** User connected; network interrupted 15s
- **Data:** Connection drops; client reconnects within 30s grace window
- **Expected:** Reconnect succeeds without re-auth; group rejoined; missed notifications delivered on reconnect (or fetched via GET unread endpoint); no duplicate notifications

---

### UC-NTF-03: Manage Notification Template (Scriban + Sanitize)

Source: `uc-supporting.md#uc-ntf-03` — Endpoints `POST /PUT /api/v1/notifications/templates`.

#### TC-NTF-03-01: Create template — happy path Scriban render
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** Admin has `Notifications.TemplateManage`; Scriban 7.2.0 installed
- **Data:** `POST /api/v1/notifications/templates { code:"TASK_ASSIGNED", language:"vi", subject:"CV được gán", body:"{{ task.Title }} gán bởi {{ assignedBy.FullName }}", channels:["Email","InApp"] }`
- **Expected:** 201 Created; template persisted; dry-run render with sample context succeeds; audit `TemplateCreated/code=TASK_ASSIGNED`; no `include`/`import` directives present → passes validation; subsequent dispatch renders body correctly

#### TC-NTF-03-02: Create template — Scriban syntax error
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Admin authenticated
- **Data:** `body: "{{ task.Title | invalid_func }}"` (non-whitelisted function)
- **Expected:** 400 Bad Request; body `{ "error": "Scriban syntax error: invalid_func not in whitelist" }`; template not persisted; no audit row for creation

#### TC-NTF-03-03: Create template — dangerous directive blocked
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** Admin authenticated
- **Data:** `body: "{{ include 'malicious.sbn' }}"` with `include` directive
- **Expected:** 422 Unprocessable Entity; message `"Template contains unsafe directive: include"`; template not persisted; audit `TemplateRejected/reason=unsafe_directive`

#### TC-NTF-03-04: Update template — version history preserved
- **Pri:** P2 | **Type:** Positive | **Layer:** Integration | **Trace:** special req (soft history)
- **Pre:** Template `TASK_ASSIGNED` v1 exists
- **Data:** `PUT /api/v1/notifications/templates/{id}` with updated body
- **Expected:** 200; new version persisted; previous version soft-archived (not deleted); `ModifiedBy` + `ModifiedAt` updated; audit `TemplateUpdated/code=TASK_ASSIGNED/prevVersion=1/newVersion=2`

---

## Audit Module

### UC-AUD-01: Record Audit Log (System Actor, Auto via Interceptor)

Source: `uc-supporting.md#uc-aud-01` — No endpoint (automatic interceptor).

#### TC-AUD-01-01: Audit record created on entity save — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** Audit interceptor registered; `WorkflowTask` entity modified via command handler; `AuditLogs` table exists
- **Data:** Execute `UpdateWorkflowTaskStateCommand` → handler → `SaveChanges()`
- **Expected:** Exactly 1 `AuditLog` row inserted in same transaction; `UserId=00000000-0000-0000-0000-000000000001` (system actor); `EntityName=WorkflowTask`; `Action=UPDATE`; `OldValues` + `NewValues` JSON non-null; `PreviousHash` set correctly; record immutable (no UPDATE ever on `AuditLogs`); write completes in <10ms

#### TC-AUD-01-02: Audit record — HMAC chain hash continuity
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** special req HMAC chain
- **Pre:** Minimum 3 consecutive audit records exist (R1, R2, R3)
- **Data:** Query R2.PreviousHash + recompute `SHA256(R1.content + HmacSecret)` via test util
- **Expected:** `R2.PreviousHash == computed SHA256(R1)`; `R3.PreviousHash == computed SHA256(R2)`; chain intact; any manual UPDATE to R1 breaks chain → detected in TC-AUD-02-02

#### TC-AUD-01-03: Audit record — human actor attributed (non-system action)
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 3-4
- **Pre:** Admin user `admin01` executes `CreateUserCommand`
- **Data:** Command executed with `ICurrentUserService.UserId = admin01.Id`
- **Expected:** Audit row `UserId=admin01.Id` (not system actor); `Action=INSERT`; `EntityName=ApplicationUser`; correct tenant scoping (`TenantId=admin01.TenantId`)

#### TC-AUD-01-04: Audit — cross-tenant query filter enforced
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (260525-E)
- **Pre:** Audit records for both `tenant-a` and `tenant-b`; admin in `tenant-a`
- **Data:** `GET /api/v1/audit/logs` without tenant filter override
- **Expected:** Response contains ONLY `tenant-a` records; `tenant-b` rows invisible; verified by asserting all rows `TenantId=tenant-a`

---

### UC-AUD-02: Query Audit Log (Admin Filter + HMAC Chain Verify)

Source: `uc-supporting.md#uc-aud-02` — Endpoints `GET /api/v1/audit/logs` + `POST /audit/logs/verify-chain`.

#### TC-AUD-02-01: Query audit logs — filter by user + date range
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-3
- **Pre:** Admin has `Audit.View`; 100 audit rows for `submitter01` in last 7 days
- **Data:** `GET /api/v1/audit/logs?userId={submitter01Id}&fromDate=7daysAgo&toDate=now&pageSize=50`
- **Expected:** 200; paginated result ≤50 rows; all rows `UserId=submitter01Id`; `TotalCount` header present; query completes <1s; response includes `Hash` field per row

#### TC-AUD-02-02: Verify HMAC chain — tampered record detected
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** step 4-6
- **Pre:** Audit chain exists; manually UPDATE one row's `NewValues` in DB (simulating tampering)
- **Data:** `POST /api/v1/audit/logs/verify-chain { fromDate:..., toDate:... }`
- **Expected:** Response `{ status:"BREACH", tamperDetectedAt:"{id}", message:"Chain integrity violation" }`; security incident flag emitted; audit `ChainVerificationFailed/recordId={id}`

#### TC-AUD-02-03: Query — no Audit.View perm
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** User lacks `Audit.View`
- **Data:** `GET /api/v1/audit/logs`
- **Expected:** 403 Forbidden; no log rows returned; audit `AccessDenied/perm=Audit.View`

#### TC-AUD-02-04: Verify chain — clean chain returns INTACT
- **Pri:** P2 | **Type:** Positive | **Layer:** Integration | **Trace:** step 5-6
- **Pre:** Audit records unmodified (pristine state)
- **Data:** `POST /api/v1/audit/logs/verify-chain`
- **Expected:** Response `{ status:"INTACT", recordsVerified:N }`; no incident flag; audit `ChainVerificationCompleted/result=intact`

---

### UC-AUD-03: Export Audit Evidence (CSV/JSON for Compliance)

Source: `uc-supporting.md#uc-aud-03` — Endpoint `POST /api/v1/audit/logs/export`.

#### TC-AUD-03-01: Export CSV — happy path with signature
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** Admin has `Audit.Export`; 500 audit rows matching filter
- **Data:** `POST /api/v1/audit/logs/export { format:"CSV", fromDate:..., toDate:... }`
- **Expected:** 200; `Content-Type: text/csv`; file has UTF-8 BOM (Excel compatible); `X-Export-Signature` response header with base64 PKCS#8 signature; filename includes timestamp + filter summary; SHA256 checksum computed over file bytes + embedded in header; external auditor can verify signature with public key

#### TC-AUD-03-02: Export JSON — happy path
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** Admin has `Audit.Export`
- **Data:** `POST /api/v1/audit/logs/export { format:"JSON", ... }`
- **Expected:** 200; `Content-Type: application/json`; valid JSON array; `X-Export-Signature` header present; audit `AuditExported/format=JSON/recordCount=N`

#### TC-AUD-03-03: Export — >100k records warning
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Filter matches 150k records
- **Data:** `POST /api/v1/audit/logs/export` with broad date range
- **Expected:** 422 Unprocessable Entity; message `"Export exceeds 100k record limit. Narrow filter or use chunked export."`; no file generated

#### TC-AUD-03-04: Export — no Audit.Export perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** User has `Audit.View` but NOT `Audit.Export`
- **Data:** `POST /api/v1/audit/logs/export`
- **Expected:** 403 Forbidden; no file generated; audit `AccessDenied/perm=Audit.Export`

---

### UC-AUD-04: Retention Policy (Auto-Archive + Purge)

Source: `uc-supporting.md#uc-aud-04` — Endpoints `GET/PUT /api/v1/audit/retention-policies`.

#### TC-AUD-04-01: Configure retention policy — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-2
- **Pre:** Admin has `Audit.Manage`; current policy retentionYears=7
- **Data:** `PUT /api/v1/audit/retention-policies { retentionYears:5, archiveToColdstorage:true, purgeAfterArchive:false }`
- **Expected:** 200; policy persisted; audit `RetentionPolicyUpdated/retentionYears=5`; nightly job picks up new config on next run

#### TC-AUD-04-02: Nightly job — archive old records to cold storage
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 3
- **Pre:** Policy retentionYears=3; audit records older than 3 years exist; `AuditLogs_Archive` table exists
- **Data:** Trigger background job (manual invoke or advance clock)
- **Expected:** Old records bulk-inserted to `AuditLogs_Archive`; deleted from active `AuditLogs` table; `AuditLogsArchivedEvent` emitted; HMAC chain continuity preserved in archive; audit `RetentionJobCompleted/archivedCount=N`

#### TC-AUD-04-03: Retention policy — below 3-year legal floor rejected
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** special req NĐ91
- **Pre:** Admin authenticated with `Audit.Manage`
- **Data:** `PUT /api/v1/audit/retention-policies { retentionYears:2 }`
- **Expected:** 400; message `"Retention period must be at least 3 years (NĐ91 compliance)"`; policy not persisted

#### TC-AUD-04-04: Get retention policy — no Audit.Manage perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** User lacks `Audit.Manage`
- **Data:** `GET /api/v1/audit/retention-policies`
- **Expected:** 403 Forbidden; policy details not returned; audit `AccessDenied/perm=Audit.Manage`

---

## Integration Module

### UC-INT-01: Configure Outbound Webhook (Target + Auth + Filter)

Source: `uc-supporting.md#uc-int-01` — Endpoint `POST /api/v1/integration/partners/{partnerId}/webhooks`.

#### TC-INT-01-01: Register webhook — happy path HMAC_SHA256 auth
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** Admin has `Integration.WebhookManage`; target URL `https://partner.example.com/webhook` reachable
- **Data:** `POST /integration/partners/{partnerId}/webhooks { eventFilter:"WorkflowTaskCompleted", targetUrl:"https://...", authMethod:"HMAC_SHA256", authSecret:"s3cr3t", retryPolicy:{ maxAttempts:5, initialDelayMs:1000, backoffMultiplier:2.0 }, isActive:true }`
- **Expected:** 201 Created; `{ webhookId }`; `authSecret` stored encrypted at rest (TDE); audit `WebhookConfigured/partnerId/webhookId`; event filter `WorkflowTaskCompleted` matches exactly; first dispatch test (optional ping) succeeds

#### TC-INT-01-02: Register webhook — target URL not HTTPS
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** special req HTTPS only
- **Pre:** Admin authenticated
- **Data:** `targetUrl: "http://insecure.partner.com/webhook"` (HTTP not HTTPS)
- **Expected:** 422 Unprocessable Entity; message `"Target URL must use HTTPS"`; no webhook persisted

#### TC-INT-01-03: Register webhook — target URL unreachable
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Target URL returns connection refused
- **Data:** `POST` with unreachable `targetUrl`
- **Expected:** 422; message `"Target URL validation failed: connection refused"`; no webhook persisted; audit `WebhookValidationFailed/reason=url_unreachable`

#### TC-INT-01-04: Register webhook — wildcard event filter
- **Pri:** P2 | **Type:** Positive | **Layer:** Integration | **Trace:** special req wildcard
- **Pre:** Admin authenticated; target URL valid
- **Data:** `eventFilter: "Workflow*"` (wildcard)
- **Expected:** 201; webhook matches `WorkflowTaskCompleted`, `WorkflowInstanceCreated`, etc.; verify dispatch picks up both event types in TC-INT-03-01 follow-up

---

### UC-INT-02: Receive Inbound Webhook (Signature HMAC Verify)

Source: `uc-supporting.md#uc-int-02` — Endpoint `POST /api/v1/integration/webhooks/{webhookId}/receive`.

#### TC-INT-02-01: Receive inbound — valid HMAC signature, new idempotency key
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** Webhook subscription exists; secret known; no prior record of `X-Idempotency-Key: uuid-abc`
- **Data:** `POST /integration/webhooks/{id}/receive` with body + `X-Webhook-Signature: sha256={valid_hmac}` + `X-Webhook-Timestamp: {now}` + `X-Idempotency-Key: uuid-abc`
- **Expected:** 202 Accepted; `InboundWebhookReceivedEvent` emitted; `MessageLog` row persisted; handler invoked for event type; idempotency key stored (24h TTL)

#### TC-INT-02-02: Receive inbound — invalid HMAC signature
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** Valid webhook; tampered body or wrong secret
- **Data:** `X-Webhook-Signature: sha256={wrong_hmac}`
- **Expected:** 401 Unauthorized; message `"Signature verification failed"`; event NOT processed; no `MessageLog` row; audit `InboundWebhookRejected/reason=signature_invalid`

#### TC-INT-02-03: Receive — timestamp >5min old (replay attack)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** special req timestamp validation
- **Pre:** Valid HMAC computed from stale request; `X-Webhook-Timestamp` = 6min ago
- **Data:** Replay request with old timestamp
- **Expected:** 401; message `"Request timestamp too old (replay attack prevention)"`; event not processed; audit `InboundWebhookRejected/reason=stale_timestamp`

#### TC-INT-02-04: Receive — duplicate idempotency key (within 24h)
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 4a
- **Pre:** `X-Idempotency-Key: uuid-abc` already processed within 24h (TC-INT-02-01)
- **Data:** Re-send identical request
- **Expected:** 200 (idempotent response); event NOT re-processed; no duplicate `MessageLog`; response body matches first response; audit `InboundWebhookDuplicate/key=uuid-abc`

---

### UC-INT-03: Dispatch Outbox Event (Internal Event → External, Retry+DLQ)

Source: `uc-supporting.md#uc-int-03` — Background job + `GET /api/v1/integration/outbox/dead-letter-queue`.

#### TC-INT-03-01: Outbox dispatch — happy path 2xx response
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** Outbox row exists; webhook subscription matches `WorkflowTaskCompleted`; target URL returns 200
- **Data:** Trigger outbox dispatcher job
- **Expected:** HTTP POST sent to target URL with HMAC signature header; 200 response received; `Outbox.IsProcessed=1`; `MessageLog` row created with `IsSuccess=true`; audit `OutboxDispatched/eventType=WorkflowTaskCompleted`

#### TC-INT-03-02: Outbox dispatch — 5xx failure → exponential retry → DLQ
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** step 7-8
- **Pre:** Target URL returns 500 on all attempts; `RetryCount=0` initially
- **Data:** Dispatcher job runs; target consistently fails
- **Expected:** Retry 1: +1s; Retry 2: +4s; Retry 3: +16s; Retry 4: +64s; Retry 5: +256s; after 5th failure, row moved to `DeadLetterQueue` table; admin alert emitted; `Outbox.IsProcessed` NOT set; audit `OutboxMaxRetriesExceeded/eventId`

#### TC-INT-03-03: Outbox dispatch — 4xx client error → no retry
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** step 6
- **Pre:** Target URL returns 400 (bad request — client config error)
- **Data:** Dispatcher job runs; target returns 400
- **Expected:** Error logged; NO retry attempted (4xx = client error, retry futile); `MessageLog.IsSuccess=false`; `Outbox.IsProcessed=1` (mark processed to avoid infinite retry); audit `OutboxClientError/status=400/noRetry`

#### TC-INT-03-04: Outbox insert atomicity — business write + outbox fail-together
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** special req atomicity
- **Pre:** Domain handler wraps business entity save + outbox insert in same transaction
- **Data:** Simulate DB failure mid-transaction after business entity saved but before outbox INSERT commits
- **Expected:** Full transaction rolled back; business entity NOT persisted; outbox row NOT inserted; no orphaned event; re-execute → both succeed atomically

---

### UC-INT-04: View Webhook Logs (Delivery History, Admin Debug)

Source: `uc-supporting.md#uc-int-04` — Endpoints `GET /api/v1/integration/webhooks/{id}/logs` + `POST /message-logs/{id}/retry`.

#### TC-INT-04-01: View webhook logs — paginated delivery history
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-4
- **Pre:** Admin has `Integration.Debug`; webhook has 50 log entries (mix success/failed)
- **Data:** `GET /api/v1/integration/webhooks/{webhookId}/logs?pageSize=20&status=failed`
- **Expected:** 200; paginated response ≤20 rows; all rows `IsSuccess=false`; each row includes `{ Timestamp, EventType, HttpStatus, ResponseBody, RetryCount }`; `TotalCount` header; audit `WebhookLogsQueried/webhookId`

#### TC-INT-04-02: Admin retry DLQ entry — re-queued successfully
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 5-7
- **Pre:** MessageLog entry in DLQ (`RetryCount=5`); admin triggered retry; target URL now healthy
- **Data:** `POST /api/v1/integration/message-logs/{logId}/retry`
- **Expected:** 200; record moved from DLQ back to Outbox queue (`RetryCount` reset to 0); dispatcher job picks up on next poll; on successful delivery, `IsSuccess=true`; audit `WebhookRetryInitiated/logId`

#### TC-INT-04-03: Admin retry — max retries already exhausted (reject)
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 5a
- **Pre:** Entry already retried 5× manually (admin exhausted retries beyond auto-limit)
- **Data:** `POST /api/v1/integration/message-logs/{logId}/retry` when manual limit reached
- **Expected:** 422 Unprocessable Entity; message `"Max retries exhausted. Discard or escalate."`; record not re-queued; audit `WebhookRetryRejected/reason=max_retries`

#### TC-INT-04-04: Admin discard DLQ entry — cross-tenant guard
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (260525-E)
- **Pre:** Admin in `tenant-a`; message log belongs to `tenant-b` webhook
- **Data:** `DELETE /api/v1/integration/message-logs/{tenantB-logId}`
- **Expected:** 404 (not 403 — avoid cross-tenant leak); record not deleted; audit `AccessDenied/cross_tenant/operation=message_log_discard`

---

## Test Data Requirements

| Entity | Records | Notes |
|--------|---------|-------|
| Tenants | tenant-a, tenant-b | Cross-tenant isolation (all 4 modules) |
| Users | submitter01, admin01, compliance01 | Files.Upload/Download, Audit.Export, Integration.Debug |
| Files | 1 Clean, 1 Quarantined, 1 Scanning, 1 with legal hold | All FIL-02/03/04 states covered |
| NotificationTemplates | TASK_ASSIGNED (vi + en) | UC-NTF-01, NTF-03 |
| AuditLogs | ≥10 rows (3 consecutive for HMAC chain test), 2 tenants | UC-AUD-01/02 |
| WebhookSubscriptions | 1 active HMAC_SHA256 for tenant-a; 1 tenant-b for cross-tenant test | UC-INT-01/02/04 |
| OutboxRows | 1 unprocessed WorkflowTaskCompleted event | UC-INT-03 |
| DeadLetterQueue | 1 entry with RetryCount=5 | UC-INT-04-02/03 |

Tham chiếu `tests/AqtCoreFW.Tests.Integration/Infrastructure/Seeders/E2ETestDataSeeder.cs` để mở rộng seed.

---

## Execution Notes

- **Manual run:** Postman collection import từ Swagger `/swagger/v1/swagger.json`; chạy theo thứ tự module (FIL → NTF → AUD → INT)
- **Automation roadmap:** TC này là input cho xUnit integration tests; mỗi TC-ID maps 1-1 sang `[Fact]` / `[Theory]` method name; background-job TCs (AUD-04-02, INT-03-01/02/03) cần `IHostedService` test harness hoặc manual trigger endpoint
- **Scriban version:** Verify `Scriban 7.2.0` via `dotnet list package` before TC-NTF-03 suite (CVE GHSA-24c8-4792-22hx patched per memory 260522-D)
- **HMAC test util:** Provide `HmacTestHelper` that replicates `SHA256(body + timestamp + secret)` for TC-INT-02-01 setup
- **Cleanup:** `IClassFixture<DatabaseFixture>` reset per TC; DLQ entries seeded fresh per test; HMAC chain tests use isolated tenant to avoid chain pollution

---

## Unresolved Questions

1. **TC-FILE-01-04 (cross-tenant upload):** UC does not explicitly state whether `documentId` cross-tenant check is done at controller layer or domain layer — if domain layer only, controller may return 400 (validation) not 404. Confirm boundary enforcement location.
2. **TC-NTF-01-04 (rate-limit behavior):** UC says "max 5 notifications/min per user (spam prevention)" but does not clarify if 6th is silently dropped or returns 429 to the internal caller. Needs product decision for test assertion.
3. **TC-AUD-01-02 (HMAC chain verification):** `HmacSecret` key management unclear — is it per-tenant, per-instance, or global config? Affects reproducibility of chain verification in test environments. Needs arch decision.
4. **TC-INT-04-03 (manual retry limit):** UC states "retry limit exceeded → HTTP 422" but does not define a separate manual-retry limit vs auto-retry limit (5). Is manual retry allowed unlimited times? Clarify whether `RetryCount` is reset on manual retry or appended.
5. **TC-FIL-03-03 (sign idempotency):** UC does not specify behavior when file already has completed signature — return existing job ID (200) or reject with 409. Needs product confirmation before automation.
