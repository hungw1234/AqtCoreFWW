---
title: Use Cases - Platform Modules
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-25
ucs_total: 17
---

# Use Cases — Platform Modules

7 module platform/infrastructure: MasterData, AI, Organization, Collaboration, Reporting, Rules, SystemParams (22 controllers → 17 UC). Cockburn fully-dressed TERSE (~30-40 lines/UC). Search module: pure infrastructure (no UC).

---

## MasterData Module

### UC-MD-01: Tra cứu dữ liệu tham chiếu (Lookup Reference Data)
- **Goal:** User find Province/District/Ward or CaseType from master data (lookup/dropdown)
- **Actor:** User | **Scope:** DictionaryController (read)
- **Priority:** P1 | **Trigger:** Form field select

**Preconditions:** User authenticated; master data cached; tenant-scoped

**Main Success Scenario:**
1. GET `/api/v1/masterdata/dictionaries?type=Province&search=Ha`
2. System return paginated list: `[{id, code, name, ...}]`
3. HTTP 200 + data

**Extensions:**
- 2a. Cache stale → return with `X-Cache-Age` header; user can force refresh (UC-MD-03)

**Special:** Performance <100ms (cached); data filtered by tenant

**Endpoint(s):** `GET /api/v1/masterdata/dictionaries` | Permission: `MasterData.View`

---

### UC-MD-02: Quản trị loại hồ sơ (Manage CaseType CRUD)

> **⛔ DESCOPED v1.0 (user decision 2026-07-23):** CRUD chưa từng được build (`git log -S "CreateCaseType"` = 0 add) và domain tiêu thụ — Cases module — đã bị xóa toàn phần 2026-05-01 (PR #71 `fca8a9efd`), TRƯỚC khi snapshot SRS này chốt (2026-05-25); UC mồ côi từ đó. CaseType surface hiện hữu (entity + seed + `GET /api/v1/masterdata/case-types`) giữ **dormant nguyên trạng**, không xóa. Re-spec UC này chỉ khi một domain hồ sơ mới cần danh mục — domain đó tự định nghĩa yêu cầu. Evidence: `plans/reports/audit-260723-1050-leg2-boot-clean-empirical-and-ucmd02-gap-report.md` §3.

- **Goal:** Admin create/edit/deactivate CaseType
- **Actor:** System Admin | **Scope:** MasterDataAdminController
- **Priority:** P2 | **Trigger:** Settings → Master Data → CaseType "Create"

**Preconditions:** Admin has `MasterData.Admin`; CaseType code unique per tenant

**Main Success Scenario:**
1. POST `/api/v1/masterdata/admin/case-types` → `{name, code, isActive}`
2. System validate code unique; create record
3. System log audit; invalidate cache (trigger UC-MD-03)
4. HTTP 201 + created object

**Extensions:**
- 2a. Code exists → HTTP 409 Conflict
- 4a. CaseType in-use → allow deactivate; warn in UI (soft-delete)

**Special:** Audit all CRUD; cache invalidation mandatory

**Endpoint(s):** `POST /api/v1/masterdata/admin/case-types`, `PUT /{id}`, `PATCH /{id}/deactivate` | Permission: `MasterData.Admin`

---

### UC-MD-03: Làm mới cache (Refresh Master Data Cache)
- **Goal:** Admin trigger cache refresh; system auto-refresh per TTL
- **Actor:** System Admin (manual) or System (auto)
- **Scope:** DictionaryController (cache mgmt)
- **Priority:** P3

**Preconditions:** Admin has `MasterData.Admin` (manual) or System scheduled job

**Main Success Scenario:**
1. POST `/api/v1/masterdata/cache/refresh` (or system auto on job)
2. System reload all dictionaries from DB → cache (Redis/in-memory)
3. Log refresh event (duration, count)
4. HTTP 200 + summary `{cachedItemCount, refreshedAt, nextAutoRefreshAt}`

**Extensions:**
- 2a. DB error → HTTP 503; keep previous cache; log + alert

**Special:** Auto-schedule every 24h (configurable); async if large dataset

**Endpoint(s):** `POST /api/v1/masterdata/cache/refresh` | Permission: `MasterData.Admin`

---

## AI Module

### UC-AI-01: Gửi yêu cầu chat (Send Chat Message - Non-Streaming)
- **Goal:** User send message to local AI (Ollama) → receive full response
- **Actor:** User | **Scope:** AiController (non-streaming)
- **Priority:** P1 | **Trigger:** Chat UI "Send" button

**Preconditions:** User authenticated (`AI.Chat`); AI service reachable; session exists

**Main Success Scenario:**
1. POST `/api/v1/ai/chat` → `{sessionId, message, classification}`
2. System resolve tenantId, userId from JWT
3. Call Ollama model → generate response (5-30s)
4. Store message + response in DB (audit + history)
5. HTTP 200 + `{messageId, response, tokens, model, responseTime}`

**Extensions:**
- 3a. Model timeout (>60s) → HTTP 504; user retry UC-AI-02 (streaming)
- 5a. Rate limit exceeded → HTTP 429

**Special:** Prompt injection defense (sanitize input); no cross-tenant data leak; timeout 60s; max 4096 tokens

**Endpoint(s):** `POST /api/v1/ai/chat` | Permission: `AI.Chat`

---

### UC-AI-02: Chat streaming (Server-Sent Events)
- **Goal:** User send message → receive response token-by-token (SSE)
- **Actor:** User | **Scope:** AiController (streaming)
- **Priority:** P1 | **Trigger:** Chat UI "Live stream response" option

**Preconditions:** User authenticated (`AI.Chat`); client supports SSE; session exists

**Main Success Scenario:**
1. POST `/api/v1/ai/chat/stream` → `{sessionId, message}`
2. System set response headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`
3. Stream tokens from Ollama as SSE: `data: The\ndata: case\n...\ndata: [DONE]`
4. Browser receive events real-time; JS render incrementally
5. After `[DONE]`, persist full response to DB
6. Client close connection

**Extensions:**
- Client disconnect → stream stop; partial message saved; user can retry

**Special:** First token <2s (P99); subsequent <100ms; no nginx buffering (`X-Accel-Buffering: no`)

**Endpoint(s):** `POST /api/v1/ai/chat/stream` | Permission: `AI.Chat`

---

### UC-AI-03: Trích xuất tài liệu (Extract Structured Fields from Document)
- **Goal:** User upload document image → AI vision extract structured fields (OCR)
- **Actor:** User | **Scope:** AiExtractAgentController (vision)
- **Priority:** P2 | **Trigger:** Form "Upload document for OCR"

**Preconditions:** User authenticated (`AI.Extract`); file format image/png/jpeg/webp; size ≤20 MB

**Main Success Scenario:**
1. POST `/api/v1/ai/extract` (multipart) → image file
2. System validate type + size
3. Call Ollama vision model → extract text + fields
4. HTTP 200 + `{title, fullText, fields: {invoiceNumber, issueDate, amount}, confidence}`

**Extensions:**
- 3a. Image unreadable (confidence <0.6) → warn user; allow manual correction
- 3b. Timeout (>30s) → HTTP 504

**Special:** Image NOT stored (unless user save explicitly); returned confidence score; user can override fields

**Endpoint(s):** `POST /api/v1/ai/extract` | Permission: `AI.Extract`

---

## Organization Module

### UC-ORG-01: Quản trị cây đơn vị (Manage OrgUnit Hierarchy)
- **Goal:** Admin create/edit/move/delete OrgUnit hierarchy
- **Actor:** System Admin | **Scope:** OrgUnitsController (hierarchy CRUD)
- **Priority:** P1 | **Trigger:** Settings → Organization → OrgUnits "Create/Edit/Delete"

**Preconditions:** Admin has `Organization.Manage`; parent exists (if creating child); code unique per tenant

**Main Success Scenario:**
1. POST `/api/v1/org-units` → `{parentId, code, name, description}`
2. System validate code unique; parent exists
3. Create OrgUnit; set hierarchy level based on parent depth
4. HTTP 201 + created object `{id, code, name, parentId, level}`
5. Admin can edit: PUT `/{id}` (update name, description, parentId)
6. Admin can move: PATCH `/{id}/move` (change parent)
7. Admin can delete: DELETE `/{id}` (only if no child; or warn)

**Extensions:**
- 2a. Code exists → HTTP 409
- 5a. Move creates cycle (A→B→A) → HTTP 400; prevent cycle
- 7a. Has children → HTTP 409; warn must delete children first

**Special:** Tree depth max 10; changes trigger OrgUnit-based candidate pool refresh (<5s SLA); audit all CRUD

**Endpoint(s):** `POST /api/v1/org-units`, `PUT /{id}`, `PATCH /{id}/move`, `DELETE /{id}` | Permission: `Organization.Manage`

---

### UC-ORG-02: Gán người dùng vào đơn vị (Assign User to OrgUnit M:N)
- **Goal:** HR admin assign user to OrgUnit(s) via M:N `UserOrgUnitAssignment` with temporal window
- **Actor:** HR Admin | **Scope:** StaffAssignmentController
- **Priority:** P2 | **Trigger:** Staff Management → Assign OrgUnit

**Preconditions:** Admin has `Organization.StaffAssign`; user + OrgUnit exist; no overlapping assignment

**Main Success Scenario:**
1. POST `/api/v1/staff-assignments` → `{userId, orgUnitId, startDate, endDate, isActive}`
2. System validate date range (start ≤ end); user/OrgUnit exist
3. Create UserOrgUnitAssignment record
4. HTTP 201 + created assignment `{id, userId, orgUnitId, startDate, endDate}`
5. If user has active tasks → recompute candidate pool (if OrgUnit in predicate)
6. Log audit event

**Extensions:**
- 2a. Date overlap with existing → HTTP 409; show existing dates
- 5a. Pool recompute fails → assignment created; pool refresh deferred (logged)

**Special:** User can belong to multiple OrgUnits (M:N); temporal validity (assignment valid if today ∈ [startDate, endDate]); soft-delete (expired assignments retained for audit)

**Endpoint(s):** `POST /api/v1/staff-assignments`, `PUT /{id}`, `DELETE /{id}` | Permission: `Organization.StaffAssign`

---

## Collaboration Module

### UC-COL-01: Tạo bình luận (Create Comment on Task/Instance)
- **Goal:** User post comment on workflow task/instance
- **Actor:** User | **Scope:** MessagesController (comment CRUD)
- **Priority:** P2 | **Trigger:** Task detail "Comment" button

**Preconditions:** User authenticated (`Collaboration.Comment`); task/instance exists; user has view access; comment non-empty

**Main Success Scenario:**
1. POST `/api/v1/conversations/{conversationId}/messages` → `{content, taskId}`
2. System validate task exists; user has access
3. Create message record; author=current user; timestamp=now
4. HTTP 201 + message `{id, content, author, createdAt}`
5. Emit notification event (UC-NTF-01) to task assignee + watchers
6. Update task.LastCommentedAt

**Extensions:**
- 2a. User lacks permission → HTTP 403
- 5a. Notification fails → message created; notification logged for retry

**Special:** Support @-mention (UC-COL-02); edit within 5 min (author only); soft-delete (content → "[deleted by user]"); audit all comments

**Endpoint(s):** `POST /api/v1/conversations/{conversationId}/messages`, `PUT /{messageId}`, `DELETE /{messageId}` | Permission: `Collaboration.Comment`

---

### UC-COL-02: @-Mention + Notification (Mention User)
- **Goal:** User mention @name in comment → system extract mention, create notification
- **Actor:** User | **Scope:** MessagesController + NotificationService
- **Priority:** P2 | **Trigger:** Part of UC-COL-01 (subfunction)

**Preconditions:** User typing comment with @-mention syntax (`@John`); mentioned user exists; user has `Collaboration.Mention`

**Main Success Scenario:**
1. User post comment with "@John" (UC-COL-01)
2. System parse comment for @-mention pattern (`@[A-Za-z0-9\s]+`)
3. Resolve mention to user ID (e.g., "John" → userId)
4. Extract mention metadata: `{mentionedUserId, position, context}`
5. Create CommentMention record (m:n)
6. Emit UserMentionedEvent → NotificationService
7. NotificationService create Notification + send (email/in-app/SMS per user preferences)
8. Return HTTP 201 with mentions array: `{messageId, mentions: [{userId, displayName, mentionedAt}]}`

**Extensions:**
- 3a. Mentioned user not found → ignore; post comment without mention
- 7a. Notification service fails → event queued in outbox; retry on next sync

**Special:** Deduplication (1 notification per user even if mentioned multiple times); must have `Collaboration.Mention` + mentioned user in same tenant

**Endpoint(s):** Handled via MessagesController.Create() | Permission: `Collaboration.Mention`

---

## Reporting Module

### UC-RPT-01: Chạy báo cáo (Run Predefined Report)
- **Goal:** User select report template → provide parameters → system generate + return results
- **Actor:** User | **Scope:** ReportsController (query + render)
- **Priority:** P2 | **Trigger:** Reporting → Select template → "Run now"

**Preconditions:** User authenticated (`Reporting.View`); template exists + active; user has data access

**Main Success Scenario:**
1. POST `/api/v1/reports/run` → `{templateId, parameters: {startDate, endDate, status}}`
2. System load template definition (SQL or parameterized builder)
3. Substitute parameters; apply RLS filter (tenant + assigned data)
4. Execute query; fetch results (max 10,000 rows; warn if truncated)
5. HTTP 200 + `{templateId, templateName, rows, recordCount, generatedAt, executionTime}`

**Extensions:**
- 4a. Row count >10,000 → truncate; return warning; recommend filters or export (UC-RPT-03)
- 4b. Query timeout (>30s) → HTTP 504; recommend schedule async (UC-RPT-02)

**Special:** Performance <5s typical; RLS mandatory; audit execution (who, when, template, params)

**Endpoint(s):** `POST /api/v1/reports/run`, `GET /api/v1/reports/templates` | Permission: `Reporting.View`

---

### UC-RPT-02: Lên lịch báo cáo (Schedule Recurring Report)
- **Goal:** Admin schedule report run at intervals (Hangfire) → auto-generate + deliver
- **Actor:** Admin | **Scope:** ReportsController (Hangfire scheduling)
- **Priority:** P3 | **Trigger:** Reporting → "Schedule report"

**Preconditions:** Admin has `Reporting.Schedule`; template exists; Hangfire service operational; delivery method configured

**Main Success Scenario:**
1. POST `/api/v1/reports/schedules` → `{templateId, name, cronExpression, parameters, deliveryMethod, recipients}`
2. System validate template exists; cron valid; recipients valid
3. Schedule Hangfire RecurringJob
4. HTTP 201 + `{id, nextRunAt, frequency}`
5. At scheduled time:
   - Run report (call UC-RPT-01 logic)
   - Generate file (CSV/Excel)
   - Send via delivery method (email attachment or download link)
   - Log job result

**Extensions:**
- 2a. Cron invalid → HTTP 400; show format hint
- 5a. Job fails → Hangfire auto-retry (3 times); admin notified on final failure

**Special:** Generated files retained 30d; no parallel runs (skip if previous still running); cron times in system timezone

**Endpoint(s):** `POST /api/v1/reports/schedules`, `GET /api/v1/reports/schedules`, `DELETE /api/v1/reports/schedules/{id}` | Permission: `Reporting.Schedule`

---

### UC-RPT-03: Xuất dữ liệu (Export Report Data)
- **Goal:** User export report data (UC-RPT-01 results) to CSV/Excel
- **Actor:** User | **Scope:** ReportsController (export)
- **Priority:** P2 | **Trigger:** Report results → "Export to CSV" / "Export to Excel" button

**Preconditions:** Report data loaded (UC-RPT-01); user has `Reporting.Export`; file size <100 MB

**Main Success Scenario:**
1. GET `/api/v1/reports/{reportId}/export?format=csv` (or `format=excel`)
2. System fetch cached report data (or re-run if expired)
3. Format data (CSV comma-separated or Excel XLSX)
4. HTTP 200 + file download:
   - Header: `Content-Type: text/csv` or `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
   - Header: `Content-Disposition: attachment; filename="report_YYYYMMDD.csv"`
5. Browser save to Downloads

**Extensions:**
- 3a. Data >100 MB → HTTP 413; recommend filters first

**Special:** Same RLS filter (user sees only own tenant's data); <10s typical; headers included; data types preserved

**Endpoint(s):** `GET /api/v1/reports/{reportId}/export` | Permission: `Reporting.Export`

---

## Rules Module

### UC-RUL-01: Định nghĩa business rule (Define Business Rule)
- **Goal:** Admin define business rule (RuleSet + DecisionTable)
- **Actor:** System Admin | **Scope:** RuleSetsController + DecisionTablesController
- **Priority:** P2 | **Trigger:** Settings → Rules Engine → "Create new rule"

**Preconditions:** Admin has `Rules.Manage`; rule name unique per tenant

**Main Success Scenario:**
1. POST `/api/v1/rules/rule-sets` → `{name, description, ruleType: DecisionTable, isActive: false}`
2. System create RuleSet in draft (IsActive=false)
3. Admin add DecisionTable: POST `/api/v1/rules/rule-sets/{ruleSetId}/decision-tables`:
   ```json
   {rows: [{condition: "caseValue <= 5M", outcome: "ManagerReview"}, ...]}
   ```
4. System validate conditions syntactically; outcomes map to known codes
5. Persist DecisionTable
6. Admin publish: PATCH `/api/v1/rules/rule-sets/{ruleSetId}/activate`
7. System set IsActive=true; log activation

**Extensions:**
- 4a. Condition syntax error → HTTP 400; show error + valid syntax hint
- 4b. Outcome code unknown → HTTP 400; show available codes

**Special:** Versioning (each activation = new version); audit all changes (who, when, before→new)

**Endpoint(s):** `POST /api/v1/rules/rule-sets`, `POST /api/v1/rules/rule-sets/{ruleSetId}/decision-tables`, `PATCH /api/v1/rules/rule-sets/{ruleSetId}/activate` | Permission: `Rules.Manage`

---

### UC-RUL-02: Đánh giá rule (Evaluate Rule at Runtime)
- **Goal:** During workflow (UC-WF-08), system evaluate rule against context → return outcome
- **Actor:** System (Workflow engine) | **Scope:** RuleEvaluationService (subfunction)
- **Priority:** P1

**Preconditions:** RuleSet published (IsActive=true); context variables match rule signature

**Main Success Scenario:**
1. Workflow handler call `IRuleEvaluationService.EvaluateAsync(ruleSetId, context, tenantId, ct)`
   - context: `{caseValue, caseType, ...}`
2. System load active RuleSet + DecisionTable version
3. Iterate DecisionTable rows:
   - Evaluate condition against context
   - If match → return outcome; exit
4. Return `{ruleSetId, outcome, matchedRow, evaluationTime}`
5. Caller (workflow handler) use outcome for next action

**Extensions:**
- 3a. No condition matches → HTTP 200 + null outcome (or default if configured)
- 3b. Evaluation exception (undefined variable, type mismatch) → HTTP 400 + detail; log error

**Special:** Performance <100ms (cached definition); deterministic (same context → same outcome); audit evaluations (if enabled)

**Endpoint(s):** `POST /api/v1/rules/evaluate` (if exposed) or internal-only `IRuleEvaluationService.EvaluateAsync()` | Permission: `Rules.Evaluate`

---

## SystemParams Module

### UC-SYS-01: Xem tham số (View System Parameter)
- **Goal:** Any user read system/tenant-specific parameters (e.g., MaxCaseSize, DefaultLanguage)
- **Actor:** Any authenticated user | **Scope:** SystemParamsController (read)
- **Priority:** P1

**Preconditions:** User authenticated; parameter visible to tenant

**Main Success Scenario:**
1. GET `/api/v1/system-params?key=MaxCaseSize` or `?category=Workflow`
2. System load parameter(s) from cache/DB
3. HTTP 200 + `{parameters: [{key, value, category, dataType}]}`

**Special:** Cached in-memory/Redis (TTL 1h); parameters global or tenant-specific; sensitive values never returned

**Endpoint(s):** `GET /api/v1/system-params` | Permission: Implicit (all users read non-sensitive)

---

### UC-SYS-02: Cập nhật tham số (Update System Parameter)
- **Goal:** System Admin update system parameter (adjust limits, toggle feature flag)
- **Actor:** System Admin | **Scope:** SystemParamsController (write)
- **Priority:** P2 | **Trigger:** Settings → System Parameters → Edit value

**Preconditions:** Admin has `SystemParams.Manage`; parameter exists; new value valid per data type + constraints

**Main Success Scenario:**
1. PUT `/api/v1/system-params/{key}` → `{value: 2000000}`
2. System validate value (integer, range [0, 10M])
3. Update parameter; invalidate cache
4. Log change (old→new, who, when)
5. HTTP 200 + updated parameter
6. If feature flag → may trigger config reload or notify services

**Extensions:**
- 2a. Value invalid → HTTP 400 + validation error
- 6a. Config reload fails → update persisted; cache invalidated; services use new value on next read

**Special:** Audit mandatory; soft validation override (admin can override with reason); feature flags affect availability immediately

**Endpoint(s):** `PUT /api/v1/system-params/{key}`, `GET /api/v1/system-params` | Permission: `SystemParams.Manage`

---

## Cross-Module Notes

**Organization vs Identity Groups:**
- **OrgUnit:** Hierarchical org chart (Department, Team). Used for candidate resolution (UC-WF-05).
- **Group:** Logical user grouping for permission assignment. Used for ABAC (UC-IDN-A01).

**Rules vs PolicyRules:**
- **Rules module:** General-purpose business rules engine (decision tables, condition evaluation).
- **PolicyRules (ABAC):** Authorization-specific rule evaluation. Both use decision table notation; Rules is business-logic-general.

**Search Module:** Pure infrastructure (full-text indexing + `ISearchService`). No controller → no SRS UC. Reference `docs/codebase-summary.md`.

---

## Summary

- **Total UCs:** 17 | **Controllers:** 22 | **Modules:** 7
- **Line count:** ~750 (terse Cockburn format)
- **Cross-links:** UC-COL-02→UC-NTF-01; UC-ORG-02→UC-WF-05; UC-RUL-02↔UC-IDN-A01
- **Key security notes:** Prompt injection (UC-AI-01), RLS enforcement (UC-RPT-01), mention filtering (UC-COL-02)
- **Performance targets:** <100ms lookup (UC-MD-01), <5s report (UC-RPT-01), <30s vision (UC-AI-03)
