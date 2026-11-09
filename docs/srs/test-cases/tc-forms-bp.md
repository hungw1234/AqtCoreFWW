---
title: Test Cases - Forms & Business Process Modules
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-26
ucs_covered: 10
tcs_total: 48
trace_source: docs/srs/use-cases/uc-forms-bp.md
---

# Test Cases — Forms & Business Process Modules

Đặc tả test case (IEEE 829-style compact) cho 10 use case của Forms Module và BusinessProcess Module. Mỗi TC trace ngược về UC source + step number, dùng cho QA execution thủ công và làm input cho dev viết test code automation sau.

## Conventions

- **TC-ID format:** `TC-FORM-{UC#}-{TC#}` (Form UCs) và `TC-BP-{UC#}-{TC#}` (BP UCs) — UC# mirrors UC-FRM-XX / UC-BP-XX suffix; TC# seq 01-NN
- **Priority:** P0=Critical | P1=High | P2=Medium | P3=Low (edge/cosmetic)
- **Type:** Positive | Negative | Boundary | Security | Performance
- **Layer:** Unit | Integration (DB+HTTP) | E2E (FE+BE+infra) | Manual (UX/visual)
- **Trace:** UC-FRM-XX / UC-BP-XX step Y links back to source UC scenario step or extension

## Coverage Summary

| UC | Title | TCs | P0 | P1 | P2 |
|----|-------|-----|----|----|----|
| UC-FRM-01 | Design Form Template | 4 | 1 | 2 | 1 |
| UC-FRM-02 | Save Form Graph | 6 | 2 | 3 | 1 |
| UC-FRM-03 | Publish Form Template | 4 | 1 | 2 | 1 |
| UC-FRM-04 | Render Runtime Form | 4 | 1 | 2 | 1 |
| UC-FRM-05 | Submit Form Data | 6 | 2 | 3 | 1 |
| UC-FRM-06 | Edit Form Data In-Flight | 5 | 1 | 3 | 1 |
| UC-BP-01 | Create BP Definition | 4 | 1 | 2 | 1 |
| UC-BP-02 | Configure Form-Map Binding | 6 | 2 | 3 | 1 |
| UC-BP-03 | Trigger BP Instance | 5 | 2 | 2 | 1 |
| UC-BP-04 | View BP Status | 4 | 1 | 2 | 1 |
| **Total** | | **48** | **14** | **24** | **9** |

---

## UC-FRM-01: Thiết kế Form Template (Design Schema Draft)

Source: `uc-forms-bp.md#uc-frm-01` — Endpoints `POST /api/v1/forms/templates` + `POST /api/v1/forms/templates/{id}/sections`.

#### TC-FORM-01-01: Create form template — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-8
- **Pre:** Admin authenticated với perm `Form.Template.Manage`; tenant active; name "Approval Request Form" not used
- **Data:** `POST /api/v1/forms/templates { "name": "Approval Request Form", "description": "Employee request form", "status": "Draft", "isPublished": false }`
- **Expected:** 201 Created; DB row `FormTemplate` với `Status=Draft, Version=1, IsPublished=false`; response includes `id` (Guid) + empty sections list; audit row `FormTemplateCreatedEvent` logged với `field_count=0`

#### TC-FORM-01-02: Create form template — name duplicate trong tenant
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 1a
- **Pre:** Template "Approval Request Form" already exists in tenant-a
- **Data:** `POST /api/v1/forms/templates { "name": "Approval Request Form" }`
- **Expected:** 409 Conflict; message `"Form name already exists in this tenant"`; no DB row created

#### TC-FORM-01-03: Add section to template — happy path
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 4
- **Pre:** Draft template `form-id-01` exists; admin authenticated
- **Data:** `POST /api/v1/forms/templates/form-id-01/sections { "name": "Personal Info", "displayOrder": 1 }`
- **Expected:** 200 OK; section row created; `FormTemplate.ModifiedAt` updated; response <50ms

#### TC-FORM-01-04: Create template — missing perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** precondition
- **Pre:** Caller authenticated without `Form.Template.Manage`
- **Data:** Same body as TC-FORM-01-01
- **Expected:** 403 Forbidden; message `"Insufficient permissions"`; no DB write; audit `AccessDenied`

---

## UC-FRM-02: Lưu Form Graph (Save Fields + Sections + Validations)

Source: `uc-forms-bp.md#uc-frm-02` — Endpoint `POST /api/v1/forms/templates/{id}/save-graph`.

#### TC-FORM-02-01: Save graph — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** Draft template `form-id-01` exists; admin authenticated; graph has 2 sections + 2 fields + 2 validations
- **Data:** `POST /api/v1/forms/templates/form-id-01/save-graph` with full sections/fields/validations payload (per UC payload spec)
- **Expected:** 200 OK; DB contains exactly 2 sections + 2 fields + 2 validations; `FormTemplate.ModifiedAt` updated; audit `FormGraphSavedEvent` với `section_count=2, field_count=2`; single SaveChanges() (verify via DB query count instrumentation)

#### TC-FORM-02-02: Save graph — orphan-delete (EF Core 10 raw-SQL contract)
- **Pri:** P0 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 3 (memory anchor 260524-A/B/C)
- **Pre:** Template `form-id-01` has 3 fields (fld-1, fld-2, fld-3) + 2 sections in DB; caller submits new graph with only fld-1 + fld-2 (fld-3 removed); `Id > 0` rows exist (persisted, not transient)
- **Data:** `POST /save-graph` payload omitting fld-3 and any orphaned section; `ifMatch` current ETag
- **Expected:** After SaveChanges(): DB contains 0 rows for fld-3; `ExecuteDeleteAsync` with `Unchanged∪Deleted` filter applied (raw-SQL path bypasses EF Core 10 batch sorter); no FK violation; single atomic commit; `FormTemplate.ModifiedAt` bumped

#### TC-FORM-02-03: Save graph — validation V1 fails (dangling section ref)
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Draft template exists; field payload references `sectionId: "sec-99"` which is NOT in sections array
- **Data:** POST save-graph with `fields[0].sectionId = "sec-99"` missing from sections list
- **Expected:** 422 Unprocessable Entity; message includes `"Section 'sec-99' referenced by field not found"`; no DB writes

#### TC-FORM-02-04: Save graph — validation V4 fails (duplicate field names in section)
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2b
- **Pre:** Draft template exists
- **Data:** POST save-graph with two fields in same section both named `"FullName"`
- **Expected:** 422; message `"Field names must be unique within section"`; no DB write

#### TC-FORM-02-05: Save graph — concurrent edit race (idempotent orphan-delete)
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** Two admin sessions both fetched template at same ETag; Admin-A removed fld-3; Admin-B submits without fld-3 concurrently
- **Data:** Admin-B's `POST /save-graph` arrives after Admin-A's atomic delete
- **Expected:** Admin-B receives 200 OK or 412 (ETag mismatch); orphan fld-3 NOT double-deleted error (ExecuteDeleteAsync idempotent); no FK violation; DB consistent

#### TC-FORM-02-06: Save graph — on already-published template
- **Pri:** P2 | **Type:** Negative | **Layer:** Integration | **Trace:** precondition (IsPublished=true)
- **Pre:** Template has `IsPublished=true` (already published)
- **Data:** POST save-graph to published template
- **Expected:** 409 or 422; message `"Cannot modify published template"`; no DB mutation; forces version increment flow

---

## UC-FRM-03: Publish Form Template (Validate + Activate)

Source: `uc-forms-bp.md#uc-frm-03` — Endpoint `POST /api/v1/forms/templates/{id}/publish`.

#### TC-FORM-03-01: Publish template — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-8
- **Pre:** Draft template with saved graph (V1-V4 clean); admin authenticated with `Form.Template.Manage`
- **Data:** `POST /api/v1/forms/templates/form-id-01/publish`
- **Expected:** 200 OK; DB `IsPublished=true, Status=Active`; `FormTemplateVersion` record created (Version=1, immutable snapshot); audit `FormTemplatePublishedEvent`; subsequent save-graph call rejected (409/422)

#### TC-FORM-03-02: Publish — re-validation fails at publish time
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Template has dangling section reference introduced after last save (direct DB manipulation in test)
- **Data:** `POST /api/v1/forms/templates/form-id-01/publish`
- **Expected:** 422; message `"Cannot publish: validation errors exist"`; `IsPublished` remains false; template stays in draft

#### TC-FORM-03-03: Publish — version lock enforced (in-flight submissions keep snapshot)
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** NFR version semantics
- **Pre:** Template Version=1 published; existing FormSubmission references Version=1
- **Data:** Admin increments to Version=2 and publishes again; query existing submission schema
- **Expected:** Existing submission still references `FormTemplateVersion.Version=1` snapshot; new submissions use Version=2; no retroactive schema change (verify via `FormSubmission.TemplateVersionId` FK points to V1 record)

#### TC-FORM-03-04: Publish — idempotent (already published)
- **Pri:** P2 | **Type:** Positive | **Layer:** Integration | **Trace:** precondition
- **Pre:** Template already `IsPublished=true`
- **Data:** Re-submit `POST /publish`
- **Expected:** 200 OK (idempotent) OR 409; `IsPublished` remains true; no duplicate `FormTemplateVersion` row created; audit `NoOp` or no extra event

---

## UC-FRM-04: Render Runtime Form (Read Schema + Permission Scopes)

Source: `uc-forms-bp.md#uc-frm-04` — Endpoint `GET /api/v1/forms/public/{id}/schema`.

#### TC-FORM-04-01: Render schema — happy path with field scopes
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** Template published; user role `EMPLOYEE` (can see public fields, not admin-only); tenant-a context
- **Data:** `GET /api/v1/forms/public/form-id-01/schema` với `Authorization: Bearer <token>`
- **Expected:** 200 OK; response includes all sections + fields; field `scope` resolved per role (`editable` for standard fields, `read-only` for locked fields, admin-only fields absent from payload); response time <100ms

#### TC-FORM-04-02: Render schema — admin-only field hidden from non-admin
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** step 3 (field-level permission)
- **Pre:** Template has field `InternalNotes` with admin-only scope; caller has role `EMPLOYEE` (no admin perm)
- **Data:** `GET /api/v1/forms/public/form-id-01/schema`
- **Expected:** 200; response sections/fields does NOT include `InternalNotes`; field-scope resolution via `IEffectivePermissionService` applied (note: see Unresolved Q1 re escalation risk)

#### TC-FORM-04-03: Render schema — template not published returns 404
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** precondition
- **Pre:** Template `form-id-draft` has `IsPublished=false`
- **Data:** `GET /api/v1/forms/public/form-id-draft/schema`
- **Expected:** 404 Not Found; no schema data returned; no info leak on draft existence

#### TC-FORM-04-04: Render schema — cross-tenant template access blocked
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** NFR tenant isolation (memory anchor 260525-E)
- **Pre:** Template `form-id-01` belongs to `tenant-b`; caller JWT is `tenant-a`
- **Data:** `GET /api/v1/forms/public/form-id-01/schema` from tenant-a caller
- **Expected:** 404 (not 403, anti-information-leak); no cross-tenant data returned; audit `AccessDenied/cross_tenant`

---

## UC-FRM-05: Submit Form Data (Validation + Persist)

Source: `uc-forms-bp.md#uc-frm-05` — Endpoint `POST /api/v1/forms/submissions`.

#### TC-FORM-05-01: Submit form — happy path (standalone, no BP)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7 (no BP)
- **Pre:** Template published; user authenticated; all required fields present; no businessProcessId
- **Data:** `POST /api/v1/forms/submissions { "templateId": "uuid", "data": { "FullName": "John Doe", "Email": "john@example.com", "Reason": "Budget reallocation" } }`
- **Expected:** 200 OK; `FormSubmission` row created (SubmissionId, TemplateId, UserId, SubmittedAt, DataJson); PII fields (email) encrypted at rest; audit `FormSubmissionCreatedEvent`; no workflow instance created (bpId absent)

#### TC-FORM-05-02: Submit form — F1 required field missing
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** step 2 ext 2a (F1 fail)
- **Pre:** Template has `FullName` marked `isRequired=true`
- **Data:** POST submission omitting `FullName` field
- **Expected:** 422 Unprocessable Entity; error detail `{ "field": "FullName", "error": "Required field missing" }`; no `FormSubmission` row created

#### TC-FORM-05-03: Submit form — F3 regex validation fail
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** step 2 (F3 fail)
- **Pre:** Template field `Email` has `FormValidation.rule=email_pattern`
- **Data:** POST submission with `"Email": "not-an-email"`
- **Expected:** 422; error `{ "field": "Email", "error": "Invalid email format" }`; no submission persisted

#### TC-FORM-05-04: Submit form — F5 XSS input sanitized or rejected
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 2b (F5)
- **Pre:** Template text field `Reason` with no format restriction
- **Data:** POST with `"Reason": "<script>alert('xss')</script>"` in payload
- **Expected:** 422 "Invalid characters detected" OR sanitized value stored without `<script>` tag; security incident logged; no raw script stored in `FormSubmission.DataJson`

#### TC-FORM-05-05: Submit form — BP-linked triggers workflow instance
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 6 (BP trigger)
- **Pre:** Template published; BP definition active + linked workflow active; `businessProcessId` provided
- **Data:** POST with `"businessProcessId": "bp-id-01"` in payload; all required fields valid
- **Expected:** 200; `FormSubmission` created; `FormSubmissionCreatedEvent` published to Outbox; after Outbox consumer fires: `WorkflowInstance` created; response includes `workflowInstanceId`; audit `FormSubmissionCreatedEvent` + `BusinessProcessInstanceCreatedEvent`

#### TC-FORM-05-06: Submit form — rate-limit 11th submission in 1min
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** NFR rate-limit (max 10/min)
- **Pre:** User `submitter01` sent 10 submissions in last 60s
- **Data:** 11th `POST /api/v1/forms/submissions`
- **Expected:** 429 Too Many Requests; `Retry-After` header set; no submission persisted; audit `RateLimited`

---

## UC-FRM-06: Edit Form Data In-Flight (Update Before Task Complete)

Source: `uc-forms-bp.md#uc-frm-06` — Endpoint `PUT /api/v1/forms/submissions/{id}`.

#### TC-FORM-06-01: Edit in-flight — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-8
- **Pre:** FormSubmission `sub-id-01` exists (task not completed); user is original submitter; current `RowVersion=1`
- **Data:** `PUT /api/v1/forms/submissions/sub-id-01 { "data": { "FullName": "John Doe Jr.", "Email": "john.jr@example.com" }, "ifMatch": "<etag-v1>" }`
- **Expected:** 200 OK; `FormSubmission.DataJson` merged with new values; `RowVersion` incremented to 2; `ModifiedAt` updated; audit `FormSubmissionUpdatedEvent` with delta `{FullName, Email}` old+new values; response includes new ETag

#### TC-FORM-06-02: Edit in-flight — ETag mismatch (concurrent edit)
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 2-3
- **Pre:** Submission `RowVersion=2` (updated since caller last fetched v1)
- **Data:** PUT with stale `ifMatch: "<etag-v1>"`
- **Expected:** 412 Precondition Failed; no DB update; caller must re-fetch + retry; message `"Resource modified by another user"`

#### TC-FORM-06-03: Edit in-flight — task already completed blocks edit
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** Linked workflow task is `Status=Completed`
- **Data:** PUT with valid ETag
- **Expected:** 403 Forbidden; message `"Submission finalized, cannot edit"`; no DB mutation

#### TC-FORM-06-04: Edit in-flight — non-owner attempt blocked
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** NFR security (JWT sub check)
- **Pre:** Submission.UserId = `user-A`; caller JWT sub = `user-B` (not task assignee)
- **Data:** PUT from user-B
- **Expected:** 403 Forbidden; message `"Access denied: not submission owner or task assignee"`; audit `AccessDenied`

#### TC-FORM-06-05: Edit in-flight — audit trail delta recorded
- **Pri:** P2 | **Type:** Positive | **Layer:** Integration | **Trace:** NFR audit trail
- **Pre:** Submission exists; owner authenticated
- **Data:** PUT with single field change `"Email": "updated@example.com"`
- **Expected:** `FormSubmissionUpdatedEvent` audit record contains `{ field:"Email", oldValue:"john@example.com", newValue:"updated@example.com", changedBy:"user-A", timestamp }` — queryable via UC-BP-04 status endpoint

---

## UC-BP-01: Tạo Business Process Definition

Source: `uc-forms-bp.md#uc-bp-01` — Endpoint `POST /api/v1/business-processes`.

#### TC-BP-01-01: Create BP definition — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5
- **Pre:** Admin với perm `BusinessProcess.Manage`; form template `form-id-01` published; workflow definition `wf-def-01` active
- **Data:** `POST /api/v1/business-processes { "name": "Employee Request Approval", "formTemplateId": "form-id-01", "workflowDefinitionId": "wf-def-01", "isActive": false }`
- **Expected:** 200 OK; DB `BusinessProcessDefinition` created (`Status=Draft, FormMaps=[]`); audit `BusinessProcessDefinitionCreatedEvent`; FE initial state shows blank FormMaps list (manual: verify designer canvas is empty on first open — Layer: Manual)

#### TC-BP-01-02: Create BP — form template not published
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 1a
- **Pre:** `form-id-draft` has `IsPublished=false`
- **Data:** POST with `"formTemplateId": "form-id-draft"`
- **Expected:** 400 Bad Request; message `"Form template must be published"`; no BP row created

#### TC-BP-01-03: Create BP — workflow definition not active
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 1b
- **Pre:** `wf-def-inactive` has `IsActive=false`
- **Data:** POST with `"workflowDefinitionId": "wf-def-inactive"`
- **Expected:** 400; message `"Workflow definition must be active"`; no BP row created

#### TC-BP-01-04: Create BP — missing perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** precondition
- **Pre:** Caller without `BusinessProcess.Manage`
- **Data:** Valid POST body
- **Expected:** 403; message `"Insufficient permissions"`; no DB write

---

## UC-BP-02: Cập nhật Form-Map Binding

Source: `uc-forms-bp.md#uc-bp-02` — Endpoint `POST /api/v1/business-processes/{id}/form-maps/replace`.

#### TC-BP-02-01: Replace form-maps — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-6
- **Pre:** BP definition `bp-id-01` draft; workflow states `[123]` + transitions `[456]` exist; form fields `[ReviewNotes]` exist; current ETag valid
- **Data:** `POST /api/v1/business-processes/bp-id-01/form-maps/replace` with `stateForms`, `transitionForms`, `fieldMappings`, `ifMatch` (per UC payload spec)
- **Expected:** 200 OK; DB `BusinessProcessFormMap` rows replaced atomically; audit `FormMapReplacedEvent` với `state_form_count=1, transition_form_count=1`; single SaveChanges()

#### TC-BP-02-02: Form-map orphan-delete contract (EF Core 10 raw-SQL)
- **Pri:** P0 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 4 (memory anchor 260524-A/B/C — CRITICAL)
- **Pre:** BP `bp-id-01` has 2 state-form records (`Id>0`) in DB: stateForm-A (stateId=100) + stateForm-B (stateId=200); caller submits replace with only stateId=100 (stateForm-B removed from payload)
- **Data:** POST `/form-maps/replace` payload omitting stateId=200; valid ETag
- **Expected:** After SaveChanges(): DB `BusinessProcessFormMap` contains zero rows for stateId=200; `ExecuteDeleteAsync` with `Unchanged∪Deleted` filter applied (raw-SQL path); no residual orphan rows; orphan-delete runs atomically in same SaveChanges() as INSERT of new rows; verify via `SELECT COUNT(*) FROM BusinessProcessFormMaps WHERE stateId=200` = 0

#### TC-BP-02-03: Form-map validation M1 — state ID not in workflow
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** step 2 ext 2a (M1 fail)
- **Pre:** Workflow definition has states `[123]` only
- **Data:** POST payload with `"workflowStateId": 999` (non-existent)
- **Expected:** 422; message `"State ID 999 not found in workflow definition"`; no DB write

#### TC-BP-02-04: Form-map validation M2 — form field name invalid
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** step 2 ext 2b (M2 fail)
- **Pre:** Form template has field `ReviewNotes` only
- **Data:** POST payload with `"formFieldName": "NonExistentField"`
- **Expected:** 422; message `"Field 'NonExistentField' not found in form template"`; no DB write

#### TC-BP-02-05: Form-map — ETag required; missing If-Match returns 412
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** NFR ETag (D-B-3)
- **Pre:** BP `bp-id-01` exists
- **Data:** POST replace without `ifMatch` field in payload
- **Expected:** 412 Precondition Required or 400; message `"If-Match header required"`; no DB mutation

#### TC-BP-02-06: Form-map — cross-tenant BP access returns 404
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** NFR tenant isolation (memory anchor 260525-E)
- **Pre:** BP `bp-id-01` belongs to `tenant-b`; caller JWT is `tenant-a`
- **Data:** `POST /api/v1/business-processes/bp-id-01/form-maps/replace` from tenant-a admin
- **Expected:** 404 Not Found (not 403, anti-leak); no mutation; audit `AccessDenied/cross_tenant`

---

## UC-BP-03: Khởi tạo BP Instance (Trigger via Form Submit)

Source: `uc-forms-bp.md#uc-bp-03` — Async event handler (no HTTP endpoint); triggered by `FormSubmissionCreatedEvent`.

#### TC-BP-03-01: Trigger BP instance — happy path (end-to-end via form submit)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-8
- **Pre:** BP `bp-id-01` active; workflow `wf-def-01` active + has initial state; form submitted with `businessProcessId=bp-id-01`
- **Data:** Trigger via TC-FORM-05-05 (form submit with bpId); Outbox consumer picks up `FormSubmissionCreatedEvent`
- **Expected:** `WorkflowInstance` created (verify row in DB); initial `WorkflowTask(s)` created in candidate pool; `BusinessProcessInstanceCreatedEvent` logged; task visible in eligible user's inbox (candidate index updated); form-map applied: field `ReviewNotes` mapped to workflow variable `ApproverComments`

#### TC-BP-03-02: Trigger BP — BP definition inactive; graceful degrade
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** BP `bp-id-inactive` has `IsActive=false`; form submitted with `businessProcessId=bp-id-inactive`
- **Data:** `FormSubmissionCreatedEvent` with inactive BP ID consumed by handler
- **Expected:** Handler logs warning + aborts workflow creation; `FormSubmission` row persists (form data not lost); no `WorkflowInstance` created; user notified `"Form submitted; process workflow inactive"`; no hard failure/exception propagated

#### TC-BP-03-03: Trigger BP — Outbox retry on transient WF client failure
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** ext 5a
- **Pre:** `IWorkflowModuleClient.CreateInstanceAsync()` throws transient `HttpRequestException` on first call
- **Data:** Outbox consumer fires; first attempt fails; retry mechanism invoked
- **Expected:** Outbox retries up to 3 times (verify retry count in DB Outbox table); on 3rd attempt success: `WorkflowInstance` created; if 3 consecutive failures: message moved to DLQ + ops alert dispatched

#### TC-BP-03-04: Trigger BP — form-map field nulls handled gracefully
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** NFR form-map application
- **Pre:** Form submission has optional field `BudgetAmount=null`; workflow variable `BudgetVar` is optional
- **Data:** BP instance trigger with null optional field
- **Expected:** `WorkflowInstance` created without error; workflow variable `BudgetVar` absent from initial data (not set to null string); no validation exception from workflow handler

#### TC-BP-03-05: Trigger BP — cross-module tenant consistency
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** NFR security (tenant scope)
- **Pre:** Form in `tenant-a`, BP in `tenant-a`, workflow in `tenant-b` (mis-wired in test)
- **Data:** Trigger BP instance with cross-tenant workflow definition
- **Expected:** BP handler detects tenant mismatch; aborts instance creation; error logged; no `WorkflowInstance` created in wrong tenant; audit `TenantMismatch/bp_id/wf_tenant_mismatch`

---

## UC-BP-04: Xem BP Status (Form + Workflow Composite)

Source: `uc-forms-bp.md#uc-bp-04` — Endpoint `GET /api/v1/business-processes/{bpInstanceId}`.

#### TC-BP-04-01: View BP status — happy path (composite aggregation)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-4
- **Pre:** BP instance `bp-inst-01` created via UC-BP-03; form has edits (UC-FRM-06 executed once); workflow has 1 completed task + 1 in-progress task
- **Data:** `GET /api/v1/business-processes/bp-inst-01`
- **Expected:** 200 OK; response contains `formData.submissionId`, `formData.data`, `formData.edits[1 edit record]`; `workflowState.currentState`, `workflowState.tasks[2 tasks]` with correct assignees + timestamps; Dapper query <100ms; total response <200ms

#### TC-BP-04-02: View BP status — instance not found
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 1a
- **Pre:** No BP instance with id `non-existent-bp-inst`
- **Data:** `GET /api/v1/business-processes/non-existent-bp-inst`
- **Expected:** 404 Not Found; message `"BP instance not found"`; no data leak

#### TC-BP-04-03: View BP status — access denied (no permission)
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** ext 1b
- **Pre:** BP instance `bp-inst-02` in tenant-a; caller has neither `Form.View` nor `Workflow.View`
- **Data:** `GET /api/v1/business-processes/bp-inst-02` without required permissions
- **Expected:** 403 Forbidden; message `"Access denied"`; no composite data returned

#### TC-BP-04-04: View BP status — PII fields masked for low-permission caller
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** NFR security (PII masking)
- **Pre:** Form submission includes `Email` field (PII); caller has `Form.View` but NOT `Form.ViewPII`
- **Data:** `GET /api/v1/business-processes/bp-inst-01`
- **Expected:** 200 OK; `formData.data.Email` value is masked (e.g., `"j***@example.com"` or absent); other non-PII fields visible normally

---

## Test Data Requirements

| Entity | Records | Notes |
|--------|---------|-------|
| Tenants | tenant-a, tenant-b | Cross-tenant isolation TCs |
| FormTemplates | form-id-01 (published), form-id-draft (draft) | Both states needed |
| FormSections | sec-1 (Personal Info), sec-2 (Justification) | Per form-id-01 |
| FormFields | fld-1 (FullName, required), fld-2 (Email, PII), fld-3 (orphan candidate) | Orphan-delete TCs |
| FormSubmissions | sub-id-01 (in-flight), sub-id-final (completed task) | Edit + lock TCs |
| WorkflowDefinitions | wf-def-01 (active, states [123, 200]), wf-def-inactive | BP creation + trigger TCs |
| BusinessProcessDefinitions | bp-id-01 (draft), bp-id-inactive, cross-tenant bp | Form-map + trigger TCs |
| BusinessProcessInstances | bp-inst-01 (1 completed + 1 active task), bp-inst-02 (no perm user) | Status TCs |
| Users | admin01 (Form.Template.Manage + BusinessProcess.Manage), submitter01 (Form.Submit), viewer01 (Form.View only) | Role-scoped TCs |

Tham chiếu `tests/AqtCoreFW.Tests.Integration/Infrastructure/Seeders/E2ETestDataSeeder.cs` để seed entity trước khi chạy TC suite.

---

## Execution Notes

- **Manual run:** Postman collection từ Swagger `/swagger/v1/swagger.json`; chạy Form TCs trước BP TCs (BP depends on published Form)
- **TC-FORM-02-02 + TC-BP-02-02 orphan-delete:** Requires raw DB assertion after SaveChanges — instrument test với `dbContext.Database.ExecuteSqlRaw("SELECT COUNT(*)")` or Dapper probe; cannot rely solely on HTTP response
- **Automation roadmap:** TC IDs map 1-1 sang `[Fact]` method names in xUnit integration tests (e.g., `TC_FORM_02_02_OrphanDelete_ExecuteDeleteAsync_UnchangedDeletedFilter`)
- **Cleanup:** `IClassFixture<DatabaseFixture>` với transaction rollback per TC; orphan-delete TCs seed specific Id-anchored rows (Id > 0 constraint per memory 260524-C)
- **Idempotency:** TC-FORM-05-06 (rate-limit) requires rate-limit counter reset before each run; serialize via `[Collection]`

---

## Unresolved Questions

1. **UC-FRM-04 IEffectivePermissionService privilege-escalation (memory 260524-G):** Field-scope resolution uses effective permissions (merging role + group assignments). If `IEffectivePermissionService` path escalates admin-only field visibility for users with indirect group-role, TC-FORM-04-02 expected "hidden" behavior may fail. Confirm: does field-scope use direct-role-only check or full effective-permission merge?
2. **UC-BP-02 ETag/version semantics on form-map replace:** Does `ifMatch` bump only `BusinessProcessFormMap` RowVersion or also `BusinessProcessDefinition.Version`? If BP version bumps on every form-map replace, activated instances may lose consistency. Needs source confirm from `ReplaceFormMapsCommandHandler`.
3. **UC-FRM-03 FormTemplateVersion entity shipped status:** UC claims immutable snapshot at publish (Version=1 FK on FormSubmission). Verify `FormTemplateVersion` entity and `FormSubmission.TemplateVersionId` FK exist in current codebase at v2.137p113 before writing TC-FORM-03-03 automation. If not shipped, TC is spec-only until Phase N.
4. **UC-BP-03 Outbox retry count:** UC specifies max retries = 3. No source citation in UC. Confirm against actual `ResilientOutboxConsumerOptions` configuration in `appsettings.json` (may differ from spec assumption).
5. **TC-FORM vs TC-FRM prefix convention:** Task spec overrides pilot derivation rule (pilot: prefix mirrors UC module code → `TC-FRM`; task: explicitly uses `TC-FORM`). QA tooling that auto-links by prefix must accommodate this divergence. Recommend aligning to `TC-FRM` in future revision, or codifying `TC-FORM` as canonical in the Conventions section of tc-identity-core.md pilot.
