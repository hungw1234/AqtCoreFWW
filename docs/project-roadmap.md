# AqtCoreFW Project Roadmap

**Last updated:** 2026-07-14 | **Current branch:** `main` | **SQL Server:** 2022 primary (Oracle work migrated to separate repo). See `backlogs/backlog.md` for active feature pipeline and `docs/project-changelog.md` for detailed commit history.

---

## Current Status

**v2.137p117 (2026-05-26 — baseline for this refresh)**
- **15 modules live** (Module isolation guards 145/145 PASS).
- **Test baseline:** 3080 tests (Architecture 105/105, Forms 317/317, Workflow 577/577, etc.). See backlog for live count.
- **Production gates:** Vault integration, Hangfire scheduler, RLS least-privilege cutover (pending).
- **Architecture:** Modular monolith (74 projects), SharedKernel contracts-first, per-module DDD, Testcontainers integration tests.
- **Status:** Feature-complete on flagship modules (Identity, Workflow, Forms, Rules, etc.). Phase-oriented delivery with red-team validation on complex features.

---

## Strategic Direction (2026-07-16 — Stable Modular Monolith for Launch Priority)

**Decided (user 2026-07-16):** 🥇 **Shipping a stable modular monolith for launch is the #1 priority** — this reverses the 2026-07-10 directive that made microservices transformation the always-propose-first track. **Rationale:** strangler-fig extraction needs a stable base to extract from, and reaching stable-monolith-in-production generates the two signals the extraction decision was waiting on — FU-A auditor-answerability (a real deployed system to audit) and production load-evidence (the scale gate, currently at 0 pre-production). **Definition-of-Stable (exit criteria):** functional-complete to launch scope (**launch feature-set is user-TBD — open item**), operational robustness (clean boot/migration/seed), quality (green non-flaky suite + CI enforcing), data/security (RLS + audit-chain + Vault + backup + the pending audit read-isolation OPS activation), observability wired, prod deploy + soak.

**2026-07-23 — Deploy target DECIDED (leg 6):** **prod deploy target = Kubernetes** (user decision, closes P1-audit §4-Q1). Local rehearsal path: Docker Desktop kind cluster (1 node, containerd image store) executing `plans/260723-1258-k8s-local-stable-monolith-deploy/` — P0 docs → P1 first-ever helm render/install (api + authserver + new minimal web chart) → P2 semantic flips (Production env + Vault RequiredKeys, `Bus:Transport=RabbitMQ`, Demo-seed OFF, NonAudit least-priv rehearsal) → P3 Prometheus Operator + soak. ⚠️ NetworkPolicy carve-out: kindnet does NOT enforce netpol — verification only on Calico/staging. Same day, Definition-of-Stable legs 1-3 all reached 🟢 (leg 1 functional-complete 68/68 UC with UC-MD-02 descoped by decision; leg 2 boot-clean empirical; leg 3 zero production bugs — 182 suite reds convicted as test-infra/pollution, fixed via PR #134).

**2026-07-16 P1 operational-readiness update:** the P1 audit (`plans/reports/audit-260716-1805-stable-monolith-p1-operational-readiness-report.md`) found 13 gaps → 8 backlog rows, 2 tagged 🔴 T1 launch-blocker. **Both T1 blockers are now RESOLVED same-day:** `P1-STABLE-BACKUP-AUTOMATION` (recurring Hangfire `scheduled-backup` job + retention pruning + restore runbook) and `P1-STABLE-INTEGRATION-TEST-CI-GATE` (new `smoke-tests` CI job running `AqtCoreFW.Startup.SmokeTests` against a SQL Server service-container — full 197-test integration suite stays `if:false`, local-dev-only; **CI-gate green not yet observed, pending a push**). Detail → `backlogs/completed.md` §2026-07-16, `docs/project-changelog.md`. Remaining P1 rows (2×T2, 3×T4) still open; 1 low-priority follow-up filed (`BACKUP-RESTORE-DRILL-TEMP-CLEANUP`). **2026-07-21:** `ALERTING-LOGCRITICAL-HAS-NO-ALERT` RESOLVED (PR #126) — closed a pre-existing gap where application Prometheus counters were exported via OTLP only and invisible to the Prometheus-querying alert evaluator; added a `/metrics-otel` scrape endpoint, self-monitoring failure counters, and a disabled-by-default self-monitor `AlertRule`. Follow-up `PROMETHEUS-DEADMAN-SWITCH-MISSING` partially shipped same day (Helm `PrometheusRule` template, default-off everywhere) and moved to the "Waiting-on-infrastructure" backlog group — CoreBase has never been deployed to production, so nothing here can be verified against a real cluster yet. Detail → `docs/project-changelog.md`. **2026-07-21 evening, 2 further rows RESOLVED and MERGED:** `NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE` (PR #127) — SMS notifications addressed the recipient by user id, so every send failed at the provider; fixed via a new PII-scoped `IIdentityModuleClient.GetUserPhoneNumbersByIdsAsync` rather than widening the shared `UserInfoDto`, and the fix armed a dormant PII leak in an untouched log line that was masked in-slice. `ALERTING-TESTRULE-REPORTS-202-ON-FAILED-DELIVERY` (PR #128) — the admin test button re-ran every enabled rule (firing other rules' real alerts and recording breaches against them), skipped disabled rules entirely, and could not report delivery failure; it now evaluates exactly one rule and returns the outcome, with a `source` tag keeping test presses out of the self-monitor alerts shipped that morning. 3 follow-ups filed (`NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL`, `ALERTING-PROMTOOL-FIXTURES-UNVERIFIED`, `ALERTING-SOFTDELETE-COMMENTS-CONTRADICT`). Detail → `docs/project-changelog.md`, `backlogs/completed.md`.

**2026-07-31 Launch-scope signing decision:** `SIGN-FULL-PDF-CRYPTO-VERIFY` **de-scoped from `[blocks-v1.0]` → `[v1.1]`** (user decision, advisor-backed). Launch signing path = form-generated PDF + USB client-side stamping only; untrusted user-uploaded PDF signing and its crypto-verify chain (Phase-4 sandbox → Phase-5 remote PAdES assembler → Phase-6 signature verification, ~18–25d effort) deferred to v1.1. The upload-arbitrary-PDF sign path is now feature-flagged OFF (flag `feature:signing.upload-arbitrary-pdf`, default false all envs; enforced at `RequestSignatureCommandHandler` returning 409, hidden on FE). Residual gap is repudiation, not intrusion (documented "not-verified" caveat stays). Re-escalate to `[blocks-release]` if: (a) flag is re-enabled; (b) inbound liên-thông enters scope; (c) Phase-5 remote PAdES assembly ships. At that point Phase-4 sandbox MUST precede per locked chain order. Detail → `backlogs/backlog.md` row `SIGN-FULL-PDF-CRYPTO-VERIFY` + advisor report `plans/reports/advisor-260731-1443-sign-pdf-verify-launch-scope-report.md`.

**Microservices transformation is DEMOTED to a gated/deferred track (NOT cancelled)** — sequenced after the stable monolith launches, gated on (a) launch + (b) the FU-A auditor answer + (c) production load-evidence. **Prior (2026-07-10 → 2026-07-16, now superseded as the #1 framing):** microservices transformation was the #1 priority and every next-task recommendation had to propose the next microservices step first; Foundation (Sóng-0) completed under that directive. **Superseded 2026-07-13:** Sóng-1 (S4 extraction) is **DEFERRED** — a 4-lens red-team + the C3 schema-GRANT spike disproved S4's compliance-isolation driver (schema-scoped GRANT already gives it on the shared monolith instance); physical extraction is reserved for S1/S2 instead. The residual GOV auditor-delta question (FU-A, `docs/microservices-compliance-isolation-auditor-questions.md`) is the only condition that reopens S4/physical extraction and is still pending. **2026-07-14:** the monolith-side audit read-path isolation this deferral opened up (EF + Dapper) is now **code-complete, ops-activation-pending** (`ISO-EF-FIRST-AUDIT-BOUNDARY` + `ISO-DAPPER-KEYED-DI-INCREMENT` — see Recently Shipped below). **2026-07-16:** the completeness-matrix build-now CODE track (cells #3/#5/#6/#7/#10) is EXHAUSTED regardless of this priority reversal — the only remaining microservices action is FU-A. Master plan SSOT → [`docs/microservices-transformation-roadmap.md`](./microservices-transformation-roadmap.md) (ADR-012, §5 Wave Sequencing).

- **Ranked next-task tracks:** (1) **🥇 Stable Modular Monolith for Launch** — next actionable is pending the user-TBD launch feature-set (open item); see Definition-of-Stable above. (2) **Microservices transformation** (demoted/gated, not cancelled) — next actionable once gated = ops-activate the `NonAudit` login (run `scripts/260714-nonaudit-login-provisioning.sql` + publish the Vault key) and/or put the FU-A auditor question to GOV; the first physical-extraction wave stays **TBD** pending stable-monolith launch + that answer + production load-evidence (wave seq if/when it resumes: Sóng-0 ✅ → deferred-S4 → S5+S3 → S2 → S1-hub). **Secondary (feature-dev — propose only when stable-monolith work is genuinely blocked / user requests):** (3) `T5-06` SDK NuGet (deferred) → (4) polish Workflow/Forms/BP → (5) `P6-DEFER-1` Forms→Workflow remediation (blocked product input) → (6) `WF-MULTI-INSTANCE-ASSIGNMENT` (deferred, schema).
- **Prior direction (2026-06-10 Feature-Dev pivot — now secondary):** CI / test-infra PARKED; feature-dev + polish over stabilization. Deferred decisions remain in force (T5-01/06 DEFERRED, CI PARKED).
- **Parked (re-activate on release-gate / stabilization sprint):** `CI-DISABLED-SINCE-APRIL`, `T3-0x`, `R4-EXEC`, `T4-03`, `T5-07`.
- **Authoritative direction:** `backlogs/backlog.md` 🎯 Current Focus · `docs/microservices-transformation-roadmap.md` (transformation SSOT) · `plans/260610-1520-feature-dev-phase-direction/plan.md` (feature-dev track).

---

## Microservices Transformation

The platform is undergoing a strangler-fig transformation from modular monolith to **5 coarse services** (S1
Identity · S2 Audit/Alert/Notify · S3 Process/Forms · S4 Docs/Sign/AI · S5 Edge/Config). Foundation (Sóng-0) is
complete; no runtime split has started. **Master plan (SSOT):**
[`docs/microservices-transformation-roadmap.md`](./microservices-transformation-roadmap.md) — target map, 8 locked
decisions, wave sequencing, capability gaps, current status. Governing decision:
[ADR-012](./adr/012-activate-microservices-strangler-extraction.md).

---

## Recently Shipped (July 2026)

### Audit Read-Path Isolation — EF + Dapper, Monolith-Side Complete (2026-07-14, Persistence/Security)
- **Scope:** Isolates the `[audit]` schema read path on the shared monolith DB — the concrete read-isolation increment opened up by the 2026-07-13 Sóng-1/S4 deferral (schema-GRANT already proves the isolation property without extraction).
- **`ISO-EF-FIRST-AUDIT-BOUNDARY` (commits `4b74114e`/`a5e890ad`):** the 14 non-audit module EF DbContext registrations resolve their connection string via `NonAuditConnectionString.Get` (least-priv `NonAudit` login denied `[audit]`, opt-in fallback to `Default`).
- **`ISO-DAPPER-KEYED-DI-INCREMENT` (commit `f0ad95e3` + Phase-3 same-day, commit pending):** the default (unkeyed) `IReadDbConnection` (Dapper) now also resolves `NonAudit`; 12 audit-capable consumers use a keyed `"audit"` reader on `Default` via `[FromKeyedServices(ReadConnectionKeys.Audit)]`; 3 shared-infra EF contexts (Alerting/ApiKey/Webhook) folded onto `NonAudit`.
- **Proof:** `AuditBoundaryEfIsolationTests` + `AuditBoundaryDapperIsolationTests` (5/5, both SESSION_CONTEXT bypass openers included) + `AuditKeyedReaderAllowlistTests` (closed 12-consumer allowlist) + `AuditBoundaryEfWiringGuardTests` — all non-`db_owner`, non-vacuous (SqlException 229 DENY). Architecture.Tests 174/174, full regression 652 green, build 0W/0E.
- **Status:** **code-complete; production activation is an ops/DBA task** — run `scripts/260714-nonaudit-login-provisioning.sql` (GRANT DML+DDL on the 19 non-audit schemas, DENY `[audit]`) + publish the Vault KV key `ConnectionStrings__NonAudit`. ADR-012 records this as enforced-only-when-provisioned (opt-in), not a blanket "complete" — standing exceptions (UoW-active reads, 2 identity SecurityHealth keyed reads, admin/`db_owner`/backup/seeders) remain by design.
- **Follow-ups:** the `IAuditModuleClient` refactor of the 2 identity SecurityHealth reads is now **✅ DONE** (`ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR`, 2026-07-14) — monolith read-isolation tightened further: identity no longer holds an `[audit]`-capable DB credential (both handlers now go through `IAuditEvidenceClient`). The prod fail-fast / degraded-isolation health-check signal when `NonAudit` is unset in Production remains deferred (tracked in `backlogs/backlog.md`).

### Anonymous Public-Forms RLS Carve-Out (2026-07-09, Security Hardening)
- **Scope:** Asymmetric RLS policy for anonymous public-forms read access under least-privilege DB login.
- **Deliverables:** NEW `FormTemplate.IsPublic` flag + asymmetric FILTER/BLOCK RLS functions + bypass-read paths + session-context gating.
- **Impact:** Unblocks least-privilege login transition; NEW templates default IsPublic=0 (existing Active backfilled). Prerequisite for non-sa production login.
- **Test coverage:** 6 new integration tests under rls_test_app non-db_owner; 1,791 tests PASS.
- **Verify:** Build 0W/0E, code-review APPROVE_WITH_NITS.
- **Follow-ups:** admin-toggle FE/BE (make templates public), global-uniqueness product-Q (by-Code determinism).

### Sóng-0 Microservices Foundation Complete: All 3 Phases Done (2026-07-08)
- **Phase-1 (shipped 2026-07-07):** Outbox at-least-once dispatch atomicity (5 slices) + tenant-scoped event consumer + PII-guard.
- **Phase-2 (shipped 2026-07-08):** Write-path consistency inventory + Table-mode atomicity harness + 3 production defect fixes (Table-mode forms restoration).
- **Phase-3 (shipped 2026-07-08):** PermCodes vocabulary-ownership split into 21 per-module partial files + 3 architecture-test guards (drift, ownership, contract).
- **Status:** Sóng-0 Foundation COMPLETE. Plan `plans/260706-1237-song0-foundation-outbox-atomicity-authz-decouple/` CLOSED. Ready for authorization microservices extraction.
- **Verify:** Architecture suite 158/158, Forms.Application 320/320, Identity.Application 426/426, build 0W/0E, code-review APPROVE.

### Signing Provider-Allowlist Guard: Fail-Closed Early Authorization + Audit + Metrics
- **Date:** 2026-07-04 (embedded in same day as EF sentinel audit + dead-code cleanup).
- **Scope:** Security hardening (defense-in-depth). Fail-closed early provider-allowlist guard in `SignatureOrchestrator.RequestSignatureAsync` (chokepoint serving HTTP + Forms paths).
- **Deliverables:** NEW `SigningProviderAllowlistGuard` (Application/Signing) + `ProviderEnrollmentKindMap` (Infrastructure/Services/Signing) + `SigningMetrics` (Application/Telemetry, OTel counter). Guard denies 403 ForbiddenError when signer lacks Approved+Active enrollment for requested provider's `EnrollmentKind`. Every deny logged to audit + increments Prometheus `signing_provider_allowlist_denied_total{provider,tenant,reason}` counter.
- **Architecture:** ProviderKey→EnrollmentKind mapping lives in Infrastructure per D6 compliance (vendor key literals isolated from Domain/Application). Arch drift guard (`ProviderEnrollmentKindMapDriftArchTests`) asserts all registered providers are mapped — fail-closed on gaps.
- **Data audit:** CLEAN (0 signers newly-denied; both live kinds USB/Mysign pre-mapped).
- **VNPT SmartCA Revival:** Deprecation REVERSED (2026-07-04). Allowlist guard enables safe adapter re-enablement without 404 risk. To revive: add `EnrollmentKind.RemoteVnptSmartCa = 3`, add key→kind mapping, re-register provider, remove deprecation validator, enroll pilot certs (Approved). Adapter/config NOT deleted (kept for legacy tenant safety).
- **Test coverage:** 9 unit + 1 arch + 4 integration tests (all GREEN). Build 0W/0E.
- **Verify:** BE Identity.Application 426/426, Arch 150/150, Identity.Integration 169/169, full suite green, pre-merge data audit clean.
- **Status:** committed + PUSHED origin/main.

### Signature Profile Image Management: File Upload & Canvas Draw
- **Date:** 2026-07-01.
- **Scope:** FE dual-path image-upload modal (file + canvas) + DTO extensions.
- **FE deliverables:** NEW `web/src/features/profile/signatures/signature-image-upload-modal.tsx` (antd Modal, 2 slots FullSignature/InitialSignature, toggle upload/draw). `SignatureWidget` promoted from `web/src/features/forms/builder/` → `web/src/shared/components/` (git rename, 2 importers repointed).
- **BE DTO additions (Application-only, no migration):** `SignatureProfileDto` gained `hasFullImage` + `hasInitialImage` (bool, per-slot presence). Exposed via `SignatureOverviewQuery` + `ListMySignatureProfilesQuery` for FE badges.
- **Impact:** Unblocks end-to-end self-service visible signing (image mandatory prerequisite for `PadesTransactionInitiator`).
- **Test coverage:** FE scoped vitest 39/39 (6 files, incl. new modal 4 + BE query DTO test).
- **Verify:** BE Identity.Application 415/415, Arch 149/149, tsc/eslint clean, code-reviewer DONE.
- **Status:** committed local main, not pushed.

---

## Recently Shipped (June 2026)

### Signature Multi-Provider Enroll + Admin Approval + Guard Tightening
- **Plan:** `plans/260623-1950-user-signature-profiles-remote-visible-stamp/` (7→8 phase after red-team).
- **Phase 1 (Domain):** Commit `3c4f3ac8` — multi-provider cert binding, admin-approval lifecycle, visibility control.
- **Phase 2 (Self-Service Enroll):** 3 slices (commits `21cc6172`/`5c37b6c7`/`7292f703`) — profile CRUD, enrollment-nonce, remote+USB dual dispatch.
- **Phase 3 (Admin Approval + Guard Tightening):** 2 slices (commits `d1d26283`/`d956a989`) — approve/reject/list endpoints, binding guard `IsActive && Approved` + completion re-check.
- **Window cert-Pending-ký-được CLOSED** (bind guard + completion check prevent late revocation).
- **Test coverage:** Arch 149/149, signing 10/10, auth/policy 7/7.
- **Code reviews:** APPROVE ×5 (no blockers).
- **Not yet pushed** (commits 13 ahead origin/main).

### Module-RLS Guard Exhaustive Discovery + TenantSignatureConfig Allowlist
- **Commit:** `10e1f530` (test), `c72b1f04` (docs).
- **Discovery refactor:** DbSet-walk → **instantiate-per-module DbContext + Model.GetEntityTypes()** (test-only stub `ITenantContext`; 0 DB opens).
- **Coverage:** Caught `TenantSignatureConfig` (config-only, no DbSet).
- **RLS decision:** `TenantSignatureConfig` → **allowlist predicate-only** (KHÔNG RLS); reason: `SigningHealthCheckService` cross-tenant startup reader (ADR-009 class).
- **Bonus fix:** vá false-positive `IsRlsCovered` heuristic (index name matching).
- **Arch suite:** 149/149 PASS.

### 15-Module Deep Audit + Quality Fixes
- **Plan:** `plans/260623-1120-gap-audit-cleanup-v2132/` (5 parallel agents).
- **Architecture:** 0 module-isolation violations (guards 145/145 clean + DIP/service-locator/captive-dependency sweeps sạch).
- **Security fixes shipped:** ChatHub IDOR join-gate, SendMessage membership check, BulkImport role-check.
- **Code-quality fixes:** Extract `FormSignatureIngest` static helper, gỡ redundant `AddMemoryCache`, hard-fail `DocumentIngestionService` embed (all-or-nothing).
- **Verified non-issues:** `DocumentSigningWorkflow` IgnoreQueryFilters (has explicit TenantId), Files Minio disposal (general catch).
- **Deferred:** 3 L-tier findings (compliance, observability, design) tied to broader phases (least-priv RLS, alerting, multi-host).
- **Verify:** build 0E/0W, Forms.App 317/317, Ai.Infra 17/17, arch 145/145, smoke 18/18.

### AuthServer/Api Arch Re-audits + Presentation-Layer Remediation
- **AuthServer (2026-05-23):** Guard G1 relocated to `AuthServerHost`; services `Identity.Application` + interface DI; `MigrateSetSubsetGuardTests` 4/4 arch-enforced.
- **Api (2026-05-23):** NCalcSync CVE bump (5.12.0→6.1.1, safe), stale module imports cleaned, full arch re-verify 145/145 PASS.
- **Presentation remediation (plan `260622-0935`, 2026-06-22):** 2 signing controllers rewired (DbContext injection → audited commands); host-bound `ISigningDocFlowBlobStore` port (ADR-011); M2M callback audit via `ITenantAgnosticCommand`. Arch 145/145, Identity.Integration 300/300.
- **E2E live smoke:** USB DocFlow + Mysign OTP USER-CONFIRMED green.
- **Commits:** 8 pushed origin+bitbucket (p120→p125 equivalent).

### Collaboration Chat: IDOR Fixes + Read-Receipts + FK/RLS Closure
- **Features shipped:**
  - Delete message IDOR (sender-only gate), mark-as-read membership check, addmember/archive owner-only gate.
  - Per-message read receipts ("seen by"), idempotent receipt persistence, realtime SignalR broadcast.
  - MessageReadStates FK + RLS closure (allowlist predicate-only; module-local table).
- **Verification:** Collab.App 38/38, Collab integration 20/20 (incl. cross-tenant isolation + race tests).
- **Code-review:** APPROVE / DONE_WITH_CONCERNS (MED findings fixed in-flight).
- **Commits:** 4 pushed origin+bitbucket.

---

## In Progress / Next Up

### Microservices Foundation (Sóng-0)
- **Phase-1 (Outbox At-Least-Once) — COMPLETE (2026-07-07):** All slices + 1B-3 decision resolved (1A version-token, 1B-1/1B-2 forms+audit+UoW-hardening, 1B-3 WONTFIX-recorded, 1C idempotency-ledger, 1D consumer-scope-tenant-pin, 1E comprehensive-test suite). Reusable `IOutboxWriter` + tenant-scoping primitives + comprehensive white-box test coverage + PII guard expansion + doc-corrected for clarity. All PUSHED origin/main (`abd4e079`, 2026-07-07). Plan `plans/260706-1237-song0-foundation-outbox-atomicity-authz-decouple/`.
- **P6 (NetworkPolicy Security Hardening) — RESOLVED (2026-07-09):** Kubernetes Helm NetworkPolicy egress exfil hole narrowed (removed to:-less 443/4317/4318 → explicit per-CIDR allowlist for external 443 + gated OTEL behind config flag). New architecture guard prevents future regression. No new egress opened; default render provably stricter than before.
- **Phase-2 (Cross-Module Atomicity) + Phase-3 (Authz Vocabulary Split) — COMPLETE (2026-07-08):** write-path consistency inventory + `PermCodes` split into 21 per-module partial-classes + 3 architecture guards. Sóng-0 foundation fully closed. **Superseded 2026-07-13/14:** Sóng-1 (S4 extraction) deferred (C3 spike disproved its compliance driver); the monolith-side audit read-isolation increment it opened (EF + Dapper) is now code-complete/ops-activation-pending. **Next actionable = ops-activate `NonAudit`** (run `scripts/260714-nonaudit-login-provisioning.sql` + Vault key) and/or resolve the FU-A auditor question — see [`docs/microservices-transformation-roadmap.md`](./microservices-transformation-roadmap.md) §5 wave sequencing.

### Ranked Candidates (Stable Modular Monolith for Launch Phase, 2026-07-16)
1. **🥇 Stable Modular Monolith for Launch (TOP PRIORITY, user decision 2026-07-16).** Launch feature-set is **user-TBD (open item)**; Definition-of-Stable = functional-complete to launch scope (TBD) + operational robustness (clean boot/migration/seed) + quality (green CI-enforced suite) + data/security (RLS + audit-chain + Vault + backup + pending audit read-isolation OPS activation) + observability wired + prod deploy + soak. See `backlogs/backlog.md` 🎯 Current Focus for detail.
2. **Microservices transformation — DEMOTED to a gated/deferred track (user 2026-07-16, reverses the 2026-07-10 "always propose first" directive; NOT cancelled).** Sequenced after the stable-monolith launch, gated on FU-A auditor answer + production load-evidence. Sóng-1 = **S4** (Docs/Sign/AI) extraction **DEFERRED 2026-07-13** (C3 spike disproved the compliance driver); build-now CODE track already EXHAUSTED (2026-07-16). Next actionable once gates clear = ops-activate the `NonAudit` login (`ISO-DAPPER-KEYED-DI-INCREMENT` is code-complete; run `scripts/260714-nonaudit-login-provisioning.sql` + publish the Vault key) and/or put the FU-A auditor question to GOV. SSOT → [`docs/microservices-transformation-roadmap.md`](./microservices-transformation-roadmap.md).

**Secondary (feature-dev — propose only when stable-monolith work is genuinely blocked or the user requests):**
3. **`T5-01` KPI Threshold & Alerting** — ⛔ DEFERRED (user 2026-06-23); do NOT auto-propose (~8-10d after the 2026-06-20 re-scope).
4. **`T5-06` SDK NuGet** — ⛔ DEFERRED (user 2026-06-23); needs SDK-contract scoping first.
5. **Polish Workflow/Forms/BP** — ✅ DONE (polish-audit ~18 rough-edges FULLY CLOSED 2026-06-15).
6. **`P6-DEFER-1` Forms→Workflow remediation** — blocked on product input (retry semantics, orphan-remediation UX).
7. **`WF-MULTI-INSTANCE-ASSIGNMENT`** — deferred (schema migration needed; low priority).

### Active Plans
- **Signature Profile Visible Stamp** (plan `260623-1950`, Phase 1–8) — user self-service profile enrollment + raster-stamp compose + explicit-sign gate. Hard constraints: PAdES ByteRange-at-compose (not post-sign), PDF `/Annots`/`/AcroForm` flatten-required (multi-signer deferred), QuestPDF toolchain (not SkiaSharp glibc). **Phase 1–6 COMPLETE (shipped 2026-05-11 through 2026-06-30):** P1 domain (multi-provider cert binding); P2–3 (self-service enroll + admin approval + binding guard); P4 (image upload + stamp composer); P5 Slice 2 (visible stamp wire); **P6 ALL SLICES DONE:** A = FE profile tab (signature management UI, enrollment list); B = self-service set-CCCD endpoint (PUT /api/v1/me/signing-national-id) + multi-provider enroll UI modal (Remote OTP + USB token ceremony); C = FE admin dashboard (enrollment approval review, paginated queue, approve/reject, tenant-scoped); D = production PAdES signing modal with draggable placement picker + multi-page PDF support. Phase 7–8 deferred (multi-signer, flatten, orphan cleanup).
- **Forms→Workflow Link Retry Job** (`P6-DEFER-1`) — handle orphan FormSubmission→WorkflowInstance link failures. 7 phases (event emit, attempt persistence, retry-job, admin remediation UI). Phases 02–03 shipped (event emitter + attempt entity); Phase 04–07 blocked on product decision (retry semantics, rate-limit, UX).

---

## Completed / Historical

### v2.137 Delivery (2026-05-26 Complete)
**Last major version:** v2.137-QD742-PHASE05B-SIGNING-CORE shipped 2026-05-11. Signature 3-tier VGCA + Viettel Mysign (sync/async-polling/async-callback). 10 commits, 61 tests, compliance dossier, integration guide.

| Milestone | Status | Date | Key Details |
|-----------|--------|------|-------------|
| **QĐ 742 Phase-05b Digital Signature** | Complete | 2026-05-11 | 3-tier VGCA (raw-hash, XMLDSig, PDF docflow) + Viettel Mysign 3-mode (sync, polling, callback). 61 tests. Code-review 8.4/10. |
| **QĐ 742 Phase-06 Vault Key Rotation** | Complete | 2026-05-10 | Multi-version key management. IKeyResolver + VaultKeyResolver. Backwards-compat via LegacyCipherUpgrader. |
| **QĐ 742 Phase-07 Audit Dossier** | Complete | 2026-05-10 | 41-row master checklist, compliance test backfill, 39 evidence-artifact slots. 97.6% coverage (39/41 ReqIDs). |
| **PHR-V2 Phase-1-5 Workflow Resolver** | Complete | 2026-05-21 | 11 slices (5/5 pipeline path + resolver + pool-safety + task-creation + scrub + merge-gate proof). 491/491 app tests. PR #106 ready (35 commits, 100% code-complete). |
| **RT2-15 Phases 01–05 Assignment Modes** | Complete | 2026-05-26 | 6 modes live (SpecificUser, PreviousHandler, ByPermission, ByRole, ByOrgUnit, ByGroup). All resolver modes + read-side + validator gated. p110-p117. |
| **P6-DEFER-1 Phases 02–03 Forms→WF Link** | Complete | 2026-05-22 | Event emitter + attempt-tracking entity + migration. Terminal state skip-reprocess. Phases 04–07 blocked (product input). |
| **Phase-06/6C Schema Apply + Compression** | Complete | 2026-05-05 | 19 module contexts initialized with migrations. datetime2(3) config sweep. Page compression on 581 partitions (infra/scripts/apply-page-compression.sql). SchemaUniversalsTests 4/4 GREEN. |

### Previous Versions (v2.128–v2.136)
**Timeline (condensed):**
- **v2.136 (2026-05-05):** Phase-04e handler-layer drift cleared (9 tests un-skipped). Integration 94/109 tests PASS.
- **v2.135 (2026-05-05):** Phase-06/6C InitialCreateFoundationV1 applied. 12 datetime2(3) fixes.
- **v2.134 (2026-05-04):** Phase-04a-d entity tier refactor (108 entities T1/T2/T3 retier). Handler/mapper/controller propagation. Architecture 26/26 PASS.
- **v2.133p20–v2.133p24 (2026-05-01→05-02):** QĐ 742 Phase-02 Auth Lifecycle (lockout escalation, MFA requirement, first-login password change). Phase-01 Slice D2 (audit immutability + retention CRUD). RBAC Permission refactor (JWT size reduce, BE `/me/permissions` endpoint, FE hook). Tenant management CRUD. Messaging/Integration Outbox Dispatcher MVP. 6 cumulative production fixes.
- **v2.132–v2.128 (2026-04-18–04-25):** SQL Docker Desktop switch, Testcontainers integration tests, 403 stub closure, identity menu soft-hide, modular infra decoupling (IOrgUnitModuleClient, IAuditEvidenceClient extraction). 10 modules per-module contract csproj created.

### Dynamic Workflow Phases 1–3.5 (Complete — 2026-03-27)
**Status:** 4 commits, 82 files, +7,128 LOC, 260 unit tests PASS.

| Phase | Details |
|-------|---------|
| **Phase 1** | Definition CRUD + Versioning (DefinitionKey, Version, IsLatest, clone endpoint). |
| **Phase 2** | Condition Evaluation (11 operators: equals, notEquals, greaterThan, lessThan, gte, lte, in, notIn, contains, isNull, isNotNull). |
| **Phase 3** | Notification Integration (WorkflowNotificationConfig, WorkflowTransitionedNotificationHandler, CRUD endpoints). |
| **Phase 3.5** | Tenant-Workflow Assignment Config (4-level specificity, ICacheService resolution, auto-resolve on instance creation). |

### Foundational Modules (Complete — 2026-03-18)
**15 modules shipped:** F-01 SharedKernel/Infrastructure, F-02 Identity (OIDC, RBAC, ABAC, MFA, Delegation), F-03 Notifications (11 templates), F-04 Audit (HMAC-chained, RTBF, NĐ53), F-05 MasterData (Province/District/Ward), F-06 AI (LLM, Qdrant, data-sovereign), F-07 Forms (DDL, dual-mode, complex fields), F-08 Workflow (state machine, branching), F-09 Cases (DDD, workflow, export PDF/QR), F-10 Files (MinIO, ClamAV, signature, encryption), F-11 Integration (YARP, webhooks, API keys, M2M), F-12 Organization (hierarchy, departments), F-13 SystemParams (config, caching), F-14 Reporting (KPI, Excel/PDF, query catalog), FE-15 Frontend (Vite, React, TS, Auth, SDK).

---

## Documentation & Governance

| Document | Purpose | Status |
|----------|---------|--------|
| `docs/code-standards.md` | Coding patterns, INVARIANTS, RLS, async, testing | Updated 2026-06-25 |
| `docs/system-architecture.md` | 15-module topology, layer contracts, DB schema | Updated 2026-06-22 |
| `docs/dependency-map.md` | Module relationships, entity maps, FE↔BE contracts (single source of truth) | Updated 2026-06-23 |
| `docs/project-changelog.md` | Per-version delivery history (v2.128–v2.137p117) | Live |
| `backlogs/backlog.md` | Active feature pipeline, tier prioritization, deferred items | Live |
| `backlogs/completed.md` | Closed/shipped sections, historical narrative (trimmed 2026-06-20+) | Archive |

---

## Key Architectural Decisions (ADRs)

| ADR | Title | Status | Impact |
|-----|-------|--------|--------|
| ADR-004 | Write-Path Audit via ISender (Commands) | Live | All mutations audited; no direct DbContext in controllers. |
| ADR-006 | SQL Server Docker Desktop + Testcontainers | Live | Local dev simplified; integration tests run fast. |
| ADR-009 | RLS + Allowlist Predicate Pattern | Live | 36 security policies; cross-tenant gaps closed via explicit context. |
| ADR-010 | AuthServer Startup Migration Subset | Live | AuthServer MUST migrate only {Identity, Messaging} (subset-guarded). |
| ADR-011 | Presentation-Layer Controller Thin + Audited | Live | Controllers route to MediatR; DbContext injection banned. |

---

## Performance & Scale Targets

| Metric | Target | Status | Notes |
|--------|--------|--------|-------|
| **Test suite** | 0E/0W, <2min full run | 145 arch tests PASS | Testcontainers: ~36s per integration suite. |
| **Build** | <1min incremental | ✅ | C# incremental build fast. |
| **FE bundle** | <150 KB gzipped | ✅ | Vite+React optimized. |
| **API latency** | p95 <200ms | TBD | Load test on k6; Prometheus alerts wired. |
| **RLS enforcement** | 0 row leaks | ✅ | Guard-tested on 36 policy objects. |
| **Audit HMAC chain** | Append-only, immutable | ✅ | Trigger + SessionContext enforced. |

---

## Known Limitations & Deferred

| Item | Scope | Reason | Owner |
|------|-------|--------|--------|
| **Untrusted PDF crypto-verify chain (Phases 4–6)** | Signing v1.1 | Parsing untrusted uploaded PDFs requires Phase-4 sandbox (in-process + isolated worker) → Phase-5 remote PAdES assembly → Phase-6 signature verification (~18–25d). Launch uses form-generated PDFs only (USB client-side stamping). Re-escalate if: flag re-enabled, liên-thông enters scope, or Phase-5 remote PAdES ships. Chain ordering: Phase-4 sandbox must precede Phase-5/6. M2 compliance gap: PAdES-B (no timestamp) signatures unverifiable after signer cert expiry (24mo+ retention); Phase 5 requires TSA timestamp (PAdES-T). | Product |
| **Multi-signer visible PDF stamp** | Signing Phase 4 | Spike: PdfIncrementalSignatureWriter fails on `/Annots`/`/AcroForm`. Flatten required; deferred. | Product |
| **Signature per-cert completion precision** | Signing Phase 5 | C5 tie-break: serial/issuer not recoverable from cert thumbprint. Tier-level check sufficient (product decision). | Product |
| **PendingTransaction cleanup on multi-pod** | Signature Phase 5 | RowVersion race + orphan blob on two-pod HA. Leader-elect or distributed lock needed. Single-host only. | Arch |
| **Workflows multi-instance assignment** | Workflow Phase TBD | Schema migration for EntityType multi-support. Low priority. | Product |
| **Forms→WF retry job + orphan remediation** | P6-DEFER-1 Phase 4–7 | Blocked on product decision (retry semantics, rate-limit, UX). Event + attempt-entity shipped. | Product |
| **Async-all-the-way on ITenantOrgContext** | Code-quality | Sync contract (domain-predicate use) across modules. Not worth breaking a public interface. | Deferred |

---

## Getting Started

1. **Local dev:** `scripts/1_run-local-dev.ps1` (Docker SQL, Redis, RabbitMQ).
2. **Run migrations:** `dotnet ef database update` (each DbContext).
3. **Integration tests:** `TEST_USE_LOCAL_SQL=true` (4 persistent test DBs).
4. **FE:** `cd web && npm install && npm run dev` (Vite, `:5173`).
5. **Docs:** `docs/` — start with `code-standards.md` + `system-architecture.md`.

---

## Contact & Governance

- **Repository:** `https://github.com/thiennd212/AqtCoreFW` (SQL Server main).
- **Oracle work:** `https://github.com/thiennd212/AqtCoreFW_Oracle` (v2.156 SHIPPED, archived).
- **Active team:** Aequitas core team (aidev@aequitas.vn).
- **Release cadence:** Feature-driven; red-team validation on complex slices.
