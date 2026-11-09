# Journal — Forms orphan-workflow-link retry (BE Phases 04-05)

**Date:** 2026-08-04 · **Commits:** `ce07910d6` (P04) + `de176756a` (P05), NOT pushed (2 ahead origin/main)
**Plan:** `plans/260803-2219-forms-orphan-workflow-link-retry/`

## Session goal

Chuyển red-team M1 (`SIGN-WFLINK-RETRY-CREATE-IDEMPOTENT` + `P6-DEFER-1` Ph04-07, backlog "won't-compile-as-is") thành feature thật: retry job cho orphan Forms→Workflow instance-link + admin override API. Bắt đầu từ `/aqt-next` → design-pass → 4 product-input decisions → plan 4-phase → implement BE (Phase 04+05), gated per-phase (interactive).

## What worked (verified + commit refs)

- **Design-pass bác một phần nhãn backlog** (INVARIANT 260522-A): "won't-compile-as-is" chỉ đúng cho retry-job CHƯA viết. Scout tìm ra idempotency-guard seam ĐÃ tồn tại (`IWorkflowModuleClient.TryResolveInstanceForEntityAsync`) + DB unique-index (`UX_WorkflowInstances_Active_Entity`) + retry-claim EF index đều pre-provisioned. Real gap = entity mutators + Hangfire job + admin API, không phải hạ tầng.
- **Phase 04 (`ce07910d6`):** 6 lifecycle mutators (GOV_FRM_040-045) + `GetDueAttemptsAsync` (IgnoreQueryFilters cross-tenant claim, NULL-branch included) + `FormWorkflowLinkRetryJob` ([AutomaticRetry(0)], per-row `uow.BeginAsync(tenant)` RLS pin, resolve-before-create M1 guard, exponential backoff→Shelved dead-letter). Tests: Domain 178, App 350, Arch 207, job integration 5/5. **Full Forms integration regression 51/51 GREEN** (58m). Review APPROVE-WITH-NITS.
- **Phase 05 (`de176756a`):** admin API GET list (Dapper, tenant-scoped, 4 statuses) + POST retry-now (Q4 status-branch) + POST shelve + PermCode `FORMS.WFLINK.MANAGE` (least-priv, seed+grant Admin/SystemAdmin). Tests: app 362, integration 11, arch 12. Review APPROVE-WITH-NITS.
- **Reviewer xác nhận durability property tốt:** vì workflow-create commit trên SaveChanges riêng, crash-after-create SELF-HEALS qua đường resolve-before-create ở tick sau.

## What didn't / lessons

- **3 bug thật bị integration-test bắt** (không phải false-green — bằng chứng test có giá trị):
  - Dapper positional-record + `DateTimeOffset` vs entity `DateTime` trên `CreatedAt` → InvalidOperationException opaque. Fix: record→class + retype. (Lặp lại gotcha `AuditLogDto`.)
  - `FormTemplateId` là internal `long` T1 PK, không phải Guid AK → InvalidCastException. Fix: JOIN project `t.PublicId AS FormTemplateId` (mirror `ListFormSubmissions`).
  - Backward trap: claim-index filtered `NextRetryAt IS NOT NULL` → fresh orphan (`NextRetryAt=null`) bị bỏ sót. Fix: due-predicate include NULL-branch. (Planner tự bắt trong Blast Radius axis 1.)
- **Full integration regression chậm bất thường** (58m, cold-cache + mỗi test-class boot 1 full app-host). Không treo — verify bằng live app-host log mtime. Bài học: integration-full trên laptop quá chậm để làm gate đồng bộ; nhưng user chọn chờ (kỷ luật HARD-GATE cho background job chạm data).

## Decisions made

- **4 product-input (user 2026-08-03, FULL combo despite lean advisory):** Q1 retry=BOTH (auto Hangfire + manual) · Q2 dead-letter=Shelve+log (không cancel submission) · Q3 admin UX=dedicated FE page · Q7 backfill=full script+runbook.
- **5 technical (user 2026-08-03):** MaxAttempts=10/backoff 5m→6h/cron */5 · shelve alert=log-only (Prometheus defer) · PermCode=new `FORMS.WFLINK.MANAGE` · manual un-shelve=YES (`ReactivateForManualRetry`, manual-path only; auto job treats Shelved terminal) · backfill audit-cols=enumerate at impl-time.
- **Commit BE trước FE (user):** 2 commit sạch theo phase, tree clean, chưa push.

## Test status

- Phase 04: Domain 178, App 350, Arch 207, job integration 5/5, **full Forms regression 51/51**.
- Phase 05: App 362, integration 11 (RT6a×4/RT6b×2/List×4/perm-boot×1), Arch 12.
- Build 0/0 dưới /warnaserror. 2 code-review APPROVE-WITH-NITS (0 CRITICAL/MAJOR mỗi phase).

## Next session (carry-over)

- **Phase 06 (~1d):** FE 'Pending Workflow Links' page + TanStack route + static menu `admin-menu-entries.ts` + i18n, gate `FORMS.WFLINK.MANAGE` (byte-match BE constant, 6-site symmetry). Vitest. Render ALL 4 statuses. Needs `ui-ux-designer`.
- **Phase 07 (~1d):** full backfill script + runbook + tests finalize + docs (dependency-map + sửa stale interface doc-comment M-2: `GetDueAttemptsAsync` doc nói `AttemptCount<max` predicate nhưng cap sống ở job body `:197`, không claim query — correctness-safe, chỉ doc drift).
- **M-3 (Phase 07 test-hardening):** RT1-5 share class-level static TenantId + job claim scans all-tenants → latent cross-test row-bleed nếu suite lớn; make TenantId per-test. Không production bug.
- **Push:** 2 commit chưa push — user quyết timing.
- **Advisory nhắc:** feature này KHÔNG launch-blocker; cân nhắc ưu tiên launch-critical (soak-staging/prod-infra/posture-decisions) so với Phase 06-07 polish.
