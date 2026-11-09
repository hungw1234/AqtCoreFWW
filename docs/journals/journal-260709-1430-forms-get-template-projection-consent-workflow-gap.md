# Journal — FORMS-GET-TEMPLATE-PROJECTION-CONSENT-WORKFLOW-GAP (T4 bug-fix)

**Date:** 2026-07-09 ~14:30 ICT  
**Severity:** Medium (data-sync bug, zero data-loss)  
**Component:** Forms.API (GET single-template projection)  
**Status:** Resolved

---

## What Happened

T4 backlog row `FORMS-GET-TEMPLATE-PROJECTION-CONSENT-WORKFLOW-GAP` surfaced during code-review of the same-session `PUBLIC-FORMS-ISPUBLIC-ADMIN-TOGGLE` (IsPublic carve-out). The `GetFormTemplateQueryHandler` Dapper projection for single-template GET was missing 3 fields: `RequiresConsent`, `ConsentText`, `ApprovalWorkflowDefinitionId`. These fields were already declared in `FormTemplateDto` (trailing optionals, DTO contract stable), but the SELECT query omitted them. Result: FE Settings-tab consent + approval-workflow toggles were write-only (PUT persisted values, but GET reload showed CLR defaults).

---

## The Brutal Truth

This is a textbook silent bug. The fields aren't nullable in the schema, the DTO declaration is fine, and integration tests were green—because tests didn't exercise the reload-after-write pattern that exposes the gap. The projection completeness trap is invisible: optional fields that silently default feel fine until a user toggles a setting, saves it, and watches it revert on page-reload. Caught proactively by code-review before hitting the field.

---

## Technical Details

**The Defect:** `templateSql` in `GetFormTemplateQueryHandler` (Dapper, reading `forms.FormTemplates` by `PublicId`) omitted 3 columns.
```sql
-- BEFORE
SELECT Id, PublicId, TenantId, Name, NameVi, Code, StorageMode, Status, Version, CreatedAt, IsPublic
  FROM forms.FormTemplates
  WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0;

-- AFTER: 3 columns added (RequiresConsent, ConsentText, ApprovalWorkflowDefinitionId)
SELECT Id, PublicId, TenantId, Name, NameVi, Code, StorageMode, Status, Version, CreatedAt,
       RequiresConsent, ConsentText, ApprovalWorkflowDefinitionId, IsPublic
  FROM forms.FormTemplates
  WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0;
```

**Files Changed:**
- `src/modules/forms/AqtCoreFW.Forms.Application/Queries/GetFormTemplate/GetFormTemplateQueryHandler.cs` — Added 3 columns to the Dapper SELECT + 3 props on the `FormTemplateRow` materialization class + passed them via **named args** into the `FormTemplateDto` ctor (`RequiresConsent:` / `ConsentText:` / `ApprovalWorkflowDefinitionId:`), matching the `IsPublic:` pattern to stay positional-safe.
- `src/tests/AqtCoreFW.Tests.Integration/Forms/FormTemplatesApiTests.cs` — New regression test: baseline defaults (GET after CREATE), persist via PUT, verify GET shows persisted values (not defaults). 1/1 non-vacuous.

**Note:** `IsPublic` was already projected correctly (added earlier the same session in the admin-toggle ship); these 3 consent/workflow fields were the remaining gap in the same projection. No further omissions found in this handler.

---

## Quality Gates

- **BE build:** 0 warnings, 0 errors ✓
- **BE integration:** FormTemplatesApiTests 14/14 (1 new regression, 13 existing, 0 regression) ✓
- **Code review:** APPROVE_WITH_NITS (0 Critical/High, projection completeness audit comment noted) ✓

---

## Lessons Learned

**1. Optional DTO fields + silent Dapper defaults = write-only silently.** When a DTO declares optional fields but the projection omits them, the CLR defaults take over. Add a reload-after-write regression test to any DTO-level optional field — the test is the only way to surface the gap.

**2. Projection-completeness audits should be systematic.** Found this during manual code-review; a static check (DTO props vs. SELECT aliases) would catch all gaps in one pass. Backlog item for future.

---

**Status:** DONE  
**Summary:** Bug-fix: GET single-template projection was missing 3 fields (RequiresConsent, ConsentText, ApprovalWorkflowDefinitionId), causing write-only silently. Added columns; regression test + audit cleanup flagged.  
**Concerns:** None — all tests green, review approved.
