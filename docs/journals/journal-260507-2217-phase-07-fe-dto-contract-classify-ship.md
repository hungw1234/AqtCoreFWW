# Phase-07 PHASE7-FE-DTO-CONTRACT-CLASSIFY — Ship Journal

**Date:** 2026-05-07 22:17 | **Branch:** feat/phase-07-fe-dto-contract-classify

## What shipped

### Phase-01: NotificationTemplate T1→T2 Retier

Retiered `NotificationTemplate` from T1 (long Id only) to T2 (long Id PK internal + Guid PublicId AK external) following the FormTemplate (PR-A) and MfaPolicy (PR-1) precedents.

Key changes:
- `NotificationTemplate` entity: added `IPublicIdentified`, `Guid PublicId { get; private set; }`, factory `Create(IGuidGenerator gen, ...)`
- `INotificationTemplateRepository`: removed `GetByIdAsync(long)`, added T2 trio: `GetByPublicIdAsync`, `GetByInternalIdAsync`, `GetInternalIdByPublicIdAsync`
- `SendNotificationCommand.TemplateId`: `long?` → `Guid?`; handler resolves PublicId→internal long FK via `GetInternalIdByPublicIdAsync` before creating `Notification` and `NotificationLog` records
- Migration `20260507144339_AddNotificationTemplatePublicId`: `AddColumn<Guid>` with default Guid.Empty + SQL `UPDATE SET PublicId = NEWID()` backfill + unique index
- Admin API controller: all routes use `{publicId:guid}`, Create returns `Guid`
- `NotificationTemplateDto.Id`: `long` → `Guid`; Dapper queries `SELECT PublicId AS Id`
- `NotificationTemplateSeeder`: wired `IGuidGenerator`

Bug found and fixed: `CreateClientWithPermissions` in `NotificationEndpointsTests` was missing `MfaAtHeader` — the `[RequiresMfa]` class-level attribute on the admin controller was returning 403 on all template management test calls. Pattern confirmed by matching failure in `WorkflowTaxonomyAdminEndpointsTests` (pre-existing, separate scope). Fixed by adding `TestAuthHandler.MfaAtHeader` = `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` to the helper.

### Phase-02: ACCEPT-LONG Annotations (22 surfaces)

Added `// arch:accept-long T1 <Entity>: <reason>` directives on 22 FE-facing long fields for 3 T1 entities that are deliberately kept T1 (not retier candidates):

- **WorkflowTransition (7):** Graph FK topology addressed via Name natural key in SaveDefinitionGraph — long Id is opaque round-trip handle
- **WorkflowState (6):** Aggregate-internal state identity — FE treats as opaque, analytics consumers use stateName for display
- **FormField (9):** Child of T2 FormTemplate aggregate — addressed via parent PublicId; FieldId stays internal

### Phase-03: CSV + Docs Sync

- CSV row 76: `NotificationTemplate T1 internal-only` → `T2 guid-ak /api/v1/admin/notification-templates/{publicId:guid}`
- `dependency-map.md`: T1 examples list updated, T2 list includes NotificationTemplate with route sig, entity table updated with T2 note
- `project-changelog.md`: v2.137 entry
- `development-roadmap.md`: version bumped, PHASE7-FE-DTO-CONTRACT-CLASSIFY closed
- `backlog.md`: item marked CLOSED
- `completed.md`: entry added

## Test results

| Suite | Before | After |
|-------|--------|-------|
| Architecture | 26/26 | 26/26 |
| Notifications.Application unit | 26/26 | 26/26 |
| Notifications.Integration | - | 7/7 PASS |
| FE tsc | 0 errors | 0 errors |

## Architecture baseline note

Plan specified 27/27 — actual baseline on main was 26/26 (MigrationConsistencyTests was already failing pre-change). Our migration + snapshot update fixed it. No regression introduced.

## Key decisions

1. **T1 entities kept T1** (WorkflowTransition, WorkflowState, FormField): These are aggregate-internal children or FK-graph nodes addressed by natural keys (ActionName). Retiering would break graph topology. `arch:accept-long` directive codifies intent at point of use.

2. **Internal FKs untouched**: `Notification.TemplateId (long?)` and `NotificationLog.TemplateId (long)` remain long — within-module FK. Only the command input changed from long to Guid.

3. **MFA in integration tests**: `[RequiresMfa]` on admin controllers requires `mfa_at` claim. `CreateClientWithPermissions` pattern must always include `MfaAtHeader`. This is pre-existing gap in MasterData tests too — out of scope for this PR.
