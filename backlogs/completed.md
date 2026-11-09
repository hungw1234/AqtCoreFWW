# Completed Work Archive — AqtCoreFW

Items moved here from `backlog.md` upon completion.

## Closed 2026-07-28

- **OIDC-SPA-CLIENT-REDIRECT-URIS-HARDCODED** `[blocks-v1.0]` — ✅ RESOLVED 2026-07-28 (plan `260728-1331-oidc-spa-redirect-uris-from-config`, 3 phases; code review 3 rounds BLOCKED → DONE_WITH_CONCERNS → DONE). SPA client URIs now bind from `Auth:SpaClient:{RedirectUris,PostLogoutRedirectUris}` via `SpaClientRedirectUris.Resolve`; Development falls back to the byte-identical previous localhost list (dev loop, `.http`, Playwright e2e, Swagger all keep working with zero config), non-Development throws at startup naming the key (mirrors `Program.LoadRequiredCertificate`). Blank entries are filtered *before* the configured-vs-absent decision, so `""` fails like absent. Resolve+validate moved **before** `DeleteAsync` — the client is delete-and-recreated each boot, so a config error previously would have left a deployment with no SPA client and no way to log in. `values-local.yaml` supplies the `*.localtest.me` set; base `values.yaml` deliberately supplies none so the first prod deploy fails loudly at boot. `scripts/3_setup-tunnel-urls.py` stopped regex-rewriting the C# source and writes the same config keys instead. Verify: AuthServer build `-warnaserror` 0W/0E · unit 40/40 · smoke 3/3 on a clean bin · arch 203/203 · full solution 0E.
  **Two review findings that changed the design, both worth remembering:** (1) a mid-slice reduction of the post-logout list 8→4 rested on a *refuted* premise — OpenIddict does NOT normalize these URIs. `new Uri("http://h:p")` equals `new Uri("http://h:p/")`, so the descriptor's `ISet<Uri>` keeps the **first-inserted** spelling, OpenIddict persists its `OriginalString`, and matching is `StringComparison.Ordinal` (the relaxed URI-parsing branch is native-clients-only; this client is Public). Since the SPA sends `window.location.origin` (slashless), the slash-only list would have returned **400 on every logout** in dev/kind/tunnel while login kept working. Fixed slashless-first in all four write paths, pinned by a mutation-verified test. (2) The integration test that should have caught it was a **false green**: it was reading an `Auth:SpaClient` section the tunnel script had left in the committed dev config, so it exercised the configured branch while its assertions read as fallback coverage. That section is now out of the committed config and `--restore` **deletes** the key instead of rewriting it. Verified both directions — with the section present the slash-first mutation stayed green; with it absent the same mutation goes red.
  **Not verified, tracked separately:** no real browser login has been shot against kind (`OIDC-BROWSER-LOGIN-ROUNDTRIP-VERIFY-ON-KIND`); prod origins need an overlay (`AUTHSERVER-PROD-VALUES-SPA-REDIRECT-URIS`). Logout in particular has no integration coverage — the ordinal-match path is exactly where the near-miss lived.

## Backlog-optimize archival 2026-07-28 (26 closed rows pulled out of the active backlog)

> Rows below were already CLOSED but still sat in `backlogs/backlog.md`, inflating the open-work count. Moved verbatim, grouped by close date. No content edited. Rows whose record already existed here were deleted from the backlog rather than duplicated.

### Closed 2026-06-23

- **COLLAB-IDOR-DELETE-MESSAGE** — ✅ RESOLVED 2026-06-23 (`/cook`, code-reviewer APPROVE) — added a sender-only ownership gate to `DeleteMessageCommandHandler` (`message.SenderId != caller → ForbiddenError`); `DeleteMessageCommand` now carries the caller `UserId` (injected server-side from JWT by `MessagesController`). **Threat-model corrected:** the original "cross-tenant delete" framing was an over-claim — `GetByPublicIdAsync` uses `TrackingQuery` which applies the global tenant filter, so cross-tenant was already blocked; the real (now-closed) hole was a same-tenant **horizontal** IDOR (any user deleting another user's message). 3 new handler tests (author→ok, non-author→403, not-found→fail). Moderation/admin-delete intentionally deferred (no such capability exists today). HTTP contract unchanged.
- **COLLAB-IDOR-MARKASREAD-MEMBERSHIP** — ✅ RESOLVED 2026-06-23 (`/cook`, code-reviewer APPROVE_WITH_NITS) — `MarkAsReadCommandHandler` now returns `ForbiddenError` when the caller is not a conversation member (was a silent `member?.UpdateLastRead` no-op that still returned success). Command already carried `UserId`; added `[ProducesResponseType(403)]`; 1 new non-member test. Consistent with the send/delete membership gates.
- **COLLAB-MUTATE-HANDLERS-NO-PERSIST** — ✅ RESOLVED 2026-06-23 (`/cook`, 2-agent investigation + code-reviewer APPROVE) — confirm-then-fix. (1) Scope-sweep across all 15 modules → Collaboration-ONLY (14 others persist via GenericRepository or explicit UpdateAsync+SaveChanges). (2) Testcontainers integration test `CollaborationMutationPersistenceTests` proved the gap RED (delete soft-delete + mark-read pointer never reached DB). Remediation (user chose per-handler repo-method over systemic UnitOfWork behavior): added `SaveChangesAsync` to `IConversationRepository`/`IMessageRepository` (+impls); entities load via `TrackingQuery == Set<T>()` so a flush suffices. Flushed in `DeleteMessage`/`ArchiveConversation`/`MarkAsRead`. `AddMember` had a SECOND bug — `_ = ConversationMember.Create(...)` orphan never attached → now `conversation.AddMember(...)` + flush. `SendMessage` left unchanged (its `UpdateLastMessageAt` already persists atomically via the shared-scope `AddAsync` SaveChanges; a separate save would split the transaction) — documented. 2 integration tests RED→GREEN; Collab.App unit 27/27; Collab integration 10/10.
- **COLLAB-AUTHZ-ADDMEMBER-ARCHIVE** — ✅ RESOLVED 2026-06-23 (`/cook`, code-reviewer, filed+fixed same session) — MED-1 from the persist-fix review: `AddMember`/`Archive` had `[Authorize]` (authn) but NO caller-authz — any same-tenant user could archive any conversation by Guid or inject members (incl. `MemberRole.Owner` → privilege escalation). The persist fix un-masked AddMember (was a silent no-op). Fix: Owner-only gate in both handlers (`IsArchived` is an aggregate-wide flag; AddMember-with-Role is a privilege vector), caller `RequestedBy` injected server-side from JWT (never body-bound), authz before the duplicate-conflict check. Added `[ProducesResponseType(403)]`. 9 new handler unit tests (owner-ok / non-owner-403 / non-member-403 / not-found / conflict).
- **COLLAB-MESSAGEREADSTATE-DEAD-CODE** — ✅ RESOLVED 2026-06-23 (`/cook`, user chose WIRE over drop; 2 code-reviewer passes APPROVE / DONE_WITH_CONCERNS) — `MessageReadState` is now a live per-message read-receipt ("seen by") feature, full BE+FE+realtime. BE: `MarkAsReadCommandHandler` idempotently persists a receipt (`IMessageReadStateRepository.AddIfNotExistsAsync`, unique-index + DbUpdateException race backstop) and raises `MessageReadEvent` only on first read; `GET messages/{id}/read-receipts` (Dapper, tenant+membership gated → 200/403/404); realtime `MessageReadSignalRHandler` broadcasts `ReceiveReadReceipt` (log-and-swallow). FE: `useMessageReadReceipts` (queries only the newest message → 1 query) renders "Đã xem bởi …" excluding self; SignalR invalidation; i18n. Verify: arch 145/145, Collab.App 38/38, Collab integration 19/19 (incl. cross-tenant-404 + concurrent-race tests added per review MED-1/MED-2), FE typecheck/eslint/vitest green.
- **COLLAB-MESSAGEREADSTATE-FK-RLS** — ✅ RESOLVED 2026-06-23 (`/cook`, code-reviewer) — (a) added FK `collab.MessageReadStates(MessageId) → collab.Messages(Id)` with `OnDelete(NoAction)` (user chose NoAction over the row's cascade suggestion — matches the module convention: `Thread→Message`/`Message→Conversation` both NoAction; messages are only soft-deleted, never hard-deleted, so cascade would be dead code). The FK is a referential-integrity guard (reject receipts for non-existent messages). Empirical: 0 hard-delete path in the module (`grep .Remove/ExecuteDelete/HardDelete` = 0) → no orphan rows existed to fail the `WITH CHECK` constraint. Migration `20260623084434_AddMessageReadStateMessageForeignKey` (FK-only, no drift; FK column already covered by the composite unique index). (b) RLS/tenant = non-issue, **document-only**: the row's "ADR-009 allowlist" was a category-mismatch — that guard scopes *shared-infra* DbContexts, not module contexts; the table is intentionally tenant-less (tenant-safety rides on the tenant-scoped message lookup, test-proven by `GetReadReceipts_CrossTenant_Returns404`). Added a "do not restore parity by adding RLS" comment in the entity config. New teeth test `InsertReceipt_ForNonExistentMessage_ViolatesForeignKey` (orphan insert → `DbUpdateException`). Verify: build 0E, Collab integration 20/20, arch 145/145, no public-contract change.
- **COLLAB-CHATHUB-ISMEMBER-INTEGRATION-TEST** — ✅ RESOLVED 2026-06-23 (`/cook`, code-reviewer DONE 0-blocking) — added `ChatHubMembershipTests` (Testcontainers, real `ChatHub` + real repo + real SQL via `WebAppFixture`; SignalR has no in-test transport → synthetic `HubCallerContext` carrying JWT claims + substituted `IGroupManager`). User chose scope "full SignalR hub integration" over repo-level. 4 tests: member→join (asserts `AddToGroupAsync(connId, "conversation:{id}")`), non-member(same-tenant)→`HubException`, cross-tenant(member userId + wrong tenant claim)→`HubException` (isolates the `c.TenantId` predicate, not "any wrong guid"), missing-claims→`HubException` (hub-only short-circuit branch). Teeth-proof: gate bypass (`if(false)`)→ exactly 3 RED deny-tests + 1 PASS member, then reverted. Verify build 0E, 4/4 green (real Docker SQL). **Carry-fwd (N2):** the `!m.IsDeleted`/`!c.IsDeleted` soft-delete predicates in `IsMemberAsync` remain the ONLY uncovered branch — intentionally deferred (no member-removal/conversation-delete path exists to set those flags); add a deny test when a member-removal feature lands (removed member must lose realtime access — real security boundary).
- **MODULE-AUDIT-LOW-FINDINGS** — ✅ RESOLVED 2026-06-23 (`/cook` plan `260623-1727`, code-reviewer DONE 0-concern-above-Low) — empirical re-probe of the 6-finding defer bundle (INVARIANT 260522-A; audit-author labels NOT trusted). Verdicts: **(a) FIXED** — `ClaimsEnrichmentTransformer` injects `IHttpContextAccessor`; `RequestAborted` (`HttpContext?.RequestAborted ?? None`) threaded into the two hot-path EF `.ToListAsync()` (GetScopedRoles/GetActiveDelegations); UserManager calls have no ct overload → left as-is; dedicated cancellation unit-test judged disproportionate for 3-line wiring (build+smoke cover DI). **(b) DOCUMENT-ONLY (no code)** — `JwtOrgContext` sync-over-async is on a cache-backed call + per-scope cache; `ITenantOrgContext` is a SharedKernel **sync** contract (domain-predicate use) so async-all-the-way breaks a cross-module public contract — not worth it; doc-comment already declares it safe. **(c) FIXED** — extracted `FormSignatureIngest` static helper (verify-at-ingest + record-evidence); `SubmitForm`/`SubmitPublicForm` share it (the only 2 deltas — TenantId source + signerUserId auth-vs-anon — are params). Behavior-preserving: Forms.App 317/317 incl. SignerUserId/TenantId-binding + Mode-1/2 + tampered-never-persists; reviewer verified line-by-line. **(d) FALSE-POSITIVE (no code)** — `RulesEngineEvaluator:97` `cache.Set(key,engine,CacheTtl=10min)` HAS absolute-expiration TTL; "no eviction/TTL" claim wrong (no SizeLimit but keyed by ruleSetId, low cardinality). **(e) FIXED** — removed redundant `AddMemoryCache()` from Collaboration (host/shared `InfrastructureRegistration:120` registers it; only caller is Api host; smoke 18/18 confirms PresenceCache DI intact). **(f) PARTIAL** — `DocumentIngestionService` embed-fail changed from silent-skip+`Result.Ok` (partial-index-as-success) to **fail-hard** `Result.Fail(embedResult.Errors)` (user chose all-or-nothing, mirrors Qdrant GOV_AI_003) + teeth test; Ollama-queue capacity-metric/health-check **deferred** (queue already logs+throws GOV_AI_004 — not silent; metric is YAGNI on-prem low-traffic, re-open only if saturation observed). VERIFY: build 0E/0W, Forms.App 317/317, Ai.Infra 17/17, arch 145/145, smoke 18/18, net −79 LOC, no public-contract change.
- **EXTRACT-API-ERROR-CODE-DEDUP** — ✅ RESOLVED 2026-06-23 (`/cook`, code-reviewer DONE 0-concern) — row verified accurate: `extractApiErrorCode` IS a strict superset. Deleted `workflow-taxonomy-helpers.ts` (sole export `extractTaxonomyErrorCode` read only `response.data.errors[0].code`); repointed the 2 caller files (`workflow-{action,state}-taxonomy-admin-page.tsx`, 6 call sites) to `extractApiErrorCode` from `@/core/api`. **Bonus (not just dedup):** the swap closes a latent UX gap — those catch-sites receiving a `NormalizedApiError` (the default axios-interceptor shape, no `.response`) previously showed the generic toast; `extractApiErrorCode`'s `?? e.code` fallback now surfaces the specific `GOV_*` message. Strict improvement, never a regression (all 6 sites are positive `code === 'GOV_X'` equality, else→`common.error`). Stale doc-comment 3rd-consumer `taxonomy-deprecate-dialog` died with the file (it never imported the helper). Verify: typecheck clean, eslint 0, vitest 12/12 (extract-api-error) + 29/29 (5 workflow-taxonomy incl. both admin-page component tests exercising the catch-blocks), no public-contract change.

### Closed 2026-06-24

- **ARCH-MODULE-RLS-GUARD-DISCOVERY-EXHAUSTIVE** — ✅ RESOLVED 2026-06-24 (`/cook`, code-reviewer APPROVE) — discovery của `ModuleDbContextTenantIsolationGuardTests` chuyển từ DbSet-walk sang **instantiate mỗi module DbContext (stub `ITenantContext`/`IKeyResolver` + bogus SqlServer conn — 0 DB mở) → `Model.GetEntityTypes()` + `GetTableName()`** (test-only: thêm `EntityFrameworkCore.SqlServer` ref + xóa `Pluralize()` heuristic/override-map). Bắt được `TenantSignatureConfig` (config-only, no DbSet). Blast-radius scout = **chỉ 1 entity** flip. 🔴 `Model.GetEntityTypes()` ban đầu định instantiate-via-DI bất khả thi (12/15 ctx require `ITenantContext` ctor → NRE) → giải bằng **stub ctx trong test, KHÔNG refactor `ApplyGlobalFilters` prod** (Expression.Constant đọc `.TenantId` per-query, model build với stub OK). Arch suite **149/149**.
- **RLS-TENANTSIGNATURECONFIG** — ✅ RESOLVED 2026-06-24 (`/cook`, đóng cùng ARCH-MODULE-RLS-GUARD-DISCOVERY-EXHAUSTIVE) — quyết **ALLOWLIST predicate-only, KHÔNG RLS** (backlog đoán "RLS hợp" → scout bác). Lý do: `SigningHealthCheckService` (startup IHostedService) đọc `TenantSignatureConfig` **cross-tenant không ambient** (`.Where(c=>c.IsEnabled).CountAsync()`) → RLS least-priv zero-row → silent startup-health fail (ADR-009 class); IdentityDbContext KHÔNG derive ModuleDbContext (0 tenant-filter), readers request-scoped isolate qua explicit `WHERE TenantId`. 🔴 BONUS-FIX: vá false-positive pre-existing trong `IsRlsCovered` (fragment `_{table}_Tenant` khớp nhầm index `UQ_{table}_TenantId` → entity trông "covered" giả) → siết sang `_{table}_TenantFilter`/`_{table}_Tenant]`; teeth-proof gỡ allowlist→flag thật. Migration `20260510170906` giữ nguyên 0-RLS (đúng chủ đích).

### Closed 2026-06-25

- **SIGN-ENROLLMENT-NONCE-EXPIRY-SWEEP** — ✅ RESOLVED 2026-06-25 (T4 from Phase-2 enroll ship `5c37b6c7`) → **least-priv follow-up also RESOLVED 2026-07-02** — hourly `EnrollmentNonceCleanupJob` (IHostedService, PeriodicTimer 60min; `ExecuteDeleteAsync` sweeps `ConsumedAt IS NOT NULL OR ExpiresAt < now`; no grace period—spent nonce never re-auth; NOT registered in Testing; mirrors `PendingTransactionCleanupJob`). 3 integration TC all green (consumed→deleted, expired→deleted, active→retained). Least-priv follow-up (shared debt w/ `SIGN-PENDINGTX-CLEANUP-JOB-LEAST-PRIV`) now resolved via `TenantScopedSweep` per-tenant iteration + `DocFlowTenantSession.PinAsync` SESSION_CONTEXT pinning.

### Closed 2026-07-01

- **SIGNATURE-PROFILE-VISIBLE-STAMP** — ✅ T3 🟡 (plan `260623-1950`, **P1–P8 ALL SHIPPED 2026-07-01 — plan CLOSED**: P1-P6 BE/FE, P8 HA deterministic-key hardening, P7 docs+Alpine-musl close-out; multi-signer separately deferred `MULTI-SIGNER-VISIBLE-STAMP`) — **P1-P5 shipped:** aggregate `UserSignatureProfile` (T2 Identity entity) + `SignerEnrollmentNonce` challenge/TTL; multi-provider self-service enroll (Mysign+USB); admin approval + binding-guard tightening (IsActive && Approved); image-upload pipeline (`IUserSignatureImageStore` port → `UserSignatureImageStoreAdapter` → `IFileSecurityService`/`IFileStorageService` bucket `user-signatures`); stamp composer (`ISignatureStampComposer`, sanitizes text, pass-through image bytes, no raster); **P5 Slice 2 visible stamp wiring:** NEW `SignaturePlacement` parser (Application), `ISignatureStampRasterizer` + `SignatureStampRasterizer` (Infrastructure QuestPDF JPEG rasterizer), `QuestPdfExporter.EnsureInitialized()` (shared), cross-module `IUserSignatureImageStore.GetAsync` port (Identity → host adapter → Files). PAdES `PadesTransactionInitiator` wire: placement → profile lookup (tenant+signer scoped, fail-closed 403) → fetch image → compose VN text → rasterize → appearance (frozen at INITIATE inside ByteRange); no placement/malformed → invisible (backward-compat, USB unchanged). Appearance-preserving flatten DEFERRED (no permissive PDF lib under "no new library" constraint; clean-PDF precondition kept). Single-signer v1 (Multi-signer deferred P8). **3 M-level findings (P4):** H6 (cross-module adapter pattern RESOLVED), C2 (no-raster pivot ACCEPTED), M2 (orphan-blob delete-on-overwrite RESOLVED). **M1 (ClamAV-skip accepted+documented):** signature images use `IFileSecurityService.ValidateExtension` + PNG/JPEG magic-byte narrowing (no deep-scan queue, incompatible with sync set-on-profile); gate P5 sign on scanned-clean (or run through quarantine before P5) filed as `SIGN-PROFILE-IMAGE-CLAMAV-SCAN` follow-up. **P6 Slice A FE (2026-06-30):** user profile page refactored to antd Tabs; "Chữ ký số" tab added with SignatureManagementTab component (profile CRUD, enrollment list, status badges). Hand-written react-query hooks consume new BE endpoints (overview, profiles CRUD, enrollments list). 8 vitest, full suite 1225/1225 + typecheck/eslint GREEN, code-reviewer APPROVE_WITH_NITS. **P6 Slice C FE (2026-06-30):** admin dashboard at `/admin/signature-enrollments` (standalone route, JWT-tenant-scoped BE, separate from cross-tenant config admin). Paginated pending-enrollment queue (Approve/Reject actions with Popconfirm, reason-required reject modal, status badges, scope-note banner). Hand-written react-query hooks consume BE admin endpoints. 4 vitest, full suite 1229/1229 + typecheck/eslint GREEN, code-reviewer APPROVE_WITH_NITS. **P6 Slice D FE (2026-06-30):** production Files remote-PAdES sign modal with a PDF visible-stamp PLACEMENT picker (pure screen→PDF-point geometry, react-pdf overlay) — code-reviewer caught a C1 cross-boundary defect (FE multi-page picker vs BE writer always page-1) → fixed in BE (`SignatureAppearance.Page` + `PdfDocumentModel.PageObjectNumber` flat-tree fail-closed + writer targets page N). **P6 Slice B FE (2026-06-30):** self-service enroll modal (Remote Mysign / USB VGCA reusing vgca-usb-adapter) + **NEW BE `PUT /me/signing-national-id` self-service set-CCCD** (reversed Phase-2 admin-only-CCCD anti-circular design per user; safe — set-CCCD writes a claim, binding enforced downstream by enroll possession-proof + SignerBindingGuard; guardrails 12-digit + active-enrollment lock + tenant-assert). code-reviewer APPROVE_WITH_NITS, M1/M2 fixed. **P8 COMPLETE (07-01):** HA deterministic-key hardening. **P7 CLOSED (07-01):** smoke runbook + dependency-map audit + README; Alpine musl render verified (rasterizer 2/2 on `linux-musl-x64`); manual OTP operator-gated. **Plan `260623-1950` fully CLOSED.** Multi-signer separately deferred (`MULTI-SIGNER-VISIBLE-STAMP`).
- **SIGN-PADES-STAMP-RASTER-MUSL-CI** — ✅ RESOLVED 2026-07-01 (P7 close-out) — ran `SignatureStampRasterizerTests` **2/2 GREEN on `linux-musl-x64`** in a real `mcr.microsoft.com/dotnet/sdk:10.0-alpine` container (apk fontconfig/font-noto/freetype/icu-libs; `NuGetAudit=false` mirroring the API Dockerfile; read-only source mount + tar-copy so host obj untouched) → QuestPDF baseline-JPEG raster survives musl native — no format drift / missing-font / license-init failure. Repeatable via the same `docker run` one-liner (see P7 journal/runbook). _Original:_ T4 🔵 (BE, signing Phase 7 infra, filed 2026-06-30 from Phase-5-Slice-2 code-review H1) — add linux-musl-x64 (Alpine) execution of `SignatureStampRasterizerTests` asserting `JpegImageInfo.Parse` accepts QuestPDF's baseline-JPEG output format, before relying on visible stamps in production. Aligns with plan Phase 7 musl-smoke test gate (currently P7 is broader); overlaps existing smoke-test scope. Execution context: Alpine container, QuestPDF Community license, Noto Sans font. Failure modes: baseline-JPEG rejected by parser (format drift), font rendering unavailable (no system fonts), license init fails. FIX: narrow to just the rasterizer tests + add pre-check before stamps go live.
- **SIGN-P5-PADES-COMPLETION-MULTIPOD** — ✅ RESOLVED 2026-07-01 (`/cook` phase-8 plan `260623-1950`, deterministic-key variant) — HA completion concurrency hardening via **deterministic blob-key instead of row-claim** (user-approved deviation from plan's original row-claim design). Changed `PadesCompletionService.CompleteAsync` signed-PDF storage key from random `{TenantId}/{NewIdMonotonic()}.signed.pdf` → deterministic `{TenantId}/{TransactionId}.signed.pdf`. Because `PadesPdfAssembler.Assemble` is a pure function of INITIATE-frozen resume state + polled signature, byte-identical across replicas, two concurrent poll replicas write the SAME key + SAME bytes → collapse to one idempotent blob. RowVersion still elects the single Initiated→Completed transition. `IGuidGenerator` dependency removed from service (was only the random-key source). **No schema change / migration** (0 ClaimedBy/ClaimedAt columns added). Gates: unit 9/9, Identity.Infrastructure 331/331, Architecture 149/149. code-reviewer APPROVE (0 blocking, 1 informational YAGNI note on TransactionId charset, skipped).

### Closed 2026-07-02

- **SIGN-ENROLL-REMOTE-POSSESSION-PROOF-BINDING-CIRCULAR** — ✅ RESOLVED 2026-07-02 (7th ship, T3 🟡, BE security fix) — remote (Viettel Mysign) signature-cert enrollment was failing with "Remote possession proof failed — the signer did not complete the sign challenge." **Root cause:** `EnrollMySignatureCertCommand.ProveRemotePossessionAsync` delegated to `SignPayloadCommand` to sign the enrollment challenge; `SignPayloadCommand` runs the anti-impersonation `SignerBindingGuard`, which required an already-Approved enrollment matching the cert being enrolled → circular dependency, fail-closed forever. **Fix:** added `SkipBindingGuard` flag (bool, default false) to `SignPayloadCommand`. Enrollment possession-proof passes `true`; normal signing keeps it enabled. Possession-proof establishes the binding (challenge signed by caller using their cert), so pre-enrollment binding-guard check was circular. Attribution preserved: sign issued against caller's own `SigningNationalId`; post-proof CCCD-equality check; enrollment recorded `Pending` for admin approval. Files: `SignPayloadCommand.cs`, `EnrollMySignatureCertCommand.cs` (+2 test files). Gates: Identity.Application.Tests 417/417, Architecture 23/23, build 0W/0E. code-reviewer APPROVE. Committed local.
- **SIGN-PENDINGTX-CLEANUP-JOB-LEAST-PRIV** — ✅ RESOLVED 2026-07-02 (T3) — new `TenantScopedSweep` helper (Identity.Infrastructure/BackgroundJobs/TenantScopedSweep.cs) enumerates all tenant PublicIds from the NON-RLS `Tenants` registry, runs each job's sweep per-tenant with `DocFlowTenantSession.PinAsync` setting SESSION_CONTEXT; poison-tenant isolation via try/catch. `PendingTransactionCleanupJob` + `EnrollmentNonceCleanupJob` both updated to use the helper for per-tenant iteration + explicit `TenantId == tenantId` predicate. No schema/migration change, no public contract change; RLS retained. Gates: cleanup-job tests 9/9 each, Architecture 149/149, Identity.Infrastructure build clean. code-reviewer APPROVE_WITH_NITS.
- **ARCH-RLS-POLICY-MATRIX-SIGNERNONCE-OMISSION** — ✅ RESOLVED 2026-07-02 (T4) — added missing `("identity", "SignerEnrollmentNonces")` entry to `ExpectedPolicies` matrix in `RlsPolicyPresenceTests` (count 49→50, identity 9→10); fixed pre-existing red test that DB had 50 policies since 2026-06-25 migration `20260625033442`.
- **SIGN-PROFILE-IMAGE-CLAMAV-SCAN** — ✅ RESOLVED 2026-07-02 (T4, BE security) — signature image upload now gates on synchronous ClamAV deep-scan (mirrored from async Files quarantine pipeline; same feature flag `feature:files.virus-scan`; fail-closed 503 on scanner error, 422 on infection). Adapter (`UserSignatureImageStoreAdapter`) gains `IVirusScanner` + `IFeatureFlagService` deps. Close-note: Phase-4 M1 conscious-skip closed; images scanned before embedding into signed PDFs others download. Adapter tests 12/12, Identity.Application 415/415, Arch 149/149 all GREEN.

### Closed 2026-07-04

- **SIGN-ORCHESTRATOR-VERIFYSIGNERBINDING-DEADCODE** — ✅ RESOLVED 2026-07-04 (T4 🔵, BE) — removed dead `ISignatureOrchestrator.VerifySignerBindingAsync` facade method (0 callers, superseded by `ISignerBindingGuard.VerifyAsync` invoked by strategies/`SignPayloadCommand` at INITIATE); removed orphaned private `LoadAsync` + unused ctor `ISignerBindingGuard` param; fixed 9 test ctor call-sites. Build 0W/0E, tests 160/160, code-review 0 blocker.

### Closed 2026-07-07

- **FORMS-PUBLIC-SUBMIT-500-TENANT-AGNOSTIC** — ✅ RESOLVED 2026-07-07 (T2 🟠, BE bug, user-facing 500) — `POST /api/v1/public/forms/{code}/submit` now returns HTTP 200 for anonymous requests. **Fix:** `SubmitPublicFormCommand` marked `ITenantAgnosticCommand` to opt out of ambient-tenant gate; new `ICrossModuleUnitOfWork.BeginAsync(Guid tenantOverride, CancellationToken)` overload — handler resolves template tenant + pins SESSION_CONTEXT before INSERT via `uow.BeginAsync(row.TenantId, ct)`, enabling RLS to admit the write under future least-privilege login. **Reusable primitive:** tenant-override UoW overload for any tenant-agnostic write path (public forms, internal migrations, system jobs). Tests: unit 14/14, integration 4/4 (HTTP 200 guard + Testcontainers SESSION_CONTEXT read-back). Closes latent greenfield defect (no prod traffic impact).

### Closed 2026-07-23

- **SEEDER-RLS-BLIND-CHECK-THEN-INSERT** — ✅ **RESOLVED 2026-07-23 tối** (fix-direction (c) ambient tenant-zero, existing Slice-1D plumbing). Scout narrowed the "12 seeders" blast radius to **exactly ONE Production-path offender**: `SystemParamSeeder` is NULL-tenant-safe **by design** (`RlsMigrationHelper.NullableTenant.cs` documents "NULL-tenant INSERTs allowed so the SystemParamSeeder can write global rows without db_owner"); MasterData tables (Provinces/Districts/Wards/Taxonomies) and `identity.Tenants` have **no RLS policy** (full matrix: grep `GenerateRlsPolicyStatements` — 24 table groups); the remaining 10 seeders are Development-only (run as sa/db_owner → not blind in practice; residual for a future least-priv-Development posture, note only). Fix: `NotificationTemplateSeeder` pushes `AmbientTenantScope(Guid.Empty)` BEFORE creating its DI scope → real ITenantContext factory branch 2 → `ScopedTenantContext(Guid.Empty)` → `TenantSessionContextInterceptor` pins SESSION_CONTEXT → FILTER matches the global rows for both the read and BLOCK-AFTER-INSERT. 3 unit tests pin both branches + dispose-restore (real-path drift guard = `ConsumeScopeTenantContextTests` per review). **Empirical (kind, live)**: NonAudit re-activated → pod boots CLEAN under non-db_owner (was CrashLoop) → `/health/ready` overall **Healthy** incl. `audit-read-isolation: Healthy — NonAudit login configured` (first time ever) → seeder seeded=0 (AnyAsync SAW the 11 rows through RLS) → second restart also clean → smoke ALL GREEN. Review DONE_WITH_CONCERNS 0 blocker (Low-1 test-doc claim fixed in-slice). Original evidence + fix-directions preserved in git history of this row.
- **TEST-MFA-HEADER-STALE-MASTERDATA-ORG** — ✅ RESOLVED 2026-07-23 (slice `260723-1135-test-infra-*`) — 17 integration test đỏ âm thầm ~2,5 tháng: controllers gained `[RequiresMfa]` (`3d459c9eb` 05-02) nhưng 2 test helper (MasterData taxonomy + Organization) không gửi `X-Test-MfaAt` → 403. Fix: thêm MfaAt vào 2 helper + `includeMfaClaim: true` cho GetTree. In-slice mở rộng: 3 test Organization lộ lớp 2 — stale Guid assert trên `orgUnitId` (DTO cố ý `long` FK T1→T2) → sửa exact long-assert qua `GetOrgUnitDbIdAsync`. MasterData 15/15 + Organization 37/37 xanh.
- **TEST-LOCALSQL-TRUNCATE-KILLS-MIGRATION-SEED** — ✅ RESOLVED 2026-07-23 (cùng slice) — `TruncateAllDataAsync` wipe seed nhúng migration trên warm DB, migration recorded không re-run → 2 test Audit không bao giờ pass được ở local-SQL (false-red mode #3, isolated re-run KHÔNG bắt được). Fix: `ReseedMigrationEmbeddedDataAsync` (3 seed set: retention global-default + ClassificationRules ×4 + MfaPolicies ×3, giá trị verbatim, C#-side OBJECT_ID guard vì batch ad-hoc fail 208 compile-time kể cả IF unreachable — review H1 bắt 3 fixture non-Main sẽ sập). Audit 84/84 + Messaging 12/12 + Identity.Infra 339/339 xanh.
- **K8S-LOCAL-P1-FIRST-HELM-INSTALL** — ✅ RESOLVED 2026-07-23 PM (plan `260723-1258-*` P1) — first-ever helm render/install/upgrade của stack lên Docker Desktop kind: SMOKE ALL GREEN ×3 (install + reseed-from-zero drill + second-live-upgrade), migrate job 121 migrations/98s qua pre-install hook. **10 bug lộ và fix tại gốc**: 5 chart-never-rendered (probe path, thiếu serviceaccount.yaml, hook-ordering ConfigMap/Secret/SA, configmap thiếu key env tham chiếu, migrate-job không đường lấy conn) + 3 build-infra (web thiếu dockerignore, phantom rc-upload import, **web image bất khả build từ khi VGCA land** — context không với tới `other/`; compose authserver context stale cùng sửa) + 2 k8s-ification (thiếu startupProbe → liveness kill pod giữa seed 14k users, readiness cần conn AuditAdmin). Review REQUEST_CHANGES → C1 (keep+hook mâu thuẫn/ESO-wipe) + H1 (env last-wins vô hiệu hóa runbook RLS-PRINCIPAL) + H3 (rollback skew) fix + đóng bằng empirical upgrade-2 (hooks survive, external secret 4 keys intact). Probe fact: containerd-store KHÔNG chia image → ctr-import fallback (README §Facts). New: `infra/k8s-local/` + `infra/helm/web/` + values-local ×3 + reseed/secrets scripts. P2 (Production/RabbitMQ/NonAudit) + P3 (Prometheus/soak) pending.
- **UC-MD-02-CASETYPE-CRUD-DESCOPE** — ✅ RESOLVED 2026-07-23 (user decision b: re-scope SRS) — leg-2 audit follow-up phát hiện P2 audit's "MasterData ✅ COMPLETE" sai cho UC-MD-02: CRUD endpoints (`POST/PUT/PATCH /masterdata/admin/case-types`) **never-built** (`git log -S "CreateCaseType"` = 0 add) và 0 FE consumer. Root-cause: UC mồ côi — domain tiêu thụ (Cases module) đã full-delete 2026-05-01 PR #71 `fca8a9efd` (Cases cũ dùng `enum CaseType` riêng, chưa từng consume MasterData entity), nhưng SRS snapshot chốt 2026-05-25 SAU đó vẫn spec đủ CRUD. **User descoped khỏi v1.0**; CaseType surface (entity+seed+GET+4 test) giữ dormant nguyên trạng — GET vô hại, removal effort > giá trị. SRS banner tại `uc-platform.md#uc-md-02` + `tc-platform.md`. Leg 1 verdict giữ 68/68 PASS (67 built + 1 descoped-by-decision). Re-spec chỉ khi domain hồ sơ mới xuất hiện. Evidence: `audit-260723-1050-leg2-*-report.md` §3.

### Closed 2026-07-23 (records recovered — the backlog row was the only copy)

- **FILES-DIGSIG-DEAD-SEAM** — ✅ RESOLVED 2026-07-23 PM — seam XÓA (không comment-keep): full-span grep (src+tests+web) xác nhận 0 caller, và stub trả `IsValid=true` = fake pass-through nếu bị wire nhầm (delete thắng keep). 6 doc mention stale sửa cùng slice (compliance-evidence-checklist trỏ về Identity signing stack thật, backlog stub-inventory, tech-stack ×2, codebase-summary; SRS giữ nguyên — snapshot lịch sử). Recover: `git log -S IDigitalSignatureService`. Review APPROVE. Files tests 15+23+144+37 xanh.
- **RABBITMQ-HEALTHCHECK-LIVE-BROKER-VERIFY** — ✅ **RESOLVED 2026-07-23 PM** (🔵 T4, k8s-local plan `260723-1258-*` P2 RabbitMQ cutover — first deployment ever to set `Bus:Transport=RabbitMQ`). Both verifications shot against the live broker in the kind cluster: **(1) Degraded-not-500** — broker up → `rabbitmq: Healthy`; `kubectl scale deploy rabbitmq --replicas=0` → `rabbitmq: Degraded` + `masstransit-bus: Degraded` with `/health/ready` **still HTTP 200**; scale back up → both Healthy again (ProbeConnectionSource reconnected on its own). **(2) No connection leak** — 65 probes → broker reports a stable **2** connections from the API pod (1 MassTransit bus + 1 cached probe), unchanged between probe 35 and probe 65; the ~8.6k/day leak class is dead. BONUS bug found & fixed at the cutover: the probe logged in as user `''` (appsettings ships `Username: ""` and `"" ?? "guest"` keeps `""`) while MassTransit accidentally-greened on the same empty strings — `IsNullOrEmpty` fallbacks now in BOTH `HealthCheckConfiguration` and `MessagingRegistration` so probe and bus share credentials by construction. Latent key mismatch also noted (appsettings `VirtualHost` vs code `VHost` — benign, both resolve "/"). **FOLLOW-UP (review M2, XS, prod-rollout item):** the IsNullOrEmpty fix means a prod whose Vault `Bus__RabbitMq__Password` key gets blanked now silently falls back to guest/guest (before, it failed LOUD as user `''`) with no health-signal — at prod RabbitMQ rollout, either add Bus creds to ProductionSecretValidator.RequiredKeys conditionally on `Bus:Transport=RabbitMQ`, or rely on the broker having guest disabled (standard hardening) and document that dependency.

## 2026-07-25 (Soak post-mortem — ephemeral local data tier killed the 44h run)

> Row `VAULT-DEVMODE-INMEMORY-KILLS-SOAK`, filed AND resolved 2026-07-25 during the P3 close-out of plan `260723-1258-k8s-local-stable-monolith-deploy`. Commit `d04d6b842`. The session opened expecting to read soak numbers and close the plan; the cluster showed 146 CrashLoopBackOff restarts instead. Code-review returned 1 Critical + 1 High + 4 Medium; C1/H1/H2/M1/M3/M4 fixed in-slice. Sibling row `SOAK-24H-REQUIRES-STAGING-NOT-LAPTOP` stays OPEN in the backlog (blocked on a staging environment existing).

- **VAULT-DEVMODE-INMEMORY-KILLS-SOAK** — ✅ RESOLVED 2026-07-25 (🟠 T2, infra).
  **Soak outcome, stated honestly: the ≥24h criterion was NOT met.** The run started 2026-07-23T12:59Z produced **~2.5h of valid clean data** (`restarts=0` through 07-23 14:50Z; one CSV sample at 13:11Z with every criterion green), then the API and AuthServer CrashLooped for ~36h to **146 restarts**. Timeline reconstructed from Prometheus retention (`kube_pod_container_status_restarts_total` range query, not inference): `restarts=15` by 07-24 01:50Z, then ~+11.5/h at the 5-min backoff cap. One clean sample is not a 24h pass — plan P3 is recorded COMPLETE-PARTIAL, not closed.
  **Root cause was the environment, not the application.** `infra/k8s-local/vault.yaml` ran Vault in dev-mode (in-memory storage) and `sqlserver.yaml` used `emptyDir`. A host sleep/resume restarted both pods → Vault's KV store and the entire DB schema vanished → `ProductionSecretValidator` fail-fast on every boot (`Production secret missing or contains DEV marker: Encryption:FieldKey`; the accompanying `[Vault] Could not load secrets from Vault: {"errors":[]}` is a 404 on an empty store, **not** a credential error). The validator behaved correctly throughout. The dev-mode limitation was already written into the plan's §Secrets hygiene — what was never computed was its effect on a multi-day soak.
  **Fix:** Vault → `storage "file"` on a 1Gi PVC + an `unseal` sidecar (`infra/k8s-local/vault-unseal.sh`) that initializes on first boot and re-unseals after every restart; SQL Server → 10Gi PVC; both `strategy: Recreate` so two writers never share one volume.
  **New hazard the fix created, and its guard:** because the data tier now survives, `scripts/k8s-local-reseed.sh` is no longer freely re-runnable — step 5b mints NEW random encryption keys, which against a surviving database would orphan the key its existing rows were encrypted under. The script now refuses to run while the data PVCs exist and requires `RESET_DATA=1` to wipe data and keys together; that path `helm uninstall`s the releases rather than deleting their objects, since helm does not reliably recreate resources removed behind its back.
  **Three behaviours established by probing rather than assumption, each commented beside the code that depends on it:** (1) the `hashicorp/vault` image ships **no curl**, so the original Kubernetes-Secret key store silently no-op'd — the key now lives on the vault PVC beside the data it unlocks, so key and data can never drift apart; (2) `vault operator init -format=json` / `vault status -format=json` are **pretty-printed**, so line-based `sed`/`grep` patterns matching `"field":value` never fire — every parser flattens with `tr -d` first (this bit in **3 separate places**, including the reseed gate, and in the init path it produced an empty unseal key *after* `operator init` had irreversibly run, leaving an initialized-but-unopenable vault); (3) `vault status` **exits 2 when sealed** — a status code, not a failure, so `|| fallback` discards perfectly good JSON.
  **Review C1 (Critical, fixed in-slice):** `initialize || log ...` disables `set -e` for the **entire body** of the callee, so `store_key`'s `return 1` was discarded and execution continued into `operator unseal` / `token create` / `secrets enable` — reproducing exactly the unrecoverable state the function exists to prevent (an initialized vault whose unseal key exists only in an ephemeral shell variable). Every must-not-skip step after the irreversible `operator init` is now checked explicitly, and the un-regenerable artifact is persisted first. **H1:** `exit 1` inside the reconciler killed the sidecar and scrolled the cause out of the logs → `return 1`. **H2:** the reseed guard's advice text steered users away from the `RESET_DATA=1` escape in the very case most likely to reach it (an interrupted reseed — PVCs are created at step 3 of 7), now distinguishes 3 situations. **M1:** `vault.yaml` mounts a ConfigMap it does not define (created only by the reseed script) → documented, with the `rollout restart` needed after editing the script. **M4:** a 403 from `vault secrets list` was indistinguishable from "kv-v2 not enabled" → output and exit code now checked separately.
  **Verified from zero on the live kind cluster:** reseed → 3 charts deployed → smoke ALL GREEN; then deleted the vault and sqlserver pods → vault auto-unsealed from the stored key (`Initialized true`, `Sealed false`, sidecar logged `vault unsealed from stored key`), all app pods stayed at **0 restarts** with zero "DEV marker" crashes, **200 tables survived** without re-running migrations, smoke ALL GREEN again — precisely the scenario that killed the soak. Also unit-tested both JSON parsers against real pretty-printed shapes (4/4, incl. base64 `=` padding and the `hvs.` token prefix) and added a C1 regression check.
  **Local-only posture, explicitly not production:** 1 key share / threshold 1, unseal key unencrypted beside its data so a local reset discards both. Production still needs a real unseal ceremony with split key custody, plus HA, k8s auth (AppRole/JWT) and audit devices. Stale claims corrected in `infra/k8s-local/README.md` and `docs/deployment-guide.md` §122-126.
  **Residual, deliberately out of scope:** `audit-read-isolation` reports Degraded after a from-zero reset because the NonAudit SQL login is a manual DBA step outside the reseed script — row `P1-STABLE-AUDIT-ISO-OPS-ACTIVATE` already tracks it.
  **Prod implication (real evidence, not theory):** a single Vault restart wipes every secret unless prod runs HA Vault with a genuine unseal ceremony and split key custody. Feeds the prod seal-posture decision now listed among the user-owned open questions.

## 2026-07-23 (Deferred-module config-flag OFF mechanism — option B)

> Row `LAUNCH-DEFERRED-MODULES-OFF-MECHANISM`, filed 2026-07-23 AM from the P2 functional-completeness audit (leg 1's sole open item — the 07-16 decision to defer AI/Collaboration/BusinessProcess/Reporting/Rules to v1.1 had no enforcement mechanism). User chose option B (config-flag registration + FE route-gate) over A (permission-hide, blocked on building PermCodes for 3 modules first) and C (build-exclude, over-engineered for a 1-version deferral). Plan `plans/260723-0829-deferred-modules-config-flag-off/`. Code-review DONE_WITH_CONCERNS → both findings (C1 Critical, H1 High) fixed in-slice.

- **LAUNCH-DEFERRED-MODULES-OFF-MECHANISM** — ✅ RESOLVED 2026-07-23 (🟠 T2, launch-readiness).
  **Mechanism:** `Features:{Ai|Collaboration|BusinessProcess|Reporting|Rules}:Enabled` config flags (default **true** — a missing key means enabled, so dev/test keep today's everything-on behavior with zero config). Declared in `src/host/AqtCoreFW.Api/DeferredModuleFlags.cs`; documented in `appsettings.json`'s `Features` section. Turning a module OFF removes it at 5 layers, all driven from this one class: (1) its `Add{X}Module/Infrastructure/Presentation` DI registration block is skipped in `Program.cs`; (2) its Presentation assembly is removed from the `ApplicationPartManager` AFTER `AddControllers()` — required because project-referenced Presentation assemblies auto-discover via DependencyContext independent of DI, so gating DI alone would 500 the endpoint instead of 404; (3) its DbContext is filtered out of `StartupMigrationSets.EnabledContexts(config)` — a fresh DB never receives the schema; (4) Collaboration OFF also skips `MapHub<ChatHub>("/hubs/chat")`; (5) Reporting OFF additionally calls `RecurringJob.RemoveIfExists("reporting.report-cleanup")` on the OFF branch — a schedule persisted by a previously-ON deployment is not implicitly removed by a bare registration-skip (Hangfire re-reads its own storage, not DI, to fire recurring jobs).
  **Review C1 (Critical, fixed in-slice):** phase-01's own context notes had claimed "Rules `RulesEngineCacheSubscriber` + Reporting `ReportCleanupJob` — tắt theo registration là đủ, không cần gate riêng" — true for Rules (a hosted service dies with its registration block) but WRONG for Reporting's RECURRING job, which needed the explicit `RemoveIfExists` call above. Pinned by a Program.cs text-parse guard fact.
  **New endpoint:** `GET /api/v1/system/module-flags` (host-local `ModuleFlagsController`, `[Authorize]` no policy — lives in the host, not a module Presentation assembly, so it keeps answering when the modules it describes are off) returns `ModuleFlagsDto {ai, collaboration, businessProcess, reporting, rules}` (bool, camelCase) — the FE's read of the same 5 flags.
  **New FE:** `web/src/features/auth/module-flags-api.ts` (`useModuleFlags` hook, TanStack Query, `staleTime: Infinity`, **fail-open** on loading/error — the real gate is the BE 404, not this hook) → `module-flag-gate.tsx` (`ModuleFlagGate` component + `withModuleFlag` HOC wrapping 13 route files) → `web/src/layouts/module-flag-menu-filter.ts` (route-prefix post-filter applied over both menu sources — API-driven `menuTree` and the hardcoded fallback). `app-layout.tsx` additionally gates the CopilotSidebar/ToggleButton on `flags.ai`.
  **Review H1 (High, fixed in-slice):** the first FE cut wrapped only 12 of the 13 deferred-module routes — `copilot-page.tsx` (AI module family) was missed, and the app-layout's Copilot sidebar/toggle rendered unconditionally regardless of `flags.ai`. Fixed: 13th route wrapped + app-layout gated.
  **Mutation-verification story:** default-flip (flag default true→false) turned the corresponding ON-branch assertion RED as expected; the part-removal-disable mutation (skip only the ApplicationPartManager removal step) also turned RED — and while writing that mutation test, 3 of the smoke fixture's 404 probes were found to be weak (hitting routes that don't exist at all, 404 for the wrong reason) and were replaced with real `[HttpGet]` routes that exist only when the module is ON, so the probe actually exercises the gate; the Program.cs text-parse guard (the C1 fix) also turned RED under mutation (reverting the `RemoveIfExists` line).
  **Tests:** `DeferredModuleFlagMigrationFilterTests` (10: 5 `[Fact]` + 1 `[Theory]`×5 `InlineData`, incl. the Reporting recurring-job text-parse guard) · `ModuleFlagsControllerTests` (3) · `DeferredModulesOffHostFixture` + `DeferredModulesOffSmokeTests` (9, REAL Program.cs boot with all 5 flags OFF, per `[[feedback-test-new-wiring-through-real-registration]]`) · FE `module-flag-gate.test.tsx` (7). All mutation-verified non-vacuous.
  **Verify:** Architecture.Tests 200/200 · Startup.SmokeTests 30/30 (incl. 9 new OFF-branch) · FE typecheck clean + gate 7/7 + layouts 30/30 + smoke-pages 62/62 · build 0E. Closure criterion: local-verify (GitHub Actions disabled by user choice).
  **AuthServer:** untouched — registers none of the 5 modules (Identity + Messaging only), migrates Identity + Messaging only.
  **Scope boundary — NOT done:** permission codes for Rules/AI/Collaboration (separate follow-up, only meaningful when a module is ON); DB-backed runtime flag (`IFeatureFlagService`) for module on/off — registration-time deliberately reads `IConfiguration` only (KISS; DB-flag reserved for feature-level toggles inside an ON module); dev-DB schema cleanup when a module goes OFF; `FILES-DIGSIG-DEAD-SEAM` (separate T4 row).

## 2026-07-23 AM (Promtool fixtures executed for the first time — the false green is closed; Alerting soft-delete comment corrected)

> Rows `ALERTING-PROMTOOL-FIXTURES-UNVERIFIED` (T3) + `ALERTING-SOFTDELETE-COMMENTS-CONTRADICT` (T4), both filed 2026-07-21 from the test-rule slice. Bundled: same module, one slice. Audit-at-slice-start: rows 2d old (< 5d threshold), light probe only — probe immediately overturned the row's fix direction (see below). Code-review APPROVE_WITH_NITS → both Low nits fixed in-slice.

- **ALERTING-PROMTOOL-FIXTURES-UNVERIFIED** — ✅ RESOLVED 2026-07-23 (🟡 T3, test-infra).
  **The row's fix direction was stale on arrival:** it said "install `promtool` locally… it ships in the Prometheus tarball". The fixture's own header (written 2026-07-21, same day) already documented a Docker-based 3-command procedure needing NO local install — `alpine/helm` render → extract `spec.groups` → `prom/prometheus` promtool. Docker 29.6.2 runs on this machine. The honest scope was therefore *script the documented procedure and execute it for the first time*, not install anything.
  **Shipped:** `infra/prometheus/run-promtool-tests.sh` (POSIX sh, `set -eu`, MSYS path-mangling disabled, `trap` cleanup of the intermediate render, exit code = promtool's — no pipe masking). awk replaces the header's python extraction (neither image ships python); `grep -q '^groups:'` guard fails loudly on template-shape drift. Fixture header now points at the script; the load-bearing release-name-pinning rationale kept verbatim.
  **First-ever execution: SUCCESS** — all 6 fixture groups (fire AND non-fire cases) pass against a genuine `helm template` render.
  **Mutation-verified non-vacuous, both sides:** (a) fixture-side — relabelling the burst-press notification series `source="test"` → `"scheduled"` turns the suite RED (threshold `> 0`); (b) template-side — removing the `source="scheduled"` selector from the notification expr turns the suite RED. Note: the *evaluation* burst-press case is NOT mutation-sensitive by arithmetic (`0+1x12` gives increase≈5 over 5m, not `> 5`) — the notification case carries that contract, which is also the case the 2026-07-21 slice was written to protect.
  **Closure criterion:** local-verify (script exit 0; both mutations exit 1 then reverted; final run green) — GitHub Actions disabled by user choice, same bar as the week's P1 rows.
- **ALERTING-SOFTDELETE-COMMENTS-CONTRADICT** — ✅ RESOLVED 2026-07-23 (🔵 T4, comment-only). `AlertingDbContext.cs` XML-doc rewritten: the wrong claim ("soft-delete handled via `Enabled` flag; filters intentionally omitted") replaced with the truth — only the TENANT filter is intentionally omitted; soft-delete IS filtered via `EntityTypeConfigurationBase.HasQueryFilter(!IsDeleted)` (reviewer chain-verified `Configure` → `ConfigureSoftDelete` → `DeleteRule` → `Entity.Delete()`); `Enabled` is a separate activation flag. Build 0W/0E, Alerting unit tests 34/34.

## 2026-07-22 PM (Seeder ordering enforced at the composition root — the "dev 2-restart converge" was a real registration-order bug)

> Row `P1-STABLE-SEED-POSTURE`, filed 2026-07-16 (P1 readiness audit §item 8 + 13). Audit-at-slice-start (INVARIANT 260522-A, row 6d old): real-ready, scope narrowed M→S with user approval. Code-review DONE_WITH_CONCERNS → both High findings fixed in-slice.

- **P1-STABLE-SEED-POSTURE** — ✅ RESOLVED 2026-07-22 (🟡 T4, operational robustness — Definition-of-Stable "clean boot/migration/seed" leg).
  **The defect, concretized:** the row said "seeder ordering unenforced (demo-seeder race)". Scout proved the demo-seeder half a non-issue (self-contained or fail-loud) but found a REAL inversion in the tenant/org/user chain: `IHostedService.StartAsync` runs in DI registration order; Identity registers at `Program.cs:191`, Organization at `:215`, so `TenantUserSeeder` (reads `organization.OrgUnits`, comment "Ensure OrgTreeSeeder ran first") ran BEFORE `OrgTreeSeeder` → first-boot graceful-skip → the documented "dev 2-restart converge". Dependency chain interleaves the two modules (`Tenant → Role → OrgTree → User → Assignment`), so no reordering of the two module-registration calls can fix it.
  **Shipped:** the conditional `AddHostedService` calls moved from both module registrations to the API host (`Program.cs`, after `AddOrganizationInfrastructure`, before `E2ETestDataSeeder`), registered in explicit chain order. `Configure<>` options bindings stay module-owned. Precedent: `DemoBusinessProcessSeeder` was already host-resident for the same reason.
  **Review H1 (fixed in-slice):** the first cut left `TenantBackfillSeeder` module-registered → it then ran BEFORE `TenantSeeder`, silently reversing the documented "canonical row wins over backfill's derived TENANT-xxxx fallback" invariant — the exact bug class the slice was fixing. Backfill pulled into the host block after TenantSeeder; chain is now **6 seeders**: `TenantSeeder → TenantBackfillSeeder → TenantRoleSeeder → OrgTreeSeeder → TenantUserSeeder → TenantAssignmentSeeder`. `TenantSeeder.cs` doc-comment repointed at the host block.
  **Guard:** new `SeederStartupOrderGuardTests` (Architecture.Tests) — test 1 text-parses `Program.cs` asserting all 6 registrations in ascending order + `E2ETestDataSeeder` after (HangfireRegistrationConsistencyTests shape); test 2 drives the REAL `AddIdentityInfrastructure` (Development stub env) + `AddOrganizationInfrastructure` with every seed flag ON and asserts NO chained seeder is hosted-registered module-locally + all 5 `IConfigureOptions<>` present (ModuleMediatRRegistrationTests shape, per `feedback-test-new-wiring-through-real-registration`). Mutation-verified non-vacuous: swapping OrgTree/User in Program.cs turns test 1 red.
  **Review H2 (fixed in-slice):** `SeederSmokeFixture`'s documented premise ("user seeder runs first, finds no OrgUnits, skips — fast") was invalidated — the single-boot smoke now seeds the full MaxLevel=2 org tree + user set + real assignments. Smoke re-run: **11/11 green, 3m56s** (password hash computed once + shared, cost is EF inserts) — the smoke is now STRONGER (single-boot convergence exercised end-to-end). Fixture + `ApiStartupSmokeTests` comments rewritten.
  **Password hygiene (user decision: delete key, keep fallback):** `SeedPasswords:BtcSeedPassword` plaintext deleted from `appsettings.Development.json` — repo-wide grep confirmed exactly ONE reader (`TenantUserSeeder.cs:42`) with an IDENTICAL code fallback, `IsDevelopment()`-gated (prod never runs it), prod config has no SeedPasswords section. Dev boot behavior unchanged.
  **Semantic note (review M2, accepted):** host gate uses `GetValue<bool>("X:Enabled")` vs old `.Get<Options>()?.Enabled == true` — equivalent for missing/valid values; malformed value still fails at boot (later stack for malformed SIBLING properties: registration-time → StartAsync-time). Correct for a boot-order-critical flag.
  **Docs (review M3):** `docs/runbooks/dev-data-reset-runbook.md` §2-restart marked FIXED (reset-all.ps1 `$MK_ABORT_NOUSERS` restart branch KEPT as safety net — if it ever fires again, that's an ordering regression and the arch-test should have gone red first); `docs/dependency-map.md` §5b records the host registration site + the load-bearing assumption that demo seeders (registered BEFORE the cluster) rely on TenantId being an unenforced Guid stamp — adding an FK to `identity.Tenants` on Workflow/Forms would break first boot.
  **Verify:** Architecture 187/187 (2 new) · Identity.Infrastructure 339/339 · Organization 79/79 · Startup.SmokeTests 11/11 · API host build 0E. Closure criterion local-verify (Actions disabled).
  **Contract:** none — module registration method signatures unchanged; behavior identical when flags identical, only ORDER changed.
  **Scope boundary — NOT decided here:** prod seed posture (readiness report §4-Q2, user-owned); the generic 16-seeder ordering framework (YAGNI — only 3 real dependency edges existed, all now pinned); runbook's pre-existing `BtcIdentitySeeder` name drift (older rename, out of scope).
  **Unresolved:** §4-Q2 prod seed posture; reviewer Q2 (simplify the now-dormant reset-all restart branch) — kept as safety net by verdict.

## 2026-07-22 (Email PII log masking — a canonical call-site helper, and 2 sibling leaks the row never named)

> Row `NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL`, filed 2026-07-21 from the SMS-half code-review. Code-review DONE (H1 surfaced + fixed in-slice, no residual Critical/High). Closure criterion is local-verify (GitHub Actions disabled by user choice, same bar as the rest of this week's rows).

- **NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL** — ✅ RESOLVED 2026-07-22 (🟡 T3, security hygiene).
  **The defect as filed:** `MailKitEmailSender.cs:37,41` logged the recipient email `{To}` in full on both the success and failure path. Email is Confidential PII (QĐ 742 §66) and encrypted at rest by the same `EncryptedStringConverter` as `PhoneNumber`, so the log sink held plaintext for a column the database deliberately encrypts. Unlike the SMS leak fixed the day before, this one was **not** armed by a recent change — live since the sender was written, wide exposure (every email notification ever sent).
  **Scope expanded in-slice (user-approved) — the row named one call site; code-review (H1) found the same defect class live in 2 more, unfixed in Notifications.Application:** `SendPasswordResetEmailHandler.cs:30` (`LogInformation` logged full `evt.Email`) and `AccountAutoDisabledNotificationConsumer.cs:69-70`/`:81-82` (success `LogInformation` + failure `LogWarning`, both logged full `evt.Email`). Closing the row on the infra sink alone would have been a false closure — the same shape `[[feedback-fixing-a-bug-can-arm-a-dormant-hole]]` already caught twice this week.
  **Shipped:** new shared `PiiLogMasking` (public static, `src/shared/AqtCoreFW.SharedKernel/Compliance/PiiLogMasking.cs`) — `MaskEmail` keeps the first local-part char + the FULL domain (`user@gov.vn` → `u***@gov.vn`, so operators can still triage delivery failures by tenant domain) and `MaskPhone` keeps the last 4 digits. Rationale documented in the helper's XML doc: Serilog's `SecretMaskingDestructuringPolicy` provably cannot mask a scalar message-template argument (its regex matches property *names*, and `TryDestructure` only inspects complex objects), so masking must happen at the call site — this is now the canonical one.
  **DRY consolidation (reviewer flagged a 3-copy threshold):** retired `MailKitEmailSender`'s private `MaskEmail` (never shipped past internal — one call site) and the pre-existing `WebhookSmsProvider.MaskPhoneNumber` (shipped 2026-07-21 for the SMS leak); both now call `PiiLogMasking`. SMS behaviour is bit-identical (still last-4).
  **All 5 email log sinks now mask:** 2 in `MailKitEmailSender`, 1 in `SendPasswordResetEmailHandler`, 2 in `AccountAutoDisabledNotificationConsumer`.
  **Tests:** new `PiiLogMaskingTests.cs` — 18 facts covering email + phone edge cases (short local part, no `@`, null/empty, already-short numbers), asserting exact masked output (non-vacuous). `MailKitEmailSenderTests.cs` — local mask tests removed (behaviour now owned by the shared helper's own tests).
  **Verify:** SharedKernel.Tests 233/233 (+18 new), Notifications.Infrastructure.Tests 16/16, Notifications.Application.Tests 39/39, Architecture.Tests 185/185, full build 0 errors (only the pre-existing NU1902 AngleSharp warning). Code-review DONE — H1 (the 2 sibling call sites) surfaced and fixed in-slice; no residual Critical/High.
  **Contract:** none — additive shared helper (no DI registration, no interface) + call-site-only edits in 4 files. A transient `InternalsVisibleTo` added to `AqtCoreFW.Notifications.Infrastructure.csproj` mid-slice was reverted before closing (no longer needed once the private mask method was retired in favour of the public shared helper).
  **Files changed:** `PiiLogMasking.cs` (new), `MailKitEmailSender.cs`, `WebhookSmsProvider.cs`, `SendPasswordResetEmailHandler.cs`, `AccountAutoDisabledNotificationConsumer.cs`, `PiiLogMaskingTests.cs` (new), `MailKitEmailSenderTests.cs`.
  **Scope boundary — NOT done:** the repo-wide masking-strategy question (call-site guard vs a Serilog enricher closing the `SecretMaskingDestructuringPolicy` scalar-argument gap) stays **OPEN** — this slice does not decide it; it establishes `PiiLogMasking` as the canonical call-site helper until it is. Every future PII log line still needs its own call-site guard.
  **Unresolved (open, non-blocking):** the repo-wide masking-strategy decision above.

## 2026-07-21 (Alert test button — one rule, real outcome, and a self-inflicted false alarm avoided)

> Plan `plans/260721-1647-alerting-testrule-single-rule-outcome/plan.md`. Row `ALERTING-TESTRULE-REPORTS-202-ON-FAILED-DELIVERY`, filed 2026-07-17 from the observability-hygiene EMAIL slice's code review. Code-review DONE_WITH_CONCERNS (1 Critical + 1 Medium, both applied in-slice). Closure criterion is local-verify (GitHub Actions disabled by user choice).

- **ALERTING-TESTRULE-REPORTS-202-ON-FAILED-DELIVERY** — ✅ RESOLVED 2026-07-21 (🔵 T4, bug + observability).
  **The row named one defect; the source held three.** As filed: an admin testing a broken EMAIL rule receives `202 Accepted` / "Test evaluation triggered." while delivery actually failed, because the per-rule catch swallows dispatch failures. Reading `AlertingAdminController.cs` against `AlertEvaluationJob.cs` added two worse ones. (1) `TestRule` called `evaluationJob.ExecuteAsync(ct)`, which selects `Where(r => r.Enabled)` across the whole table — so testing rule X evaluated Y and Z, **sent their real notifications**, called `RecordBreach()` on them, incremented `ConsecutiveBreaches` and moved `LastTriggeredAt`. Testing one rule rewrote other rules' operational history. (2) A **disabled** rule was filtered out and never evaluated, while the endpoint still answered 202 — and disabled is precisely the state of a freshly seeded rule, including the self-monitor rule seeded that same morning, making the most likely thing an admin wants to test the one thing the endpoint silently did not do.
  **The row's prescribed fix was rejected.** "Return per-rule dispatch outcomes from `ExecuteAsync`" addresses only defect 3, leaves 1 and 2 standing, and gives a Hangfire recurring job a return type whose only consumer is one admin button.
  **Shipped:** `AlertEvaluationJob.TestRuleAsync(rule, ct)` evaluates exactly the requested rule, enabled or not, and returns `AlertRuleTestOutcome` / `AlertDispatchOutcome`. It **really dispatches** — verifying the delivery path is the entire purpose, and a dry run could not have surfaced the broken EMAIL channel that produced this row — but **writes no state**: no `RecordBreach`, no `ConsecutiveBreaches`/`LastTriggeredAt` change, no `AlertBreached` audit entry, no `SaveChanges`. `SendNotificationAsync` became `DispatchAsync` returning an outcome, with scheduled-path exception semantics bit-identical (EMAIL still rethrows into `ExecuteAsync`'s per-rule catch so `rule_error` still counts; WEBHOOK still swallowed; the unconfigured-`Email:Host` early return unchanged). The controller returns 200 with the outcome, typed via `[ProducesResponseType(typeof(ApiResponse<AlertRuleTestOutcome>), 200)]` — the untyped form would have regenerated the Orval client as `void`, hiding the new body from the FE and reintroducing the defect client-side.
  **The slice would have armed a self-inflicted false alarm (code-review Critical, fixed in-slice).** The test path reused the evaluation helpers and therefore incremented the same `AlertingMetrics` counters as the scheduled evaluator. Measured against thresholds shipped hours earlier the same day: `AqtCoreFWAlertEvaluationFailing` fires at `> 5` over 5m and a failing press records **two** increments (`query_failed` + `no_data`), so **three presses page ops**; `AqtCoreFWAlertNotificationFailing` fires at `> 0`, so **one press against a broken EMAIL rule** — the single most likely use of the button — pages ops about an evaluator that is working correctly. The slice also *widened* the counters' input set, because the test path reaches disabled rules the scheduled job never evaluates. This had been documented as "accepted rather than engineered"; that judgement was wrong and the review was right. Fixed with a `source` tag (`scheduled` / `test`) on all 7 increment sites, with both Prometheus expressions in `infra/helm/aqtcorefw/templates/prometheusrule.yaml` and the seeded in-app rule in `AlertRuleDevSeeder.cs` narrowed to `source="scheduled"`.
  **Knock-on caught while fixing it:** `infra/prometheus/alert-rules-test.yml` fixtures carried no `source` label, so once the expressions gained the selector those promtool cases would have stopped firing while still reporting green. Every fixture series was relabelled and two cases added proving a burst of `source="test"` series stays silent. **Not verified — `promtool` is not installed locally and CI is off**, so the YAML is consistency-edited only; filed as `ALERTING-PROMTOOL-FIXTURES-UNVERIFIED`.
  **Tests:** new `AlertEvaluationJobSingleRuleTestTests.cs` (6 facts: single-rule scope, disabled-still-evaluated, no-state-written, really-dispatches, email-throw-reports-undelivered, metric-unavailable-reports-not-evaluated) plus 3 source-tag facts added to `AlertEvaluationJobSelfFailureMetricsTests.cs`. `AlertRuleDevSeederTests` pinned the old metric string and was updated with the reason. **Mutation-proved twice:** injecting `await ExecuteAsync(ct)` at the top of `TestRuleAsync` turned 3 of the 6 new facts red; flipping `TestSource` to `"scheduled"` turned both new tag facts red. Green on revert each time.
  **Verify:** build 0 errors; `AqtCoreFW.Infrastructure.Extensions.Tests` 216 passed / 1 pre-existing skip (alerting subset 34/34, up from 25); Architecture 185/185.
  **Contract:** `POST /api/v1/admin/alerts/{id}/test` changes 202 (no body) → 200 (`ApiResponse<AlertRuleTestOutcome>`). Safe: `usePostApiV1AdminAlertsIdTest` exists in `web/src/api/generated/` but has **zero callers** outside the generated file, so the defect was latent and no live caller breaks. The Orval client is now stale against the API — deliberately not regenerated, as that is a mechanical step for whoever builds the alerting screen.
  **Scope boundary — NOT done:** no controller-level test for the 404 / 200-shape (the repo has no `*.Presentation.Tests` project — the open `NOTIFICATIONS-PRESENTATION-TESTS-PLACEMENT` question, deliberately not reopened here); `Detail` still carries raw `ex.Message` (reviewer M3 — reachable only by an MFA'd SystemAdmin who can already read the rule and its target, and it is `Message` not `ToString()`, so no stack trace); `rule` still passed by reference rather than by value (reviewer M4 — `WritesNoState` is the regression net).
  **Unresolved (open, non-blocking):** whether `Detail` should be reduced to a coded reason once a UI exists, with free text confined to logs.
## 2026-07-21 (SMS notification recipient resolution — PII-scoped phone lookup)

> Plan `plans/260721-1417-notifications-sms-recipient-phone/plan.md`. Row `NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE`, the carried SMS half of the 2026-07-17 email fix. Code-review DONE_WITH_CONCERNS (1 Critical + 1 High, both applied in-slice). Closure criterion is local-verify (GitHub Actions disabled by user choice).

- **NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE** — ✅ RESOLVED 2026-07-21 (🟠 T2, bug + security).
  **The defect:** `SendNotificationCommandHandler` passed `request.RecipientUserId.ToString()` to `ISmsProvider.SendAsync`, whose `to` argument `WebhookSmsProvider` posts verbatim to the webhook — so every SMS notification failed at the provider. Identical in shape to the email half fixed 2026-07-17.
  **The row's open question, answered empirically:** `ApplicationUser.PhoneNumber` **is** AES-GCM encrypted at rest, by the same `EncryptedStringConverter` + `IKeyResolver` as `Email` (`ApplicationUserConfiguration.cs:146-162`), and the column was already widened `nvarchar(256)→(1024)` by `20260510154200_SplitEmailHashGlobalAndTenant` (model snapshot confirms) — **no migration required**. Dapper bypasses the EF converter, so the read decrypts through `IEncryptedFieldDecryptor.Decrypt`, mirroring the Email path.
  **Deviation from the row's prescribed fix (user-approved):** the row specified adding `PhoneNumber` to `UserInfoDto` + extending the Dapper projection. Rejected on least-privilege grounds — `GetUserInfoByIdsAsync` feeds 25 display-name enrichment call sites across Collaboration/Files/Workflow/Organization/Identity, including two SignalR broadcast handlers, while exactly one caller needs a phone number; the number is Confidential PII (QĐ 742 §66). Shipped instead as a separate PII-scoped contract method `IIdentityModuleClient.GetUserPhoneNumbersByIdsAsync(userIds, tenantId, ct)`. **`UserInfoDto` unchanged; zero existing consumers edited.**
  **Tenant scoping hardened beyond the sibling:** `tenantId` is a **required** `Guid` (sibling lookups accept `Guid? = null`), and the SQL omits the `TenantId IS NULL OR` seed-user allowance those lookups grant. Rationale, verified after code-review raised it: `identity.AspNetUsers` carries **no RLS policy** (absent from `tests/integration/AqtCoreFW.TenantIsolation.Tests/RlsPolicyPresenceTests.cs`), so the SQL predicate is the sole tenant isolation on this read — with an optional parameter, one omitted argument at a future call site would have returned phone numbers across every tenant with no compile-time signal. The NULL-tenant allowance exists so every tenant can render a system user's *display name*; that is not a reason to hand out their number.
  **SECURITY (code-review Critical, fixed in-slice) — the fix armed a dormant PII leak in a file this slice never set out to touch:** `WebhookSmsProvider.cs:27,31` logs `{PhoneNumber}` at Information and Error. That line predates this work (`git log -S` → `a7919abf7`) and was harmless for as long as the argument was a Guid. Making SMS work converted it into a plaintext Confidential-PII log sink on every send. **Serilog's `SecretMaskingDestructuringPolicy` provably cannot catch it:** its regex matches secret-ish property *names* (`(Password|Key|Token|Secret|Vault|HmacKey|FieldKey)$` — `PhoneNumber` matches nothing), and `TryDestructure` only fires for complex objects, so a `string` passed as a message-template argument never reaches the policy at all. Fixed by masking to the last 4 digits at the call site (`WebhookSmsProvider.MaskPhoneNumber`). Third instance of the pattern in `[[feedback-fixing-a-bug-can-arm-a-dormant-hole]]`.
  **Failure semantics:** `ResolveRecipientPhoneAsync` mirrors `ResolveRecipientEmailAsync` exactly — recipient absent → `Result.Fail("...not found")`, present with null/blank number → `Result.Fail("...has no phone number")`, then `LogWarning` + `MarkAsFailed` + `UpdateAsync`, never throwing. The three live callers (fire-and-forget `InProcessNotificationModuleClient` + 2 event handlers) discard the `Result`, so a throw would lose the reason. The InApp path keeps its zero-lookup cost: resolution happens inside the SMS branch only.
  **Tests:** new `IdentityReadModelPhoneDecryptionTests.cs` (2 facts — decrypted round-trip + existing-user-without-number maps to null, distinct from an absent user), modelled on the sibling email decryption test and run against a real database. **Mutation-proved non-vacuous:** replacing `decryptor.Decrypt(row.PhoneNumber)` with the raw column turned the test red, then green on restore — a mocked unit test could not have observed the decrypt at all. In `SendNotificationCommandHandlerTests`, the test that pinned the defect was **rewritten, not deleted** (`Handle_SmsChannel_SendsToTheResolvedNumber_NotTheUserId`), plus 3 new facts covering tenant-scoped resolution, both failure branches, and `MarkAsFailed`. Four hand-rolled `StubIdentityClient` doubles in Workflow integration tests gained the new member — a scout had reported no hand-rolled implementors existed, which the build disproved.
  **Verify:** full solution build 0 errors (NU1902 AngleSharp warnings pre-existing); Notifications.Application 39/39 (was 35), Notifications.Infrastructure 16/16, Architecture 185/185, Identity.Integration phone-decrypt 2/2, Workflow touched subset 6/6.
  **Contract:** `IIdentityModuleClient` gains one method (additive — breaking for implementors, all 5 updated: 1 production + 4 test stubs). `UserInfoDto`, `GetUserInfoByIdsAsync` untouched. No schema, no API, no FE-consumed shape.
  **Scope boundary — NOT done:** phone-number format validation (E.164) — the provider owns that; `MailKitEmailSender.cs:37,41` logs `{To}` in full with the identical shape → filed as `NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL` rather than fixed here, to keep the slice scoped.
  **Unresolved (open, non-blocking):** whether SMS dispatch is enabled in any deployed environment (if `SmsWebhookOptions.WebhookUrl` is unset everywhere, the log leak was latent rather than live — changes urgency, not the fix); whether the log sink has any retention/scrubbing policy bounding exposure (none found beyond the secret-name masking policy).

## 2026-07-21 (Migration pre-deploy dry-run — Hangfire boot-lock retry + SQL preflight probe)

> Plan `plans/260721-0900-p1-stable-migration-predeploy-dryrun/plan.md`. Row `P1-STABLE-MIGRATION-PREDEPLOY-DRYRUN`, sourced from `plans/reports/audit-260716-1805-stable-monolith-p1-operational-readiness-report.md` §1 #6 + #3. Two independent concerns, scope user-locked. Code-review DONE_WITH_CONCERNS (0 Critical/0 High). Closure criterion is local-verify (GitHub Actions disabled by user choice).

- **P1-STABLE-MIGRATION-PREDEPLOY-DRYRUN** — ✅ RESOLVED 2026-07-21 (🟠 T2, ops resilience) — both concerns shipped.
  **Concern A (#6 — Hangfire boot-lock retry):** recurring-job registration at API boot drives Hangfire's static `RecurringJob` facade, which acquires a SQL distributed lock; a transient lock timeout on a contended/slow-SQL boot previously hard-failed the entire host. New `RecurringJobRegistrationRetry` (`src/shared/AqtCoreFW.Infrastructure/BackgroundJobs/RecurringJobRegistrationRetry.cs`): `IsTransientLockFailure` classifies by type + SQL error number (`DistributedLockTimeoutException`, `SqlException` 1222/-2, recursing through `InnerException` and `AggregateException.InnerExceptions`), never by message text. `RunWithRetry` retries 3 attempts (2s/4s/6s backoff), then fails soft — logs Critical, boot continues. Fail-soft skips only *re-registration*; already-persisted Hangfire schedules (incl. compliance jobs: audit-log purge, RTBF SLA, alert eval) keep firing on schedule regardless. Non-transient exceptions rethrow unswallowed. `src/host/AqtCoreFW.Api/Program.cs` (~L437-505) wraps the existing 12 recurring-job registration calls verbatim — pure lambda-wrap, zero happy-path behavior change.
  **Concern B (#3 — migration preflight probe):** the `UX_FormTemplate_PublicCode` filtered-unique-index migration (`20260709082745_AddFormTemplatePublicCodeUniqueIndex`) carries a fail-loud `THROW 50000` guard that aborts `MigrateAsync` on dirty cross-tenant-duplicate data — the exact failure mode of the earlier real incident `FORMS-DEV-DB-DUPLICATE-PUBLIC-CODE-MIGRATION-BLOCK` (resolved `99fc5cb0`). New standalone read-only ops script `scripts/260721-migration-preflight-check.sql` mirrors that guard's SQL verbatim; operator runs `sqlcmd -S <server> -d <database> -b -i scripts/260721-migration-preflight-check.sql` before deploy (`-b` required for non-zero exit code on `RAISERROR` severity 16). `OBJECT_ID(N'forms.FormTemplates', N'U')` guard treats first-ever deploy (table absent) as clean. SELECT/PRINT/RAISERROR only — safe against prod, no INSERT/UPDATE/DELETE/DDL.
  **Tests:** `RecurringJobRegistrationRetryTests.cs` (`src/tests/AqtCoreFW.Tests.Integration/Infrastructure/`) — 8 pure `[Fact]`s: `IsTransientLockFailure` classification (direct + nested `AggregateException`) and `RunWithRetry` attempt-count/backoff/fail-soft/rethrow via an injectable sleeper (no wall-clock cost in the test run).
  **Verify:** full solution build 0W/0E; 8/8 new unit tests GREEN; preflight script live-tested against a SQL Server 2022 container across 3 cases (table-missing → clean exit 0, no-duplicates → clean exit 0, seeded cross-tenant duplicate → RAISERROR → non-zero exit).
  **Code-review:** DONE_WITH_CONCERNS — 0 Critical/0 High; 1 Medium (comment-sharpen on the fail-soft rationale in the XML doc) fixed in-slice; 2 Low (benign, not fixed).
  **Contract:** none — additive `BackgroundJobs` helper + wrap-only `Program.cs` change (12 existing calls unchanged, no new/removed DI registration) + a standalone ops script outside the app. No API/DTO/schema change.
  **Dependency map:** not updated — the new helper is shared-infra internal (no cross-module `I*ModuleClient` contract, no entity/DbContext change); confirmed no `dependency-map.md` entry required for this slice.
  **Scope boundary — NOT done in this slice:** an automated CI dry-run gate running the preflight against a prod-like snapshot (the source report's #3 also named this) — blocked on GitHub Actions being disabled by user choice; no CI job exists today to host such a gate. Not filed as a separate follow-up row (no concrete requester yet; would sit parked next to `P1-STABLE-CI-GATES-ENFORCE` for the same reason) — revisit when Actions is re-enabled.
  **Unresolved (open, non-blocking, from the plan):** whether to extend the preflight script to cover additional fail-loud migration guards beyond the one Forms guard mirrored today (only 1 exists in the repo as of this slice); whether a `.ps1`/`.sh` wrapper around the raw `sqlcmd` invocation is worth adding — YAGNI until either a second guard ships or an operator requests the wrapper.

### Restore-drill temp file cleanup (BACKUP-RESTORE-DRILL-TEMP-CLEANUP)

> FU of `P1-STABLE-BACKUP-AUTOMATION`, filed 2026-07-16 from code-review. Row resolved same day as the above, separate small slice.

- **BACKUP-RESTORE-DRILL-TEMP-CLEANUP** — ✅ RESOLVED 2026-07-21 (🔵 T4, low) — `ExecuteRestoreDrillAsync` left `_drill_*.bak`/`.mdf`/`.ldf` temp files uncleaned in `Backup:Directory`; the drill `.bak` record is excluded from retention prune (`BackupRetentionPruner.SelectExpired` selects only `Type == "Backup"/"ScheduledBackup"`) and `.mdf`/`.ldf` never get a DB record at all, so the `/backups` volume accumulated drill artifacts indefinitely.
  **Fix:** new static `DrillTempFileCleanup.TryDelete(paths, logger)` (`src/shared/AqtCoreFW.Infrastructure/Backup/DrillTempFileCleanup.cs`) — best-effort delete per path, swallows `IOException`/`UnauthorizedAccessException`/`PathTooLongException` with a `LogWarning`, never throws. Mirrors the existing `BackupRetentionPruner` swallow-and-log precedent.
  `DatabaseBackupService.ExecuteRestoreDrillAsync` — the 3 temp-path locals (`backupFile`/`dataFile`/`logFile`) are hoisted above the `try` (assigned `null`, then set inline where previously declared with `var`) so a new `finally` can call `DrillTempFileCleanup.TryDelete` on all three regardless of success or mid-drill failure; unassigned paths on an early throw stay `null` and are skipped by the helper. `record.MarkCompleted(backupFile, fileSize)` still runs inside the `try`, before the `finally` deletes the file — the audit trail keeps the path/size even though the file itself is gone afterward.
  **Design chosen (user-approved):** delete all 3 files including the drill `.bak` — a drill is an ephemeral restore-verification artifact, not a recovery source, so nothing needs it kept.
  **Known-limit accepted (user 2026-07-21):** if the drill fails between the restore completing and the temp DB being dropped, SQL Server still holds OS-level locks on the `.mdf`/`.ldf` files, so their delete is swallowed and they persist until a later run cleans up — a rare, logged, bounded leak confined to that one failure branch (the success path and every `.bak` are always cleaned). Documented inline in the helper's XML doc (`<remarks>`-style "Known limit" paragraph); user chose not to fix it in this slice (would require moving `DROP DATABASE` into its own nested `finally`) and did not want a separate follow-up row filed — the note lives inline only.
  **Tests:** `DrillTempFileCleanupTests.cs` (`tests/unit/AqtCoreFW.Infrastructure.Extensions.Tests/Backup/`) — 4 `[Fact]`s: all-existing-files deleted, missing path skipped without throw, null/empty path skipped without throw, locked file swallows the exception and logs a Warning (real `FileStream` exclusive lock on Windows).
  **Verify:** shared infra build 0W/0E (`-warnaserror`); 4/4 new cleanup tests GREEN; 6/6 `BackupRetentionPruner` tests unchanged/GREEN (pruner logic untouched). Code-review DONE — 0 Critical/0 High.
  **Contract:** none — new static helper (no DI registration, no interface) + `finally`-only addition to an existing private-ish method; no API/DTO/schema change. `docs/dependency-map.md` not updated — same-assembly internal helper, single caller, no cross-module `I*ModuleClient` edge.

---

## 2026-07-17 (Notification recipient resolution — EMAIL half; SMS half carried)

> Plan `plans/260717-2015-notifications-recipient-guid-not-address/plan.md`. Row `NOTIFICATIONS-RECIPIENT-IS-GUID-NOT-ADDRESS` (filed earlier the same day from the alerting EMAIL slice scout) split at slice-start into EMAIL (this entry) and SMS (carried, new row `NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE`). Code-review REQUEST_CHANGES (2 High) → both fixed in-slice → APPROVE_WITH_NITS. Closure criterion is local-verify (GitHub Actions disabled by user choice).

- **NOTIFICATIONS-RECIPIENT-IS-GUID-NOT-ADDRESS (EMAIL half)** — ✅ RESOLVED 2026-07-17 (🟠 T2, bug + security) — `SendNotificationCommandHandler.cs:67` passed `request.RecipientUserId.ToString()` straight to `emailSender.SendAsync` as the SMTP `to` address; `MailKitEmailSender.cs:21`'s `MailboxAddress.Parse` threw on every Email notification, across all 3 live callers (`WorkflowTransitionedNotificationHandler`, `ParallelApprovalTaskNotificationHandler`, `NotificationsController.Send`).
  **Fix:** new private `ResolveRecipientEmailAsync` helper in the handler calls `IIdentityModuleClient.GetUserInfoByIdsAsync([request.RecipientUserId], request.TenantId, ct)` — single-element list against the existing batch method (no new contract method; the dictionary's "missing key" is the not-found signal). Called **only inside the `"email"` channel branch**; `"inapp"` (every Forms notification today) is untouched, zero added round-trips (AC-5, test-proven with a zero-calls assertion).
  **Failure handling (user decision 2026-07-17):** user not found, or found with a null/empty `Email` → `Result.Fail` (message distinguishes the two cases) + `MarkAsFailed()` + `UpdateAsync` on the already-persisted `Notification` row, no rethrow — a missing identity is a data problem, not a transient fault, and rethrowing would drive endless Hangfire/MediatR retries.
  **New cross-module edge:** `AqtCoreFW.Notifications.Application.csproj` gains a `ProjectReference` to `AqtCoreFW.Identity.Contracts` (zero-ProjectReference leaf, no cycle — same leaf-edge pattern as the existing `Notification.Contracts`/`OrgUnit.Contracts` edges). Keeps AES-GCM Email decryption and the tenant filter inside `InProcessIdentityModuleClient`, the one place that already implements them correctly; a hand-rolled Dapper read (the "obvious" alternative to taking the reference) would have mailed ciphertext — Email is encrypted at rest and `InProcessIdentityModuleClient.cs:105` decrypts it before returning.
  **Killed assumption (recorded so it is not re-trusted):** an earlier scout pass claimed `UserInfoDto` carries phone + that Email is unencrypted at rest. Both wrong — `UserInfoDto` = `UserId, Email, FullName, TenantId, PrimaryOrgUnitId` (no `PhoneNumber`, which is why SMS could not be batched in), and Email IS AES-GCM encrypted at rest (only usable via the client's built-in decryption).
  **Code-review — 2 High, both fixed in-slice (grew the slice from 3 files to 5):**
  1. **Security — armed a dormant cross-tenant hole.** `NotificationsController.Send` bound `TenantId` from the request body while all 4 sibling actions on the same controller use `ResolveTenantId()`. Pre-fix this was inert (Guid→MailKit throw→nothing delivered); the email fix turned it into a live cross-tenant phishing primitive with a user-existence oracle (200 vs 422), because the loose tenant filter at `InProcessIdentityModuleClient.cs:83-85` (`TenantId IS NULL OR TenantId = @TenantId`) assumes a precondition — "caller already holds the UserId and a tenant-scoped join produced it" — that this HTTP path breaks. Fixed: controller now binds `ResolveTenantId()`. Costs no legitimate capability: `ApiControllerBase.cs:21-32` already grants SystemAdmin a cross-tenant path via `X-Tenant-Id`, so the fix narrows cross-tenant sends from *anyone with the send permission* to *SystemAdmin only*, matching every sibling action.
  2. **Silent-failure gap.** The plan's risk table claimed "callers handle `Result.Fail`" — false. `InProcessNotificationModuleClient.cs:25` returns a bare `Task` and discards the Result (fire-and-forget); both live event-handler callers ignore it too. Unfixed, the recipient-resolution failure would have traded a loud `FormatException` for total silence — the same silent-drop shape the alerting EMAIL slice earlier the same day had just removed elsewhere. Fixed: the failure branch `LogWarning`s (names the notification + reason) before `MarkAsFailed()`.
  **Reviewer also refuted the slice's biggest stated worry:** `NotificationRepository.cs:29-39` — both `AddAsync`/`UpdateAsync` call `SaveChangesAsync` directly, no ambient UoW/transaction pipeline (`Program.cs:140-142` registers only TenantAware/Validation/Audit behaviors). The early-return commits — an unresolvable recipient leaves a persisted `Failed` row, not a vanished one.
  **Tests:** `SendNotificationCommandHandlerTests.cs` — email test rewritten (was asserting `Received(1).SendAsync(RecipientId.ToString(), ...)`, i.e. pinning the bug); new not-found case; new null/empty-email case; new inapp test asserting zero `IIdentityModuleClient` calls; SMS test (`:113`, unchanged behaviour) kept and annotated as pinning a known defect with the follow-up row named, not deleted. New `tests/architecture/AqtCoreFW.Architecture.Tests/NotificationSendTenantBindingTests.cs` (5 tests) pin the `ResolveTenantId()` fix.
  **Test placement caveat (not resolved, flagged):** the repo has no `*.Presentation.Tests` project; the 5 tenant-binding tests live in `AqtCoreFW.Architecture.Tests` (already `ProjectReference`s `Notifications.Presentation`, exposed via `AssemblyFixtures`) as a pragmatic fit — they are controller behaviour tests, not architecture rules. Follow-up filed.
  **Verify:** build `src/AqtCoreFW.slnx` 0W/0E · architecture suite **185/185** (was 180, +5) · `AqtCoreFW.Notifications.Application` 35/35 (was 30) · `AqtCoreFW.Workflow.Application` 642/642 · `AqtCoreFW.Forms.Application` 344/344 · code-review APPROVE_WITH_NITS.
  **Non-vacuity proven by mutation** (each reverted): restoring `RecipientUserId.ToString()` → email test fails · hoisting the lookup above the channel switch → inapp + tenant tests fail · re-trusting the body tenant → 4 of 5 tenant-binding tests fail · removing `MarkAsFailed()` on the early-return path → that test fails (reviewer-found).
  **Contract:** `IIdentityModuleClient`, `UserInfoDto`, `IEmailSender`, `ISmsProvider`, `SendNotificationCommand`, `INotificationModuleClient` all unchanged.
  **Scope boundary — NOT done in this slice:** SMS path (still passes the Guid — needs `PhoneNumber` on `UserInfoDto`, a public contract every module consumes); `NotificationsController` channel-value validation at the boundary (pre-existing, and the validator (`SendNotificationCommandValidator.cs:15-18`) already rejects unknown channels with 422 — no gap); storing the resolved address on the `Notification` entity.
  **Follow-ups filed:** `NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE` (🟠 T2 — SMS half, blocked on a `UserInfoDto` contract change) · `NOTIFICATIONS-PRESENTATION-TESTS-PLACEMENT` (🔵 T4 — repo-wide test-project-layout question). Both → `backlog.md`.
  **Unresolved (open, not blocking this closure):** whether `ApplicationUser.PhoneNumber` is encrypted at rest like `Email` (matters only for the SMS follow-up) · whether any legitimate caller needs cross-tenant send *without* being SystemAdmin (the tenant-binding fix assumes not).

---

## 2026-07-17 (P1 stable-monolith — alerting EMAIL channel wired, coverage-gate half carried)

> Plan `plans/260717-1720-p1-stable-observability-hygiene-alert-email/plan.md`. Row `P1-STABLE-OBSERVABILITY-HYGIENE` split at slice-start: this slice covers the EMAIL channel only. The coverage-gate half is **NOT resolved** — carried to `P1-STABLE-CI-GATES-ENFORCE` (see `backlog.md`) because GitHub Actions is disabled by user choice; flipping the gate to enforcing now would ship a check nothing runs. Code-review REQUEST_CHANGES (H-1) → fixed in-slice → APPROVE_WITH_NITS (2 nits fixed in-slice). Closure criterion is local-verify, matching the other P1 rows this week (Actions disabled).

- **P1-STABLE-OBSERVABILITY-HYGIENE (EMAIL half)** — ✅ RESOLVED 2026-07-17 (🟡 T4, observability) — `AlertEvaluationJob`'s EMAIL case (`AlertEvaluationJob.cs:167-172`, previously `logger.LogWarning(...)` only, no send) now dispatches a real SMTP send when an `AlertRule` breaches with `NotifyChannel = "EMAIL"` and a non-empty `NotifyTarget`.
  **Seam:** new `IAlertEmailSender` (`src/shared/AqtCoreFW.Notification.Contracts/IAlertEmailSender.cs`, namespace `AqtCoreFW.SharedKernel.Contracts.Clients`) — a single-method, address-based contract (`SendAsync(toAddress, subject, body, cancellationToken)`), distinct from `INotificationModuleClient` on purpose. `AlertingRegistration`-injected `AlertEvaluationJob` (`src/shared/AqtCoreFW.Infrastructure/Alerting/AlertEvaluationJob.cs`) now takes `IAlertEmailSender` + `IConfiguration` + `IHostEnvironment` in its ctor. Implemented by new `AlertEmailSenderAdapter` (`src/modules/notifications/AqtCoreFW.Notifications.Infrastructure/Email/AlertEmailSenderAdapter.cs`), which delegates to the module's existing `IEmailSender`/`MailKitEmailSender`, registered in `NotificationsInfrastructureRegistration.cs:48`.
  **Killed approach (recorded so it is not re-attempted):** routing EMAIL through `INotificationModuleClient` was rejected on two independent counts — (1) `SendNotificationRequest` requires `RecipientUserId`(Guid)+`TenantId`(Guid), but `AlertRule` has only an address string and is system-wide/tenant-less; (2) that seam's own email path is separately broken (`SendNotificationCommandHandler.cs:67` passes a GUID string as the SMTP `to` address — see follow-up `NOTIFICATIONS-RECIPIENT-IS-GUID-NOT-ADDRESS` below).
  **Architecture decision:** `AqtCoreFW.Infrastructure` gained a ProjectReference to `AqtCoreFW.Notification.Contracts` (zero-ProjectReference leaf, no cycle — same pattern as the existing `OrgUnit.Contracts` edge), **not** to `Notifications.Application` — the repo has zero precedent for shared→module.Application and this slice does not start one, keeping the microservice-extraction path open.
  **Config posture:** `Email` section added to `appsettings.json` (dev default `localhost:1025`) and — after code-review H-1 caught the base file's dev default silently winning in prod — to `appsettings.Production.json` (empty-placeholder convention: `Host: ""`, `Password: ""`, matching `Encryption:FieldKey`/`Audit:HmacKey`). `Email:Password` deliberately **NOT** added to `ProductionSecretValidator.RequiredKeys` (would hard-fail boot for every prod deployment, including those that never use an EMAIL alert rule). Guard instead: outside Development, an unset `Email:Host` logs Critical (naming the Vault key) and returns without sending — no throw, since a misconfiguration is not transient and a rethrow would only duplicate the log every minute. A send that does throw (e.g. broker reachable but auth fails) is rethrown; the per-rule `try/catch` at `AlertEvaluationJob.cs:50-59` contains it so one bad rule cannot abort the evaluation cycle for others. `docs/ops/vault-key-rotation-runbook.md` updated with the 2 new SMTP Vault keys (was previously silent on SMTP — its only "email" row was the unrelated `email-pepper` login-hash salt).
  **Tests:** 13 new channel-switch tests (EMAIL→sends, EMAIL+empty-target→no-send/no-throw, WEBHOOK/LOG/unknown→no email send, Development-unconfigured-still-sends, Staging-unconfigured-guards, Staging-configured-sends, SMTP-throw→rethrown-but-cycle-survives, etc.) + 3 adapter/DI-registration tests (drives the REAL `AddAlertingInfrastructure`/`AddNotificationsInfrastructure` registration path, not hand-rolled DI). Mutation-proven non-vacuous: inverted `NotifyTarget` guard → 4 fail; commented-out DI registration → registration test fails; `false &&` into the `Email:Host` guard → 3 fail; guard-to-unconditional `!IsDevelopment()` → the Staging-configured test fails. Reviewer independently killed 4 more mutants.
  **Verify:** full solution `src/AqtCoreFW.slnx` build 0W/0E; architecture suite 180/180; `AqtCoreFW.Infrastructure.Extensions.Tests` 196 passed/1 skipped (pre-existing skip); `AqtCoreFW.Notifications.Infrastructure` 16/16.
  **Honest caveat (not pinned by test):** one reviewer mutant survived — making the config guard `throw` instead of `return` still keeps all 13 tests green. The return-vs-throw choice is reasoned (misconfiguration is not transient) but not test-pinned; low impact since the per-rule catch swallows either outcome, only the log shape differs.
  **Contract:** `AlertingRegistration` public signature unchanged (ctor DI only); `INotificationModuleClient`, `SendNotificationRequest`, `IEmailSender`, `AlertRule` public surface all untouched.
  **Scope boundary — NOT done in this slice:** coverage-gate enforcing flip (carried to `P1-STABLE-CI-GATES-ENFORCE`); `SendNotificationCommandHandler`'s GUID-as-address bug (filed as follow-up); `AlertRule.NotifyChannel` validation/enum (no evidence it bites today); any change to `MailKitEmailSender`/`SmtpOptions`/the notifications module's own paths.
  **Follow-ups filed:** `NOTIFICATIONS-RECIPIENT-IS-GUID-NOT-ADDRESS` (🟠 T2, bug — GUID-as-address on both Email and SMS paths of `INotificationModuleClient`) · `ALERTING-TESTRULE-REPORTS-202-ON-FAILED-DELIVERY` (🔵 T4 — admin test-rule button reports success even when delivery failed) · `ALERTING-LOGCRITICAL-HAS-NO-ALERT` (🟠 T2, ops — the new failure signals are log-only, nothing alerts on them). All 3 → `backlog.md`.

---

## 2026-07-17 (P1 stable-monolith — RabbitMQ readiness probe)

- **P1-STABLE-RABBITMQ-HEALTHCHECK** — ✅ RESOLVED 2026-07-17 (🟠 T2, observability) — `/health/ready` now covers the message broker. New probe in `AddGovHealthChecks` (`src/shared/AqtCoreFW.Infrastructure/HealthChecks/HealthCheckConfiguration.cs`) via `AspNetCore.HealthChecks.Rabbitmq` 9.0.0 (new dependency, version-aligned with the SqlServer/Redis siblings), `failureStatus: Degraded` (matching Redis, not SqlServer — the outbox + 5s/30s/5m retry absorb a transient outage and most HTTP traffic never touches the broker), tagged `["messaging","ready"]`.
  **Transport-gated (user decision 2026-07-17):** the probe registers ONLY when `Bus:Transport == "RabbitMQ"`. The bus defaults to InMemory (`MessagingRegistration.cs:47`), so an unconditional probe would report Degraded about a broker the system never connects to — trading a false-green for a false-red. Mirrors the existing `AI:Ollama:Enabled` gate for the qdrant/ollama probes in the same method.
  **Critical caught in code-review (C1 — connection leak):** the first cut passed `async _ => await factory.CreateConnectionAsync()` to `AddRabbitMQ` with a code comment asserting the package caches the resolved connection. The comment was an unverified assumption and the package's own shipped XML doc says the opposite — *"factory will be called each time the healthcheck route is requested"* — and the async overload never disposes what the factory returns. At the Helm `periodSeconds: 10` cadence that is ~8,640 leaked broker connections/pod/day, manifesting ONLY when the broker is healthy AND `Bus:Transport=RabbitMQ` (i.e. never in dev/CI, where the gate strips the probe) — the probe would eventually have caused the outage it was added to detect. Caching is automatic only on the **sync** `Func<IServiceProvider, IConnection>` overload, unusable here (`IConnection` is not in DI; RabbitMQ.Client 7.x dropped sync `CreateConnection`). Fixed with a new `ProbeConnectionSource` (`src/shared/AqtCoreFW.Infrastructure/HealthChecks/ProbeConnectionSource.cs`) — one cached `IConnection`, `SemaphoreSlim`-gated with a double-checked `IsOpen` re-check, disposing a dead connection before reconnecting. Takes `Func<Task<IConnection>>` rather than the concrete unmockable `ConnectionFactory`; that seam is what makes the cache testable.
  **N1 (same class of error, caught on re-review):** the first M1 fix passed `timeout: 3s` to `AddRabbitMQ` assuming it bounded the probe. It does not — the framework implements the registration timeout as a linked token + `CancelAfter`, and the package forwards that token only to `CreateChannelAsync`, never to the connection factory. A black-holing broker would hold the connect for `RequestedConnectionTimeout` — empirically **30s** by default (measured against RabbitMQ.Client 7.0.0, not taken on the reviewer's word) — while `/health/ready` reported at 3s. Fixed by setting `RequestedConnectionTimeout = 3s` on the factory so the connect layer is bounded too. No deadlock either way: queued probes are each capped by the framework.
  **Tests:** 9 new in `tests/unit/AqtCoreFW.Infrastructure.Extensions.Tests/HealthChecks/` — `RabbitMqHealthCheckRegistrationTests` (5 cases: RabbitMQ-registers / InMemory-strips / absent-key-defaults-to-strip / core-probes-unaffected ×2 transports) drives the REAL `AddGovHealthChecks` and reads back `HealthCheckServiceOptions.Registrations`; `ProbeConnectionSourceTests` (4 facts: first-connects / repeat-reuses / dropped-disposed-and-replaced / concurrent-collapse-to-one). Mutation-proven twice: inverting the gate turned 3 RED; disabling both cache branches turned the 2 cache tests RED. Both restored.
  **Verify:** full solution `src/AqtCoreFW.slnx` build 0W/0E; `AqtCoreFW.Infrastructure.Extensions.Tests` 183/183 pass (+4 over the 179 baseline), 1 pre-existing skip. `RabbitMQ.Client` resolves to 7.0.0 — the same version MassTransit.RabbitMQ 8.3.6 depends on, so no downgrade/conflict. Code-review REQUEST_CHANGES → APPROVE_WITH_NITS after fixes; report `plans/reports/from-code-reviewer-to-cook-rabbitmq-healthcheck-transport-gate-review-report.md`.
  **Contract:** none — `AddGovHealthChecks(IServiceCollection, IConfiguration)` signature unchanged; new probe is additive and gated off by default.
  **HONESTY / not closed:** the probe is **dormant** — every config in the repo sets `Bus:Transport=InMemory`, so nothing exercises it today and the broker-down→Degraded criterion has NO automated test (package behavior, needs a live broker; deliberately not mocked into a false-green). Follow-up filed: `RABBITMQ-HEALTHCHECK-LIVE-BROKER-VERIFY`.

---

## Backlog-optimize archival 2026-07-17 (4 fully-closed sections, no open sub-items)

> Archived whole during the 2026-07-17 post-merge backlog optimize pass (713→678 lines pre-header-slim). Each section below had zero open rows remaining — safe to move in full.

- **MSVC-CELL10-TEST-SUBSTRATE-SLICE2** — ✅ RESOLVED 2026-07-16 (test-only) — New `TransientFaultRetryConvergenceTests` (2 facts) drive the REAL MassTransit retry machinery against `WorkflowFinalizedFormSubmissionConsumer` — Test A transient-fail→retry-converges (effect `Received(2)`, no `Fault<T>`); Test B persistent-fail→exhausted-retry publishes a visible `Fault<WorkflowInstanceFinalizedIntegrationEvent>` (`Received(3)`, never a silent drop). Closes the false-green where the sibling unit test only proved the consumer throws via `Assert.ThrowsAsync` on a mocked `ConsumeContext`. `AqtCoreFW.Forms.Integration.Tests.csproj` gains a versionless `MassTransit` PackageReference. Build 0W/0E, 2/2 GREEN, mutation-proven, code-review DONE (0 Critical/High/Medium; 2 Low nits applied in-slice). Follow-up `MSVC-CELL10-RETRY-CONFIG-GUARD` (arch-guard) since RESOLVED 2026-07-16.
- **ARCH-R1-RAW-GUIDNEWGUID-SIGNING-FILES** — ✅ RESOLVED 2026-07-07 (🟡 T3, arch-guard) — threaded `IGuidGenerator` seam through 6 sites — 3 domain factory (`SignerCertEnrollment.Create`/`UserSignatureProfile.Create`/`SignerEnrollmentNonce.Issue`) take `IGuidGenerator gen` → `gen.NewIdRandom()` (kept V4-random deliberately, anti-timing-leak, NOT monotonic); 2 handler docId → `NewIdMonotonic()`; blob-suffix → `NewIdRandom()`; 4 prod-caller handlers inject generator + ~25 test call-sites updated (NSubstitute mock → hand-rolled `IGuidGenerator` double, nested factory-call in `.Returns()` corrupts last-call state). 35 files (10 prod + 25 test), build 0W/0E, `SourceLevelRulesTests` 12/12, Identity.Integration signing 174/174 + unit suites green, code-review APPROVE_WITH_NITS. Behavior-preserving (0 logic change). Fixed 6 raw `Guid.NewGuid()` call-sites in allowlisted modules (`SignerCertEnrollment.cs:146`, `SignerEnrollmentNonce.cs:64`, `UserSignatureProfile.cs:108`, `StageFileForSigningCommandHandler.cs:87`, `GenerateAndInitiateSubmissionSigningCommandHandler.cs:91`, `CompleteDocFlowUploadCommandHandler.cs:52`) surfaced by Wave-0 full-repo verify (07-07), not a Sóng-0 regression.
- **SIGN-PROFILE-STAMP-TOGGLE-INSERT-FALSE-REGRESSION-TEST** — ✅ RESOLVED 2026-07-04 (T4 🔵, BE test-coverage) — shipped 2-fact Testcontainers round-trip guard (`src/tests/AqtCoreFW.Tests.Integration/Identity/EfStoreDefaultSentinelInsertPathTests.cs`): verifies `UserSignatureProfile.Create(showSerial:false, showIssuer:false)` persists false (EF INSERT through DB defaults); bonus MfaPolicy.GraceDays=0 case. Tests 2/2 green, gates 0W/0E.
- **DB-BOOL-SENTINEL-AUDIT** — ✅ RESOLVED 2026-07-04 (T4 🔵, BE hygiene) — audit CLEAN for bool: all DB-default-true bools already have correct `HasSentinel`; IsActive deliberate-documented; IsDeleted/IsRevoked backwards-but-harmless. Audit extended to non-zero int defaults → found + fixed one reachable bug: `MfaPolicy.GraceDays` (DB default 7, no sentinel) dropped explicit GraceDays=0 on runtime INSERT (`CreateMfaPolicyCommand`) via `MfaPolicyConfiguration.cs` + `HasSentinel(-1)`. Seeded rows unaffected (HasData emits literals). Gates 0W/0E.
- **BE-NUGET-NCALCSYNC-NU1902** — ✅ CLOSED-STALE 2026-06-23 (SHIPPED by `e6c22a7c`, INVARIANT 260522-A audit) — `NCalcSync` pinned `6.1.1` (`src/Directory.Packages.props:20`), clearing advisory `GHSA-3w5p-95mh-gq75`; patched `NCalc.Core` pulled transitively. v5→v6 verified non-breaking (full build 0E, Forms.Infra 33/33 + Forms.App 317/317 green).

---

## 2026-07-16 (P1 stable-launch blockers — backup automation + CI smoke-test gate, both RESOLVED)

> Both from the 2026-07-16 Stable-Monolith P1 operational-readiness audit (`plans/reports/audit-260716-1805-stable-monolith-p1-operational-readiness-report.md`), the only 2 rows tagged 🔴 T1 (launch-blocker). Code-reviewed together, 0 Critical/0 High (1 Medium + 1 Low fixed in-slice). Full build 0W/0E.

- **P1-STABLE-BACKUP-AUTOMATION** — ✅ RESOLVED 2026-07-16, MERGED TO MAIN 2026-07-17 (PR #121, merge commit `fc097de5`) (🔴 T1, data-loss blast radius) — new `DatabaseBackupService.RunScheduledBackupAsync()` (`src/shared/AqtCoreFW.Infrastructure/Backup/DatabaseBackupService.cs`) creates a `BackupRecord` then reuses the existing `ExecuteBackupAsync` (DRY, no duplicated backup logic) and prunes retention afterward. New recurring Hangfire job `scheduled-backup` (daily `0 2 * * *`) registered in `HangfireRegistration.RegisterRecurringJobs()`. New pure function `BackupRetentionPruner.SelectExpired` (`src/shared/AqtCoreFW.Infrastructure/Backup/BackupRetentionPruner.cs`) + 6 boundary unit tests (all green). New config section `Backup:{Directory=/backups, CronExpression=0 2 * * *, RetentionDays=30}` added to `appsettings.json` + `appsettings.Production.json`. New runbook `docs/ops/backup-restore-runbook.md`.
  **Review fixes in-slice:** (Medium) clamp `RetentionDays<1` — skip prune entirely rather than deleting everything (anti self-destruct on misconfig); (Low) broadened the file-delete catch to `IOException | UnauthorizedAccessException | PathTooLongException`.
  **Scope:** effort S — backup execution logic already existed (manual `BackupAdminController` trigger); only the automation (scheduling + retention pruning) was missing. NO behavior change to existing NĐ53 cold-storage archive jobs (`archive-audit`/`archive-cases`) or the manual `BackupAdminController` trigger.
  **Verify:** full build 0W/0E; `BackupRetentionPruner` unit tests 6/6 GREEN. Code-review APPROVE_WITH_NITS (0 Critical/High; 1 Medium + 1 Low fixed in-slice).
  **Contract:** none — new job + new config keys, additive only.

- **P1-STABLE-INTEGRATION-TEST-CI-GATE** — ✅ RESOLVED 2026-07-16, MERGED TO MAIN 2026-07-17 (PR #121, merge commit `fc097de5`) (🔴 T1) — new `smoke-tests` job in `.github/workflows/ci.yml`: SQL Server 2022 service-container (mapped `1433:1433`) + `TEST_USE_LOCAL_SQL=true` + Docker available (5/7 smoke test classes self-spin Testcontainers, 2/7 use the service-container); runs `dotnet test --filter Category=Smoke` against `AqtCoreFW.Startup.SmokeTests`; SQL readiness gated via `docker exec` into the service container's `sqlcmd`. New runbook `docs/ops/ci-smoke-gate-runbook.md`.
  **NOT flipped:** the full 197-test integration suite stays `if:false` (local-dev-only; comment in `ci.yml` updated to point at the new smoke-tests job as the CI-side fresh-deploy check). No other CI gate (SCA/secret-scan/Semgrep/destructive-migration) touched.
  **Verify:** full build 0W/0E. **CI green NOT observed** — GitHub Actions stays disabled by user choice (Free-tier); the job is merged to main but has not run. Local-verify (smoke suite green) is the closure bar, not CI-observed.
  **Contract:** none — CI workflow addition only, no source/test-project code change beyond the existing `AqtCoreFW.Startup.SmokeTests` category filter.

**Follow-up filed:** `BACKUP-RESTORE-DRILL-TEMP-CLEANUP` (🔵 T4, low, pre-existing) — `ExecuteRestoreDrillAsync` leaves drill temp files (`_drill_*.bak`/`.mdf`/`.ldf`) uncleaned in `Backup:Directory`, and `RestoreDrill`-type records are excluded from `BackupRetentionPruner.SelectExpired` → `/backups` volume accumulates drill artifacts over time. Surfaced by code-review, not fixed this slice (out of scope).

## 2026-07-17 (Smoke-gate hardening — 3 bugs behind one "connection refused" symptom, incl. 1 real AuthServer prod DI regression)

> Branch `feat/p1-stable-backup-and-ci-smoke`, MERGED TO MAIN 2026-07-17 (PR #121, merge commit `fc097de5`). Started as a follow-up investigation into intermittent smoke-suite failures; deep-dived past the "connection refused" symptom and found 3 distinct root causes. Code-review APPROVE 0 Critical/0 High. Smoke suite 11/11 GREEN ×3 local runs (deterministic re-run, not a one-off).

- **SMOKE-FIXTURE-ENV-VAR-RACE-HARDENING** — ✅ RESOLVED 2026-07-17 (test-infra, 3 bugs) —
  1. **Fixture env-var race:** 3 smoke host fixtures (`ApiHostFixture`, `AuthServerHostFixture`, `SeederSmokeFixture` — all under `tests/integration/AqtCoreFW.Startup.SmokeTests/`) mutated the process-global `ConnectionStrings__Default` env var to point each host at its own Testcontainer; concurrent/sequential multi-host boot in the same process raced writes to that global. Fixed via new `UseTestConnectionStrings` extension (`tests/shared/AqtCoreFW.Tests.SharedFixtures/TestConnectionStringHostBuilderExtensions.cs`) that sets the connection string with `builder.UseSetting(...)` (per-host config, no process-global mutation) instead. `SmokeTestCollection.cs` updated for the new fixture contract.
  2. **Hangfire static storage frozen across hosts:** static `RecurringJob.AddOrUpdate` (called from the boot-path recurring-job registration block) binds `JobStorage.Current` once per process — the first host to boot in the multi-host smoke suite wins, so a later-booted host's registration silently targets the first host's (already-disposed) Testcontainer. Fixed by gating that registration block behind a new config flag `Hangfire:RegisterRecurringJobs` (`src/host/AqtCoreFW.Api/Program.cs`) — defaults `true` (prod/dev unchanged), the 2 Api-booting smoke fixtures (`ApiHostFixture`, `SeederSmokeFixture`) set it `false`.
  3. **AuthServer DI regression — real prod bug, not test-infra:** commit `fa0faff2` (2026-07-16, MSVC cell #7 slice-2) added `AuditEntryPersister` as an `AuditService` ctor dependency, but AuthServer's manual audit registration block (`src/host/AqtCoreFW.AuthServer/Program.cs` — AuthServer skips `AddInfrastructure()`/`AddAuditService()` and hand-registers Audit deps, per the recurring pattern documented in `docs/dependency-map.md` "AuthServer Host DI Registration") was never updated to match → DI activation failure on the first audited login path (`AuthorizationController`/`ExternalLoginController`/`MfaChallengeController`), a 4th occurrence of the same failure class (see dependency-map history: v2.133p3, v2.137-P7, Slice 1B-2). Masked until now because the smoke gate was already red from bugs #1/#2, so it never got far enough to exercise a real login. Fixed: added `builder.Services.AddScoped<AuditEntryPersister>();` to AuthServer's manual audit registration block.
  **New tests:** `FixtureConnectionStringIsolationTests.cs` (`tests/integration/AqtCoreFW.Startup.SmokeTests/`) — hostile-env invariant test (asserts fixture boot is unaffected by a pre-set conflicting `ConnectionStrings__Default`) + a static-source guard against reintroducing global env-var mutation in the fixture files.
  **Verify:** smoke suite 11/11 GREEN, run 3× locally (deterministic, not a Testcontainers-cold-start fluke). Code-review APPROVE, 0 Critical/0 High.
  **CI note:** the `smoke-tests` CI job (from `P1-STABLE-INTEGRATION-TEST-CI-GATE` above) is NOT the closure criterion here — user has GitHub Actions disabled by choice (Free-tier cost, solo dev), so CI-observed green is not expected or required; local-verify (11/11 ×3) is the bar.
  **Contract:** none — test-infra fix + 1 new config flag (default preserves prod behavior) + 1 missing prod DI registration restored to parity with its own dependency graph. No API/DTO/schema change.

---

## 2026-07-16 (Dev-DB duplicate-public-Code migration blocker — triaged, non-destructive fix applied)

> `/aqt-runapp` surfaced the blocker; triaged source-proven (no code change) and fixed by data-repair on the local dev DB. Commit `99fc5cb0` records the resolution in `backlog.md`.

- **FORMS-DEV-DB-DUPLICATE-PUBLIC-CODE-MIGRATION-BLOCK** — ✅ RESOLVED 2026-07-16 (dev-infra; commit `99fc5cb0`) — API host startup migration `UX_FormTemplate_PublicCode` (public-forms global-uniqueness arc) failed its intentional pre-flight guard (`THROW 50000`): dev DB had public Code `demo-intake-form` (`IsPublic=1, IsDeleted=0`) duplicated across **2 tenants** → API could not start (`AqtCoreFW.Api.Program.cs:382 MigrateAsync`). Guard worked as designed (not a regression); Auth(5002)+FE(5173) started fine, only API blocked.
  **Triage verdict (source-proven, NO code change):** NOT a seeder-bug. `DemoFormTemplateSeeder` creates `demo-intake-form` for **both** tenants (`TenantSeedProfiles.All = [BoTaiChinh, BoNoiVu]`) as **Active, `IsPublic=0`** (DB default; seeder never opts public). The duplicate-public state was created by the behavior-preserving backfill migration `20260709033410_AddFormTemplateIsPublic` (`UPDATE forms.FormTemplates SET IsPublic=1 WHERE Status='Active' AND IsDeleted=0`) acting on the pre-existing Active demo templates in this long-lived dev DB — i.e. **legacy backfill data, not a live seeder defect**. `--reseed` would also be SAFE (fresh DB backfill no-ops on 0 rows → filtered unique index builds clean → seeder inserts `IsPublic=0` rows the filtered index doesn't cover), but demote was chosen as non-destructive.
  **Resolution:** ran `UPDATE forms.FormTemplates SET IsPublic=0 WHERE Code='demo-intake-form' AND IsPublic=1` on dev DB (`localhost,1433/AqtCoreFW`) → demoted both tenant rows (Id 1 BTC `…B7C1`, Id 2 BNV `…B7C2`) to private (Active retained, seeder-intended state restored). Post-fix verify: 0 duplicate public Codes; index `UX_FormTemplate_PublicCode` still absent + migration still pending in `forms.__EFMigrationsHistory` → next API boot applies it clean. Final confirmation = re-run `/aqt-runapp` (API boots past `Program.cs:382 MigrateAsync`). Seeder + migration guard both correct as-designed — no source change shipped.
  **Contract:** none — data-repair only, no source/schema/migration change.

---

## 2026-07-16 (Retry-config integrity arch-guard — completeness-matrix cell #10, last unblocked item, build-now CODE track now exhausted)

> Follow-up of `MSVC-CELL10-TEST-SUBSTRATE-SLICE2` (below), filed 🔵 T4 "ready/unblocked, filed-not-built". Now built — the last cheap unblocked completeness-matrix cell #10 item. With this shipped, cell #10 is FULLY monolith-exhausted; combined with hard cells #5/#6 (extraction-gated), cell #7 follow-ups (compliance/auditor-blocked), and cell #3's provider half (extraction-gated), the completeness-matrix build-now CODE track now has NO unblocked items remaining — the sole remaining microservices action is FU-A (GOV auditor question, non-code, user-owned).

- **MSVC-CELL10-RETRY-CONFIG-GUARD** — ✅ RESOLVED 2026-07-16 (🔵 T4, microservices / resilience / architecture-test; test-only; commit pending) — new `MessageRetryConfigGuardTests` (`tests/architecture/AqtCoreFW.Architecture.Tests/`), a `[Theory]` with 2 `InlineData` cases (`"UsingInMemory"`, `"UsingRabbitMq"`), reads the committed `src/shared/AqtCoreFW.Infrastructure/Messaging/MessagingRegistration.cs` source via `FindRepoRoot()` and asserts each transport-configuration lambda body (extracted brace-depth-aware) contains `UseMessageRetry`. Mirrors the existing `MarsConnectionStringGuardTests` static-source pattern.
  **Why:** a regression removing `UseMessageRetry` from either transport silently converts every throws-to-retry consumer (e.g. `WorkflowFinalizedFormSubmissionConsumer`, whose retry-convergence is proven by `TransientFaultRetryConvergenceTests`) into immediate dead-letter on the first fault — permanent message loss, previously with no test to catch it. RabbitMQ is prod-only (behaviourally untestable without a live broker), so a static-source guard is the ONLY way to cover both transports.
  **Files:** new — `MessageRetryConfigGuardTests.cs` (1 file). NO production code change — test-only, zero source touched.
  **Verify:** build 0W/0E; new tests 2/2 GREEN; full `AqtCoreFW.Architecture.Tests` 180/180 (no regression); mutation-proven non-vacuous (deleting `UseMessageRetry` from the InMemory block made only the `UsingInMemory` case fail, `UsingRabbitMq` passed — proving non-vacuity AND that the parser discriminates the two blocks; reverted, prod pristine). Code-review APPROVE_WITH_NITS (0 Critical/High/Medium; sole action = a documentation note about the raw-text-match limitation, applied in-slice).
  **Contract:** none — test-project code only.
  **Plan:** none — T4 single-file mirror-test, no `plans/` dir entry (inline plan).
  **Strategic milestone:** this was the last cheap unblocked completeness-matrix cell #10 item. Cell #10 is now FULLY monolith-exhausted — 3/5 viable fault dims proven (duplication/timeout/partition) + this config-integrity guard; reordering retired-as-theater, cross-service-dedup extraction-gated, chaos low-ROI. Combined with hard cells #5 (saga)/#6 (authz-remote) both extraction-gated, cell #7 follow-ups compliance/auditor-blocked, and cell #3's provider half extraction-gated → **the completeness-matrix build-now CODE track now has NO unblocked items remaining.** The next microservices action is unambiguously non-code: **FU-A** (the GOV auditor question at `docs/microservices-compliance-isolation-auditor-questions.md`), which gates all physical extraction. Feature-dev/polish remains the only other code track.

---

## 2026-07-16 (Resilience test-substrate slice-3 — completeness-matrix cell #10, partition/broker-outage durability)

> Third code-able slice of completeness-matrix cell #10 ("Resilience", roadmap §4), semantic complement of `MSVC-CELL10-TEST-SUBSTRATE-SLICE2` (timeout/transient-retry) and `MSVC-CELL10-TEST-SUBSTRATE-SLICE1` (duplication, both below). Extraction-independent, monolith-side, test-only, reversible. Proves fault-dimension #3 (partition/broker-outage) against the real durable transactional-outbox relay. **HONESTY:** reordering is NOT built this slice — RETIRED AS THEATER this session (grounding proved `AuditLogAppendedConsumer` is a NOOP, no consumer holds cross-message ordered state); cross-service dedup + chaos remain extraction-gated/low-ROI.

- **MSVC-CELL10-BROKER-OUTAGE-DURABILITY** — ✅ RESOLVED 2026-07-16 (🟡 P2, microservices / resilience; test-only; commit pending) — `OutboxBrokerOutageDurabilityTests` (2 xUnit facts) drive the REAL durable transactional-outbox relay (`OutboxDispatcher`, prod-driven by the `OutboxProcessor` hosted service) against a fault-injecting `IBus` substitute (throws on `Publish` while an outage flag is set, swapped via `ConfigureTestServices`; the dispatcher itself is resolved from the REAL DI registration `AddScoped<OutboxDispatcher>()`, never hand-newed). **Test A (transient outage → converges, zero loss):** the row is RETAINED as a retry (`ProcessedAtUtc` null, `RetryCount=1`) while the outage flag is set; after recovery the next dispatch cycle converges (`Dispatched`, `ProcessedAtUtc` set). **Test B (persistent outage → visible dead-letter):** the outage flag stays set past `DeadLetterAfterRetries` (=2) — the row lands VISIBLY in `messaging.dead_letters` (`MessageType` = `AuditLogAppendedIntegrationEvent` FullName, `OriginalQueue` = `outbox:audit`) and the source row is marked processed — never a silent drop.
  **Review-fixed in-slice (M1, Medium):** `ClaimBatchAsync TOP(50)` starvation once more than 50 pending audit rows accumulate (a real risk under a sustained outage) — fixed in-slice via `OutboxOptions.BatchSize=1000` set through the test's `PostConfigure` seam. **L1 (Low, documented, not fixed):** `DispatchBatchAsync`'s blast radius spans all schemas, not just audit — doc-noted in the test class's XML comment.
  **RLS posture:** the outbox path is tenant-agnostic (dedicated ADO connection, no SESSION_CONTEXT, no RLS policy on `audit.OutboxMessages` or `messaging.dead_letters`) — gate-tier only; the sa/db_owner false-green concern (memory `feedback-sa-dbowner-false-greens-rls-assertions`) does not apply here.
  **Files (test-only, no src change):** new — `OutboxBrokerOutageDurabilityTests.cs` (`tests/integration/AqtCoreFW.Audit.Integration.Tests/`, 2 facts); modified — `AqtCoreFW.Audit.Integration.Tests.csproj` (+`<PackageReference Include="MassTransit" />`, versionless, central-pinned 8.3.6 in `tests/Directory.Packages.props`, mirrors `Forms.Integration.Tests`).
  **Verify:** build 0W/0E. New tests 2/2 GREEN. Full `AqtCoreFW.Audit.Integration.Tests` 84/84 (82 baseline + 2) — zero sibling regression. Mutation-proven non-vacuous: Mut-1 (retain-check removed) ⇒ both tests RED; Mut-2 (dead-letter write skipped) ⇒ Test B RED; both reverted ⇒ 2/2 GREEN. Code-review APPROVE_WITH_NITS (0 Critical/High; M1 fixed in-slice, L1 documented).
  **Contract:** none — test-project code only; no src/DTO/endpoint/cross-module client/schema/env change.
  **Strategic note (cell #10 now largely monolith-exhausted):** with duplication (slice-1) + timeout/transient-retry (slice-2) + partition/broker-outage (this slice), cell #10 has 3/5 viable-in-monolith fault dimensions covered. **Reordering RETIRED AS THEATER this session** — grounding proved `AuditLogAppendedConsumer` is a NOOP and no consumer holds cross-message ordered state, so a reordering test is extraction-dependent (needs an activated projection consumer). Cross-service dedup stays extraction-gated; chaos stays low-ROI/broad. The last cheap unblocked cell #10 item is `MSVC-CELL10-RETRY-CONFIG-GUARD` (🔵 T4, filed by slice-2, not yet built). The real remaining microservices blocker overall is FU-A (the GOV auditor question, non-code, user-owned).
  **Plan:** `plans/260716-1145-msvc-cell10-resilience-slice3-broker-outage-durability/` (plan.md + phase-01, status done).

---

## 2026-07-16 (Client-side HTTP transport for cross-module OrgUnit vertical — completeness-matrix cell #3 slice-1B)

> Second code-able slice of completeness-matrix cell #3 ("Sync inter-service + auth propagation", roadmap §4), FU of `MSVC-CELL3-CONTRACT-SERIALIZATION-GUARD` (slice-1A, below). First `Remote*Client` vertical — the client half only. Establishes the reusable HTTP-client template the other 17 `I*Client` seams copy at extraction time. **HONESTY:** NO provider endpoint, NO service-to-service auth this slice (both deferred to physical extraction, follow-up filed); default DI stays InProcess, so ZERO runtime behavior change ships with this slice.

- **MSVC-CELL3-SLICE1B-REMOTE-CLIENT-VERTICAL** — ✅ RESOLVED 2026-07-16 (🟡 T3, microservices / architecture / cross-module-client; client-side only; commit pending) — new `HttpOrgUnitModuleClient` (`src/shared/AqtCoreFW.Infrastructure/Clients/`) implements all 6 `IOrgUnitModuleClient` methods over HTTP/JSON — mirrors `InProcessOrgUnitModuleClient`'s input-normalization + `MaxBatchSize` cap (enforced client-side before the HTTP call, so the documented `ArgumentException` still fires on every transport) + empty-input short-circuits; unlike the in-process client it does NOT chunk the batch (a single wire call, provider owns any chunking it needs); `PostAsync` tolerates an empty/whitespace response body → returns default rather than throwing. New `OrgUnitClientTransport` enum {InProcess, Http} + `OrgUnitModuleClientRegistration.AddHttpOrgUnitModuleClient` (wires a typed `HttpClient` + activates the previously-dormant `TenantPropagationHandler`; fail-fast `InvalidOperationException` when `OrgUnitModuleClient:BaseAddress` is missing under Http mode). `OrganizationInfrastructureRegistration` replaces its hardcoded `AddScoped<IOrgUnitModuleClient, InProcessOrgUnitModuleClient>()` with an internal `AddOrgUnitModuleClient(services, configuration)` toggle reading new config `OrgUnitModuleClient:Transport` (absent/unparseable → InProcess). **Default DI stays InProcess = zero runtime behavior change.**
  **Files:** new — `HttpOrgUnitModuleClient.cs`, `OrgUnitClientTransport.cs`, `OrgUnitModuleClientRegistration.cs` (`src/shared/AqtCoreFW.Infrastructure/Clients/`), `HttpOrgUnitModuleClientTests.cs` (20 facts, `tests/integration/AqtCoreFW.Organization.Integration.Tests/Clients/`, driven through the real registration path against an in-memory TestServer stub). Modified — `AqtCoreFW.Infrastructure.csproj` (+1 `ProjectReference` to `AqtCoreFW.OrgUnit.Contracts`, a leaf edge, 0 cycle), `OrganizationInfrastructureRegistration.cs` (transport toggle).
  **Config keys introduced (both optional, absent → InProcess, no behavior change):** `OrgUnitModuleClient:Transport` (`InProcess`|`Http`), `OrgUnitModuleClient:BaseAddress` (required only in `Http` mode).
  **Verify:** build 0W/0E; new tests 20/20; arch guards `CrossModuleClientContractSerializationSafetyTests` + `ModuleIsolationTests` 16/16 (18-contract census unchanged); code-review APPROVE_WITH_NITS (0 Critical/High; M1+L3 fixed in-slice).
  **Contract:** none — `IOrgUnitModuleClient` interface unchanged; purely additive implementation. NO provider endpoint, NO service-to-service auth (`NoOpServiceIdentityProvider` stays a placeholder) — both deliberately deferred to physical extraction.
  **Follow-up filed:** `MSVC-CELL3-SLICE1B-PROVIDER-ENDPOINT` (🟡 T3, blocked-on physical extraction / FU-A auditor) — build the provider-side internal HTTP endpoint (6 POST endpoints under `/internal/org-units/*`) + service-to-service auth (replace `NoOpServiceIdentityProvider` with mTLS/token). Two wire-contract invariants from this slice's code-review the provider MUST honor: (L1) `/internal/org-units/user-codes` MUST return canonical UPPER-trimmed codes (the client wraps the response in an Ordinal case-sensitive `HashSet`; a raw-code provider would diverge from the monolith's `UPPER(LTRIM(RTRIM(...)))` canonicalization); (L2) the client serializes requests PascalCase and reads responses case-insensitively — document in the provider wire-contract, since a strict case-sensitive binder would reject requests.
  **Remaining (later, extraction-gated):** provider endpoint, service-auth, and activating `Http` transport in any environment.

---

## 2026-07-16 (Resilience test-substrate slice-2 — completeness-matrix cell #10, transient-fault retry convergence)

> Second code-able slice of completeness-matrix cell #10 ("Resilience", roadmap §4), FU of `MSVC-CELL10-TEST-SUBSTRATE-SLICE1` (below, duplication dimension). Extraction-independent, monolith-side, test-only, reversible. Proves fault-dimension #2 (timeout/transient-retry) — the semantic complement of slice-1's duplication test — against the effect-idempotent throws-to-retry consumer, closing the unit-`ThrowsAsync`≠real-retry false-green. **HONESTY:** partition / reordering / chaos + cross-service dedup are NOT built this slice (later, some extraction-dependent).

- **MSVC-CELL10-TEST-SUBSTRATE-SLICE2** — ✅ RESOLVED 2026-07-16 (🟡 P2, microservices / resilience; test-only; commit pending) — `TransientFaultRetryConvergenceTests` (2 xUnit facts) drives the REAL MassTransit retry machinery (`AddMassTransitTestHarness` + `UsingInMemory` wiring `UseMessageRetry` + `ConfigureEndpoints`, mirroring prod `MessagingRegistration`, never hand-invoked `Consume`) against the effect-idempotent throws-to-retry consumer `WorkflowFinalizedFormSubmissionConsumer` — target chosen because its header contract (deliberately NO `IProcessedMessageStore`; an infra failure is allowed to propagate so `UseMessageRetry` re-applies until it succeeds; exhausted retries dead-letter visibly) is the exact semantic complement of slice-1's claim-first duplication test (memory `feedback-claim-first-dedup-only-for-at-most-once-consumers`). **Test A (retry converges):** test-local fast retry policy (`r.Immediate(3)`); `IFormSubmissionStatusClient.ApproveSubmissionAsync` mocked to throw a transient `TimeoutException` on the first attempt then succeed; publish one `WorkflowInstanceFinalizedIntegrationEvent`. Asserts `Received(2)` (1 fail + 1 success), NO `Fault<WorkflowInstanceFinalizedIntegrationEvent>` published, `RejectSubmissionAsync` `DidNotReceive`. **Test B (exhausted retry dead-letters visibly):** `r.Immediate(2)`; mock throws always; publish once. Asserts `Received(3)` (initial + 2 retries) AND a `Fault<WorkflowInstanceFinalizedIntegrationEvent>` IS published (matched on `EntityId`/`MessageId`) — never a silent Pending strand. **Closes a real false-green:** the sibling unit test `WorkflowFinalizedFormSubmissionConsumerTests` proves the consumer *throws* via `Assert.ThrowsAsync` against a mocked `ConsumeContext`, but never drives real redelivery/dead-letter — the integration-level durability guarantee (retry converges; exhausted → visible fault) was unproven before this slice.
  **Verify:** build 0W/0E (full Forms.Integration project). 2/2 GREEN. **Mutation-proven non-vacuous:** Test A `== 1`→`>= 1` (always-throw) ⇒ RED; Test B `ThrowsAsync`→`Returns(CompletedTask)` ⇒ RED; both reverted ⇒ 2/2 GREEN. Code-review DONE (0 Critical/High/Medium) — confirmed race-free exact-count assertions, correct effect-idempotent semantics (no ledger assertion — right for this ledger-less consumer), faithful real-retry harness, `MassTransit` resolves 8.3.6 (zero version movement, no Testcontainer blast radius), zero prod change. Applied 2 Low nits in-slice: Test B `Immediate(2)` prod-count clarifying comment; `WaitUntilAsync` now throws with context on timeout (DX). Low #1 (harness omits outbox/tenant-filter — statusClient mocked, no ambient-tenant DB dependency, mirrors slice-1's own scope choice) accepted as valid scope.
  **Files (test-only, no src change):** new — `TransientFaultRetryConvergenceTests.cs` (`tests/integration/AqtCoreFW.Forms.Integration.Tests/EventHandlers`, 2 facts + `BuildHarness` helper, mirrors slice-1's `MessageDuplicationIdempotencyTests` structure); modified — `AqtCoreFW.Forms.Integration.Tests.csproj` (+`<PackageReference Include="MassTransit" />`, versionless via central `Directory.Packages.props`, already pinned by Notifications.Integration).
  **Contract:** none — test-project code only; no src/DTO/endpoint/cross-module client/schema/env change. `WorkflowFinalizedFormSubmissionConsumer`, `IFormSubmissionStatusClient`, `WorkflowInstanceFinalizedIntegrationEvent` all unchanged.
  **Follow-up filed:** `MSVC-CELL10-RETRY-CONFIG-GUARD` (🔵 T4, arch-guard) — assert `MessagingRegistration.cs` keeps `UseMessageRetry` (a regression removing it silently converts every throws-to-retry consumer to immediate dead-letter). User chose the 2-harness-test scope only this round; not built.
  **Regression scope (residual):** csproj change is additive-only (a package the project's existing Testcontainer tests don't reference); full-project build green is the regression evidence — a full Testcontainer suite run was NOT executed (Docker cold-start cost + warm-SQL false-red risk, ~0 added assurance for an unreferenced package).
  **Remaining (later slices):** partition / reordering / chaos fault dimensions + cross-service dedup (extraction-dependent).

---

## 2026-07-16 (Resilience test-substrate slice-1 — completeness-matrix cell #10, hosted-service boot-guard + duplication fault-dimension)

> First code-able slice of completeness-matrix cell #10 ("Resilience", roadmap §4). Extraction-independent, monolith-side, test-only, reversible. Closes the WebAppFixture hosted-service false-green (3× recurring) and seeds the first of four distributed-fault test dimensions (duplication). **HONESTY:** partition / reordering / timeout / chaos + cross-service dedup are NOT built this slice (later, some extraction-dependent).

- **MSVC-CELL10-TEST-SUBSTRATE-SLICE1** — ✅ RESOLVED 2026-07-16 (🟡 P2, microservices / resilience; test-only; commit pending) — **Part A (boot-guard):** `HostedServiceTestHelper.DriveHostedServiceAsync<T>` resolves a single `IHostedService` from the REAL app DI container (never hand-newed) and drives `StartAsync`/`StopAsync`, so a boot-time hosted-service regression (missing DI registration, throwing `StartAsync`) surfaces in an integration test instead of prod startup. The strip predicate the two fixtures share was DRY-extracted to `HostedServiceStripFilter.ShouldRetain` (previously two byte-identical loops in `WebAppFixture` + `SeederSmokeFixture`), plus a `WebAppFixture.HostedServicesToRetain` opt-in for a test to keep a specific service booting. **Part B (fault-dimension #1, duplication):** `MessageDuplicationIdempotencyTests` publishes the identical `FormSubmissionAcknowledged` event twice (same `SubmissionId`) through the real MassTransit harness and asserts the effect (`INotificationModuleClient.SendAsync`) dispatches EXACTLY once via the claim-first `IProcessedMessageStore` ledger, both deliveries consumed (not faulted). Targets a claim-first at-most-once consumer (`FormSubmissionAcknowledgedConsumer`), NOT the effect-idempotent `WorkflowFinalizedFormSubmissionConsumer`. Non-vacuous: a dedup-disabled mutation flips it RED (`Received 2`), restored → GREEN.
  **Review-driven fix (HIGH, folded in-slice):** the first cut of `ShouldRetain` retained ANY factory-lambda registration, which newly retained Hangfire's `BackgroundJobServerHostedService` (factory-registered → `ImplementationType==null`) — booting the job server in every WebAppFixture/SeederSmokeFixture host, where it would poll SQL and execute enqueued jobs (virus scan, webhooks, audit retry) mid-test, racing assertions across the ~150-file blast radius (worse in `SeederSmokeFixture`'s Development env, which also runs `RegisterRecurringJobs`). A single-class blast-radius smoke could not detect it (a started server doesn't hang boot). Fixed: restore the `ImplementationFactory.Method.ReturnType` resolution and fail-safe-retain ONLY the interface-pinned-collapse shape (`AddSingleton<IHostedService>(sp => ...)`, return type == `IHostedService`); concrete factory types (Hangfire, `FeatureFlagSubscriber`, `FormMaterializationService`) strip precisely as before this predicate was extracted. Added proof #4 (strip-branch regression guard: a Hangfire-shaped concrete factory non-seeder → `ShouldRetain == false`). Open-Q #1 answered: no production `IIntegrationTestSeeder` registers via an interface-pinned factory, so the fail-safe branch has no live trigger today (defensive).
  **Verify:** build 0W/0E (Identity.Integration + Notifications.Integration + Startup.SmokeTests). Part A 4/4 (throwing-fake propagation · `AbacCacheSubscriber` DI smoke through real host · interface-pinned factory-seeder RETAINED · concrete factory non-seeder STRIPPED). Part B 1/1 (mutation-proven). Blast-radius: prior-green `IdentityModuleClientTenantUserIdsTests` 3/3 with the predicate active; Part A host boot dropped 55s→44s after the fix re-stripped Hangfire + FeatureFlagSubscriber. Code-review DONE_WITH_CONCERNS (1 HIGH regression + 2 MEDIUM comment/repo-rule) → all resolved.
  **Files (test-only, no src change):** new — `HostedServiceStripFilter.cs`, `HostedServiceTestHelper.cs` (`tests/shared/AqtCoreFW.Tests.SharedFixtures`), `HostedServiceTestHelperIntegrationTests.cs` (`tests/integration/AqtCoreFW.Identity.Integration.Tests/HostedServices`, 4 proofs), `MessageDuplicationIdempotencyTests.cs` (`tests/integration/AqtCoreFW.Notifications.Integration.Tests/EventHandlers`); modified — `WebAppFixture.cs` (opt-in + predicate swap), `SeederSmokeFixture.cs` (predicate swap).
  **Contract:** none — test-project code only; no src / DTO / endpoint / cross-module client / schema / env change.
  **Remaining (later slices):** partition / reordering / timeout / chaos fault dimensions + cross-service dedup (extraction-dependent).

---

## 2026-07-15 (Durable audit-ingestion retry — completeness-matrix cell #7 slice-2, closes L1 ingestion-loss)

> Second code-able slice of completeness-matrix cell #7 ("Audit-chain under distribution", roadmap §4), FU of `MSVC-CELL7-TAIL-WATERMARK` (below). Closes L1 (ingestion-loss, the swallow in `AuditBehavior.cs`). **HONESTY:** does NOT close F1 (watermark-replay), M2 (total-erasure), L4 (purge holes) — carried unchanged from slice-1; surfaces a NEW compliance gap (PII riding unprotected in the Hangfire job-args table), filed as a follow-up, NOT fixed this slice.

- **MSVC-CELL7-INGESTION-SWALLOW-FIX** — ✅ RESOLVED 2026-07-15 (🟡 T3, microservices / audit / observability / persistence; commit pending) — `AuditService.LogInternalAsync` (the standalone audit-log path) now catches a synchronous persist failure and enqueues a durable, idempotent Hangfire `AuditIngestionRetryJob` instead of the previous silent swallow (`AuditBehavior.cs:59-63`'s post-business-commit `catch (Exception) { LogWarning }`, no rethrow, no retry — the headline "ingestion-loss" gap the slice-1 tail-watermark could not close, since it only detects gaps in rows that DID get written). Only a **double failure** (sync persist AND the retry enqueue both die) rethrows — `AuditBehavior` now `LogCritical`s (was `LogWarning`) and the new `audit.ingestion.lost` OTel counter fires; the fail-open contract for the business command itself is unchanged. **New files:** `AuditEntryPersister` (Infrastructure) — the single atomic row+outbox+HMAC-chain-enqueue write path, extracted from `AuditService` and shared VERBATIM by the sync path and the retry job; its write span deliberately ignores the request `CancellationToken` (`CancellationToken.None`) so a request-abort AFTER the business command already committed can't drop the audit row — this closes the dominant real-world L1 trigger. `AuditIngestionIntent` (Application) — a plain-mutable, Newtonsoft-serializable DTO freezing every field the retry job needs (no positional record — the Hangfire job-store round-trip requires get/set + a parameterless ctor). `AuditIngestionRetryJob` (Infrastructure, `[AutomaticRetry(Attempts = 10)]`) — rebuilds and re-inserts the row; idempotency is insert-then-catch-unique-key-violation on `MessageId` (the row's alternate key), not read-first (a read-first dedup would be RLS-blind under the prod least-privilege login, since a background job has no SESSION_CONTEXT). A persistently un-writable intent exhausts the retry cap and lands in Hangfire's Failed state — the dead-letter, visible and re-queueable via the dashboard, no custom dead-letter table. `AuditMetrics` (Application/Telemetry) — 3 counters (`audit.ingestion.fallback_enqueued` / `.recovered` / `.lost`, meter `AqtCoreFW.Audit`) making the formerly-silent swallow observable; `.lost` is the alert-worthy signal. **Additive:** `IBackgroundAuditJobEnqueuer.EnqueueIngestionRetry(AuditIngestionIntent)` (+ `HangfireAuditJobEnqueuer` impl); `AuditLogEntry.Rehydrate` domain factory (rebuilds from frozen state, SequenceId=0/no signature — signed by the normal next chain pass, identical to the sync path); `OpenTelemetryConfiguration.AddMeter("AqtCoreFW.Audit")`.
  **Review-driven fixes (both false-green under sa/db_owner tests, folded in-slice):** **C1 (Critical, fixed):** the retry job runs in a Hangfire scope whose ambient tenant is null — without pinning, the RLS BLOCK predicate rejects a named-tenant INSERT under the prod least-privilege (non-`db_owner`) login, so the recovery would silently no-op in production. Fixed: `AuditEntryPersister.PersistAsync` begins its UoW with `BeginAsync(entry.TenantId)` when it owns the UoW (behavior-preserving on the sync path, where `entry.TenantId` already equals the ambient tenant). Proven by new non-`db_owner` test `AuditIngestionRecoveryLeastPrivilegeTests` (`tests/integration/AqtCoreFW.TenantIsolation.Tests/`) with a non-vacuity guard. **M1 (fixed):** insert-then-catch a unique-key violation is treated as idempotent success (a read-first dedup check would be hidden by the RLS FILTER under least-priv with no SESSION_CONTEXT).
  **Atomicity note:** the row+outbox write still rolls back together on any failure inside `AuditEntryPersister` (unchanged); what changed is what happens to that failure afterward — it's now converted into a durable retry instead of propagating to the swallow.
  **Verify:** Audit.Integration 82/82; TenantIsolation.Tests 56/56 (incl. the new least-privilege test); Audit.Infrastructure unit 23/23; SharedKernel AuditBehavior 10/10; build 0W/0E. Code-review REQUEST_CHANGES (C1 Critical, M1 Medium, 1 regression) → all resolved in-slice, re-verified green.
  **Contract:** none — new internal job + DTO; `IBackgroundAuditJobEnqueuer` gains 1 additive method; no public DTO/endpoint/cross-module client change.
  **Deferred (compliance-blocked, NOT fixed this slice):** `MSVC-CELL7-INGESTION-PII-JOBARG` — `AuditIngestionIntent` carries Confidential fields (`IpAddress`, `UserName`, `DataSnapshot` — the latter also PII per `AuditPiiAnonymizer`) as raw Hangfire job args in `[hangfire].[Job]`, outside the RLS/HMAC/retention compliance boundary; a poison (Failed) job persists that PII indefinitely there — violates the repo's `PiiMaskingJobFilter` "IDs-only job args" standard (Law 91/2025 / QĐ 742). Needs compliance-owner sign-off before a fix. Deferred by user 2026-07-15 (scope decision A).
  **Residual (documented, NOT closed, unchanged from slice-1 except noted):** F-C — process-crash window between the business commit and the fallback enqueue (closes only at physical extraction, when audit becomes its own service with its own durability boundary); F-G — no independent durable store (a whole-DB outage still owes no audit row on a single-DB monolith — both the business write and the audit fallback share the one DB); fail-closed audit ("no audit, no action" per QĐ 742, vs. this slice's fail-open-with-recovery design) — candidate FU-A auditor question. F1 (watermark-replay), M2 (total-erasure), L4 (purge holes) carried unchanged from slice-1 (`MSVC-CELL7-TAIL-WATERMARK`, below).

---

## 2026-07-15 (Signed per-tenant chain tail-watermark — completeness-matrix cell #7 slice-1, closes L2 tail-truncation)

> First code-able slice of completeness-matrix cell #7 ("Audit-chain under distribution", roadmap §4). Closes L2 (key-less tail-truncation) with a signed per-tenant watermark. **HONESTY:** does NOT close F1 (watermark-replay), M2 (total-erasure), L1 (ingestion-loss), L4 (purge holes) — all documented residuals, 2 follow-up rows filed + 2 auditor questions appended.

- **MSVC-CELL7-TAIL-WATERMARK** — ✅ RESOLVED 2026-07-15 (🟠 T2, microservices / audit / security / persistence) — new `[audit].[ChainWatermarks]` (1 row/tenant, HMAC-signed: `LastSequenceId`+`ChainHead`+`WatermarkHmac`+`UpdatedAtUtc`) upserted at the end of every signing pass (incremental+backfill+0-row heal) inside the existing per-tenant applock; `HmacChainService.VerifyAsync` gains tail-attestation via a shared `EvaluateWatermark` helper used by BOTH the EF and raw-ADO verify paths, reading the watermark BEFORE streaming the chain (race-free against a concurrent append — `maxSeenSeq >= LastSequenceId` holds in the common case). Migration `20260715045449_AddAuditChainWatermarks`. **Closes L2 tail-truncation:** a key-less DB-write attacker deleting the newest K rows of an otherwise dense+valid chain (1..N-K) previously verified GREEN; RED-first proved it empirically (stub → GREEN-on-truncation → now FAIL "Tail truncation"). **Tests:** `HmacChainTailWatermarkTests` (5) + `HmacChainTailTruncationTests` (6) new; `HmacChainProdLeastPrivilegeTests` +1 (non-`db_owner` `rls_test_app` proves the `ChainWatermarks` GRANT is sufficient for upsert + read-back); `RlsPolicyPresenceFixture` +1 idempotent GRANT. **Verify:** build 0W/0E; TailWatermark 5/5; TailTruncation 6/6; HmacChain regression 29/29; ProdLeastPrivilege 5/5; RlsPolicyPresence 50. Code-review APPROVE_WITH_NITS (0 Critical/High code-level; 1 High ops-gated below; M1/L1 fixed in-slice). **Contract:** none — new internal table, no cross-module client, no public DTO/endpoint change.
  **HONESTY (compliance-critical — what verify-GREEN does and does NOT attest):** closes key-less tail-truncation only. Documented, NOT closed: **(F1) watermark-replay** (attacker holding an older valid watermark truncates to that point + replays the old watermark — passes every check; needs an external append-only anchor); **(M2) total-erasure** (deleting every signed row AND the watermark row verifies GREEN via the empty-chain-OK branch, indistinguishable from a tenant that never logged); **(L1) ingestion-loss** (slice-2, follow-up filed below); **(L4) purge-induced false-tamper** (follow-up filed below).
  **OPS-gated H1 (not code):** prod `ConnectionStrings:AuditAdmin` must stay `db_owner`-capable — a fallback to non-`db_owner` `aqtcore_app` during the rollout-heal enum would silently skip named-tenant chains, leaving their watermark unhealed (verify check-(b) false-fails until that tenant's next signing pass). Verify is authoritative only after rollout completes.
  **Follow-ups filed:** `MSVC-CELL7-INGESTION-SWALLOW-FIX` (strategy D, closes L1) · `MSVC-CELL7-PURGE-CHAIN-REANCHOR` (🔴 URGENT, closes L4). **Auditor questions appended:** `docs/microservices-compliance-isolation-auditor-questions.md` — (1) is a chain re-anchor/epoch model after retention-purge acceptable; (2) is in-DB HMAC watermark protection sufficient, or is an external anchor (WORM/TSA) needed against Vault-compromise + F1 + M2.

---

## 2026-07-15 (Client-level admin-bypass wiring proof — last code-able item on the [audit] read-isolation arc)

> Closes the optional non-db_owner integration test filed against `ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR`. With this, the monolith `[audit]` read-isolation arc has **no code-able work remaining** — the only open follow-up is FU-A (auditor question, a user→auditor action, non-code).

- **AUDIT-EVIDENCE-CLIENT-BYPASS-WIRING-TEST** — ✅ RESOLVED 2026-07-15 (🟢 test-only hardening) — `AuditEvidenceClientAdminBypassWiringTests` (2 tests) proves `InProcessAuditEvidenceClient.GetIntegrityStatusAsync` + `GetRetentionPolicyStatusAsync(null)` route their D7 row COUNTs through the `audit_admin_bypass` opener (`QuerySingleWithAuditAdminBypassAsync`), so NULL-tenant (system/global) rows are included on the D7 security-health dashboard (QĐ 742 §3.4.a / §3.2). Complements `AuditRlsEnforcementTests` (proves the RLS primitive) by proving the *client wiring* — a plain-opener refactor would silently under-count and now turns the suite RED. Runs under non-`db_owner` `rls_test_app`; A/B non-vacuity (plain COUNT `== 0` vs bypass COUNT `>= 1`) also self-guards against a `db_owner` false-green. **Fixture:** added one idempotent `GRANT SELECT ON [audit].AuditRetentionPolicies TO rls_test_app` (retention method reads that table on the plain opener first) — additive, zero sibling blast-radius. **Verify:** build 0W/0E; 2/2 pass (warm local SQL); code-review APPROVE_WITH_NITS (6/6 acceptance criteria confirmed, 2 cosmetic nits applied). **Contract:** none — test + fixture only.

---

## 2026-07-15 (Cross-module client contract serialization-safety arch-guard — completeness-matrix cell #3 slice-1A)

> First code-able slice of completeness-matrix cell #3 ("Sync inter-service + auth propagation", roadmap §4) — locks the remote-readiness precondition (contract-purity) for all 18 cross-module `I*Client` interfaces before any `Remote*Client` is built. Test-only, 0 runtime blast. Slice-1B (first `Remote*Client` vertical) filed as a follow-up, ready/unblocked, in `backlog.md`.

- **MSVC-CELL3-CONTRACT-SERIALIZATION-GUARD** — ✅ RESOLVED 2026-07-15 (🟢 test-only, arch-guard) — new `CrossModuleClientContractSerializationSafetyTests` (4 facts, `tests/architecture/AqtCoreFW.Architecture.Tests/`) proves every method (param + return, `Task`/`ValueTask` unwrapped) on all 18 `I*Client` contracts under `src/shared/*.Contracts` is remote-serialization-safe — a default-deny recursive type checker (rejects `Stream`/`IQueryable`/`Expression`/delegates/domain types/ASP.NET types; allows scalars, enums, safe collections, `*.Contracts` POCOs) with 2 deliberate carve-outs: `object` as a collection value (the form-prefill value bag) and `JsonDocument`/`JsonElement` (dynamic JSON payload). Discovery is hybrid: a source-text census over `*.Contracts` folders cross-checked against reflection, guarding against undercount from an unreferenced assembly. **Wiring:** 12 `ProjectReference` entries to the cross-module `*.Contracts` assemblies added to `AqtCoreFW.Architecture.Tests.csproj`. **Finding:** the guard caught `IRuleEngineModuleClient.EvaluateAsync(JsonDocument input)`, missed by manual scout — user decided to whitelist `JsonDocument`/`JsonElement` (consistent with the `object` carve-out) rather than touch the contract. **Verify:** guard 4/4; full `Architecture.Tests` 178/178 (174 baseline + 4); build 0W/0E; code-review APPROVE_WITH_NITS (0 Critical/High; 1 Medium — POCO recursion skipped public fields — fixed in-slice, added `GetFields` alongside `GetProperties`; re-verified 178/178). **Contract:** none — test + arch-test csproj only. **Out of scope:** `Remote*Client`, HTTP endpoint, DI toggle, contract changes — filed as follow-up `MSVC-CELL3-SLICE1B-REMOTE-CLIENT-VERTICAL` (ready/unblocked) in `backlog.md`.

---

## 2026-07-14 (EF-first audit read-isolation — non-audit module EF contexts denied [audit])

> First increment of the shared-DB read-isolation path (ADR-012 amendment 2026-07-13). Non-audit module EF reads now run under an opt-in least-privilege login denied `[audit]`; the DB+EF capability is proven and the wiring is arch-guarded. Standalone-Dapper isolation + shared-infra contexts + prod activation deferred to `ISO-DAPPER-KEYED-DI-INCREMENT`.

- **ISO-EF-FIRST-AUDIT-BOUNDARY** — ✅ RESOLVED 2026-07-14 (🟠 T2, microservices / persistence / security; plan `260713-1720`) — the 14 non-audit module DbContext registrations resolve their connection string via new `NonAuditConnectionString.Get(config)` = `ConnectionStrings:NonAudit ?? Default` (least-priv login denied `[audit]`; fallback keeps dev/test/CI booting → **opt-in** prod isolation). Audit module stays on `Default` (owns `[audit]`); `HmacChainService` audit reads already run on the audit-owner `Default` connection (no change needed). **Proof:** `AuditBoundaryEfIsolationTests` (committed `a5e890ad`) — EF analog of the C3 raw-ADO spike: `files_svc_app` reads `[files]` via EF, DENIED `[audit]` (SqlException 229), non-vacuous (positive leg + `IS_MEMBER('db_owner')=0`). `AuditBoundaryEfWiringGuardTests` locks the 14-registration wiring (count==14, non-vacuous) + audit-stays-Default. **Verify:** build 0W/0E (API + AuthServer, `-warnaserror`); TenantIsolation.Tests 46/46; arch-guard 2/2; helper unit 3/3. The 5 `Startup.SmokeTests` API-host fails are **pre-existing** warm-DB pollution (git-stash A/B: fail identically on pre-change tree; each passes isolated — memory `feedback-test-local-sql-warm-reuse-and-false-red-modes`). **Review:** DONE_WITH_CONCERNS (0 Crit / 0 High / 1 Med / 3 Low); M1 (shared-infra contexts) + L3 (prod grant-matrix runbook) resolved in ADR-012; L1 (`??` null-only — matches `AuditAdmin ?? Default` idiom) + L2 (enumerated guard — matches `MarsConnectionStringGuardTests`) accepted. **Contract:** none — registration method signatures unchanged; DI behavior identical when `NonAudit` absent. **Honest scope:** covers 14 module SERVICE contexts only; standalone Dapper + shared-infra EF (Alerting/ApiKeys/Webhooks/Messaging) + prod login provisioning deferred to `ISO-DAPPER-KEYED-DI-INCREMENT`. Impl commit `4b74114e`.

---

## 2026-07-14 (Dapper keyed-DI + shared-infra EF audit read-isolation — CODE-COMPLETE, ops-activation-pending)

> Completes the Dapper follow-on `ISO-EF-FIRST-AUDIT-BOUNDARY` (above) deferred. **Milestone note, not a closure** — the `ISO-DAPPER-KEYED-DI-INCREMENT` backlog row stays OPEN in `backlog.md` pending production ops activation (running the provisioning script + publishing the Vault key); recorded here as a code-complete milestone per the docs-update convention.

- **ISO-DAPPER-KEYED-DI-INCREMENT** — 🟢 CODE-COMPLETE 2026-07-14 (commit `f0ad95e3` keyed-DI wiring + this session's Phase-3 proof/provisioning/ADR update, commit pending; plan `plans/260714-1021-iso-dapper-keyed-di-audit-read-isolation`) — default (unkeyed) `IReadDbConnection` (Dapper) now resolves the least-privilege `NonAudit` login (denied `[audit]`, opt-in fallback `Default`); 12 audit-capable consumers (9 audit query handlers + `InProcessAuditEvidenceClient` + 2 identity SecurityHealth reads) inject a keyed `"audit"` reader on `Default` via `[FromKeyedServices(ReadConnectionKeys.Audit)]`; both SESSION_CONTEXT bypass openers inherit the per-registration login. 3 shared-infra EF contexts (Alerting/ApiKey/Webhook) folded onto `NonAudit`; `MessagingDbContext` stays `Default` (AuthServer-coupled, documented exclusion). **Proof:** `AuditBoundaryDapperIsolationTests` 5/5 (standard + both bypass openers DENY `[audit]` SqlException 229 under a non-`db_owner` constrained login, `IS_MEMBER('db_owner')=0` non-vacuous) + `AuditKeyedReaderAllowlistTests` (closed 12-consumer keyed-reader allowlist, source-text sweep of `src/`, CI backstop) + extended `AuditBoundaryEfWiringGuardTests` (separate `src/shared`-aware assertion + explicit Messaging-exclusion fact). **Verify:** Architecture.Tests 174/174; full regression 652 green; full solution build 0W/0E. **ADR-012 updated:** audit read-isolation now covers EF + Dapper + Hmac, **enforced only when the prod `NonAudit` login is provisioned (opt-in) — NOT blanket-complete**; standing exceptions unchanged (UoW-active reads, 2 identity SecurityHealth keyed reads, admin/`db_owner`/backup/seeders). **Provisioning artifact:** `scripts/260714-nonaudit-login-provisioning.sql` (GRANT DML+DDL on the 19 non-audit schemas, DENY `[audit]`; migration-vs-login ordering runbook + rollback + Vault KV `ConnectionStrings__NonAudit` in its header). **Remaining (ops, not code):** run the provisioning script + publish the Vault key to activate isolation in production. Two small follow-ups filed in `backlog.md`: `ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR` (route the 2 identity SecurityHealth reads through `IAuditModuleClient`) and `ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK` (prod fail-fast / degraded-isolation signal when `NonAudit` unset — deferred this increment).

---

## 2026-07-14 (Identity SecurityHealth reads moved off keyed audit reader onto IAuditEvidenceClient — cross-module client refactor)

> Follow-up (`ISO-DAPPER-KEYED-DI-INCREMENT` Decision 3) closing the last identity `[audit]`-capable credential. Identity's 2 SecurityHealth handlers now go through the audit module's cross-module client instead of a keyed Dapper reader on `[audit]`.

- **ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR** — ✅ RESOLVED 2026-07-14 (🔵 T4, follow-up of `ISO-DAPPER-KEYED-DI-INCREMENT` Decision 3; commit pending) — `GetAuditIntegrityStatusQuery` + `GetAuditRetentionStatusQuery` (Identity.Application) no longer read `[audit]` via the keyed `"audit"` Dapper reader; both now delegate to `IAuditEvidenceClient` (`AqtCoreFW.Audit.Contracts`), which gains `GetIntegrityStatusAsync` + `GetRetentionPolicyStatusAsync` (implemented by `InProcessAuditEvidenceClient`, audit module, audit-owner login). **New dependency edge:** `Identity.Application` → `Audit.Contracts` (ProjectReference, contracts-only), mirroring the existing `Reporting.Application` edge. **Identity now holds no `[audit]`-capable DB credential.** SQL moved verbatim; admin-bypass preserved; FE DTO contract unchanged. Audit-keyed reader allowlist 12→10; ADR-012 standing-exception #5 (the "2 identity keyed reads") removed. **Verify:** full solution build 0W/0E; `AuditKeyedReaderAllowlistTests` + wiring guard 6/6; `Identity.Application.Tests` 426/426; code-review DONE_WITH_CONCERNS (all 6 checks pass; sole finding = LOW stale-comment drift, fixed in-slice). Journal: `docs/journals/journal-20260714-1648-iso-audit-moduleclient-securityhealth-refactor.md`.

---

## 2026-07-14 (Prod observability for the dormant audit read-isolation boundary — health-check + startup warning)

> T4 follow-up of `ISO-DAPPER-KEYED-DI-INCREMENT` (filed 2026-07-13 evening as its own row, Phase-3 acceptance criterion deferred out of that increment). Closes the last open ISO-* follow-up: a prod deploy that never provisions the `NonAudit` login now gets a visible signal instead of silently booting with isolation off.

- **ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK** — ✅ RESOLVED 2026-07-14 (🔵 T4, Low; API-host only, commit pending) — previously `NonAuditConnectionString.Get()` = `NonAudit ?? Default` (opt-in) meant a Production deploy that never provisioned the least-privilege `NonAudit` login booted silently with isolation OFF (non-audit module reads on the broad `Default` login, reachable to `[audit]`), no signal. **Shipped:** (1) `NonAuditConnectionString.IsExplicitlyConfigured(IConfiguration)` — new helper distinguishing provisioned vs opt-in fallback; (2) `NonAuditIsolationHealthCheck : IHealthCheck` (new) — readiness check returning **Degraded** (never Unhealthy — preserves opt-in, keeps the pod in K8s rotation at HTTP 200) when `Production && NonAudit` unset, Healthy otherwise; terse descriptions (no schema/script-path leak on the unauthenticated `/health/ready`); registered in `AddGovHealthChecks` tagged `["audit-isolation","ready"]`, name `audit-read-isolation`; (3) warning-only (NOT fail-fast) startup log in API `Program.cs` citing the provisioning script path when `Production && NonAudit` unset. AuthServer correctly out of scope (skips `AddInfrastructure`). **Tests:** `NonAuditConnectionStringTests` +3 (`IsExplicitlyConfigured`), new `NonAuditIsolationHealthCheckTests` (3, truth-table) → 9/9 new. **Verify:** build 0W/0E (API host + Infra test project); `AuditBoundaryEfWiringGuardTests` + `AuditKeyedReaderAllowlistTests` 6/6 (no regression). **Review:** DONE_WITH_CONCERNS (0 Critical/High/Medium; L3 info-leak + L6 comment-drift applied in-slice; L1 empty-string divergence + L2 prod-rollout-ordering documented as non-blocking, see OPS note). **Contract:** none — new health-check tag + log line only, no endpoint/DTO change. **OPS note (L2, must sequence with deploy):** shipping this flips `/health/ready` to Degraded + emits the startup warning on the next Production deploy UNTIL `scripts/260714-nonaudit-login-provisioning.sql` runs and `ConnectionStrings:NonAudit` is set — sequence provisioning with/ahead of the deploy; ensure monitors don't page on Degraded (non-Healthy but HTTP 200) readiness. This closes the last ISO-* follow-up filed 2026-07-13 evening; monolith-side audit read-isolation observability is now complete/opt-in end-to-end.

---

## 2026-07-10 (Legacy stale-audit — SoD enforcementLevel compat-shim removal + LegacyEnforcementLevel obsolete-close)

> Legacy follow-up bucket stale-audit (INVARIANT 260522-A). Empirical probes on deadline-elapsed rows: the v2.127 SoD `enforcementLevel` V4 compat shim was removed outright (deprecation window v2.129+ long past at HEAD v2.156; FE uses `severity` exclusively, both test roots reference the legacy field 0×). Sibling "shadow column" row closed obsolete — `LegacyEnforcementLevel` never existed in code (compat was a computed property, not a DB shadow column). BTC-skip row updated in-place (claim decayed 15→3). Trigger-gated legacy buckets (already swept 06-20/06-23) re-confirmed — no new trigger fired.

- **SoD `enforcementLevel` V4 compat-shim removal** — ✅ RESOLVED 2026-07-10 (SoD v2.127 deprecation follow-up; intentional public-contract cleanup) — dropped the deprecated `enforcementLevel` alias from the SoD-rules API after its window (v2.129+) elapsed 27 releases ago (HEAD v2.156). **Removed:** (1) computed response property `SodRuleDto.EnforcementLevel => Severity` + `[Obsolete]` (`ListSodRulesQuery.cs`; DTO now plain positional record, ctor params unchanged); (2) `EnforcementLevel` request property from `CreateSodRuleRequest` + `UpdateSodRuleRequest`, the `request.Severity ?? request.EnforcementLevel ?? "Low"` fallback → `?? "Low"`, and the `#pragma warning disable/restore CS0618` wrappers (`SodRulesAdminController.cs`). **Blast-radius (verified clean, before + after):** `EnforcementLevel`/`enforcementLevel` grep across `src/` + `tests/` + `src/tests/` = 0 after removal; FE hand-written `web/src/features/sod-rules/*` uses `severity` exclusively (0 refs); the only FE hit is the orval-GENERATED `aqtCoreFWAPIV1.schemas.ts` optional type — inert, self-cleans on next regen (`DELEG-ORVAL-REGEN`, dev-stack-gated). No `UnmappedMemberHandling.Disallow` → stale clients sending `enforcementLevel` are silently ignored, never 400 (graceful degradation). **Contract:** intentional request+response DTO field removal, symmetric; no hand-written consumer affected. **Verify:** full solution build 0W/0E (`-warnaserror`), Identity tests 1,276/1,276 (Domain 173 · Application 426 · Infrastructure 339 · Integration 338), code-review APPROVE_WITH_NITS (sole nit = `dependency-map.md:399` doc-drift, fixed same-slice). `dependency-map.md` §v2.127 updated.
- **Drop shadow column `LegacyEnforcementLevel`** — ✅ CLOSED-OBSOLETE 2026-07-10 (audit, STALE — never implemented) — `git log --all -S LegacyEnforcementLevel` = 0 adds; the identifier never existed in code. The v2.127 SoD compat was a computed API-DTO property + request fallback (removed above), NOT a DB shadow column. Row described a strategy superseded before it shipped; no migration needed. `dependency-map.md` §v2.127 corrected.
- **BTC-SKIPPED-TESTS-REACTIVATE** — ✅ RESOLVED 2026-07-10 (test-only, resume plan `260528-1905` phases 02+03 complete → plan closed) — reactivated the last 3 `[Fact(Skip)]` Smoke tests in `OrgUnitsControllerSmokeTests.cs` (Seeding 9 + Architecture 3 were already un-skipped; seeding already wired via `OrgTreeSeeder` maxLevel 4; Phase-01 env-guard `394e2efb` already merged). **Live cause was L3 only:** the auth client omitted the `mfa_at` claim the controller's class-level `[RequiresMfa]` requires → 403. **Change (test-only, 1 file):** (1) client → 4-arg `CreateAuthenticatedClient(..., includeMfaClaim: true, roles: "Admin")`; (2) removed the 3 `[Fact(Skip)]`; (3) corrected `GET_OrgUnitsTree` assertion — `GetOrgTreeQuery : IQuery<IReadOnlyList<OrgUnitDto>>` returns a FLAT array (`OrgUnitDto` = "clients build tree from ParentId"), so it now asserts `JsonValueKind.Array` + non-empty (`GetArrayLength()>0`, proves seed visible) + first-element `code`/`level` schema sanity (was wrongly `JsonValueKind.Object`, a speculative defect never exercised while skipped). **Verify:** full `AqtCoreFW.Organization.Tests` 79/79 green (was 76 + 3 skip), `Skip=` now 0, build 0W/0E, code-review APPROVE (all 5 checks empirically verified). **Contract:** none — zero production change. **Deferred:** CI-rename OPEN-Q (make the 9 `…IntegrationTests` visible in the CI unit surface) stays parked — CI PARKED this phase; 2 pre-existing Low review notes (near-vacuous `by-code` tests; would tighten if a `by-code` endpoint ever ships).

---

## 2026-07-10 (CrossSchemaSqlScanner write-side coverage + MSVC-WAVE0-P2/P3 + AUDIT-PII audit-closes)

> Backlog-hygiene + arch-guard day. Three microservices-prep rows resolved via INVARIANT 260522-A empirical audit: two were fully stale (already shipped), one ("honest re-affirm") confirmed clean and closed with a genuine defense-in-depth extension.

- **MSVC-WAVE0-P3-CROSS-SCHEMA-REQUEST-PATH** — ✅ RESOLVED 2026-07-10 (🔵 T4, arch-guard, test-only) — Completed the write-side blind spot of `CrossSchemaSqlScanner` (arch-test helper guarding the module→schema boundary for microservices extraction). **Audit verdict:** the row's "route genuine request-path coupling" ask was a no-op — the 3 real couplings were refactored to ModuleClient interfaces at v2.132p10; whitelist = 6 install-time seeders + 2 read-only D7 dashboard queries (all legitimate, preserved); `NoModule_Should_Reference_AnotherModuleSchema_InRawSql` green. Empirical write-side probe: all 20 raw-SQL sites owner-local, 0 cross-schema writes, 0 interpolated schema → 0 current violations. **Shipped (regression-prevention):** broadened the two scanner regexes' shared keyword alternation from `FROM/JOIN` to `+UPDATE/INSERT [INTO]/MERGE [INTO]` (INTO-optional per T-SQL, leading `\b` word-boundary, backstopped by the pre-existing `KnownSchemas` filter → false-positive-safe) + 7 non-vacuous self-tests (`CrossSchemaSqlScannerTests`) driving the real `Scan()` over a temp module tree (positive per write verb, read-regression guard, owned + whitelisted negatives). `DELETE FROM` already covered via the `FROM` branch; interpolated-schema `FromSqlRaw` stays out (documented limitation). **Verify:** Architecture.Tests 168/168 (was 161; +7), real-scan stays green (0 FP), build 0W/0E, code-review APPROVE_WITH_NITS (2 Low fixed: INSERT-without-INTO coverage + `\b` anchor; 2 pre-existing nits noted). **Contract:** none — test helper only, additive/stricter. Plan `plans/260710-1111-crossschema-scanner-writeside/`.
- **MSVC-WAVE0-P2-OUTBOX-AT-LEAST-ONCE** — ✅ RESOLVED 2026-07-10 (audit-close, STALE) — fully shipped as Sóng-0 plan `260706-1237` Phase-1 (slices 1A–1E, pushed `abd4e079`). Probe: all 4 named publishers handled — SubmitForm/SubmitPublicForm→`outboxWriter.EnqueueAsync` (1B-1 `fd2cb118`), AuditService→outbox (1B-2 `9eefc562`), LockoutEscalation→WONTFIX direct at-most-once (1B-3). Tail "phase-02 M→L remaining" was a decayed leftover from superseded plan `260704-2205`. Carry-fwd caveat preserved on the backlog row (audit read-side projection activation must backfill). Docs commit `1c2e7fe6`.
- **AUDIT-ACCOUNTDISABLED-EVENT-PII-COMMENT-CONTRADICTION** — ✅ RESOLVED 2026-07-10 (audit-close, STALE) — doc-fix already shipped in Sóng-0 1B-3 (07-07): `AccountAutoDisabledIntegrationEvent` XML-doc rewritten to state the payload carries UserName+Email (transient direct-publish, never persisted), no longer the false "NO PII" claim. Docs commit `1c2e7fe6`.

---

## 2026-07-10 (Workflow parallel-join finalization correctness)

> Workflow parallel-join state transitions now properly set `CompletedAt` and fire finalization events, mirroring direct-transition finalization paths. Form-linked parallel-join workflows now durably trigger status-write outbox events when joining to final states, eliminating silent submission-status stalls.

- **WF-PARALLEL-JOIN-FINAL-NO-STATUS-WRITE** — ✅ RESOLVED 2026-07-10 (🔵 T4, correctness, latent data-loss) — `WorkflowInstance.ApplyParallelJoinTransition` now sets `CompletedAt` and fires `RaiseFinalizationEvent`, mirroring direct-transition finalization behavior. Previously, parallel-join transitions to final states left `CompletedAt = null`, preventing finalization event fire → FormSubmission status-write outbox events never enqueued → linked submissions silently remained Pending despite workflow completion. **Root:** parallel-join finalization path (post-join convergence to final state) added in Phase 4 but never integrated with status-write machinery (only direct transitions fired events). **Fix:** (1) `ApplyParallelJoinTransition` signature gains `bool isFinalState` param; sets `CompletedAt = utcNow` when final (idempotent). (2) `BranchJoinCompletionService.CompleteJoinAsync` inspects `continuationState.IsFinal`, passes flag + calls `instance.RaiseFinalizationEvent(...)` when final (event captured by OutboxInterceptor atomically). (3) Guard: outbox event emission only for `TenantScopedIntegrationEvent` consumers (FormSubmission status-write path). **Tests:** Domain 3 unit (ApplyParallelJoinTransition state + CompletedAt + event), Application 3 unit (BranchJoinCompletionService join-to-final + event + non-final path). **Verify:** Workflow.Domain.Tests 148/148, Workflow.Application.Tests 642/642, build 0W/0E, code-review APPROVE (0 defect, 2 informational nits). **Impact:** Parallel-join workflows now durably transition form statuses on finalization (latent gap; no live parallel-join→final workflows today). **Contract:** internal domain-method signature only; no public-endpoint/event/entity changes.

---

## 2026-07-09 (PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS + PUBLIC-FORMS-ISPUBLIC-ADMIN-TOGGLE + PUBLIC-FORMS-LEAST-PRIV-ANON-READ security carve-out + P6 NetworkPolicy Egress)

> Cross-tenant public-Code uniqueness determinism locked (filtered unique index + 409 gate). Anonymous public-forms RLS carve-out closes least-privilege blocker via asymmetric FILTER+BLOCK policies and IsPublic flag. NEW templates default IsPublic=0 (existing Active forms backfilled). Admin-toggle FE/BE capability completed same-day.

- **PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS** — ✅ RESOLVED 2026-07-09 (🔵 T4, correctness, cross-tenant by-Code uniqueness) — Migration `20260709082745_AddFormTemplatePublicCodeUniqueIndex` adds filtered unique index `UX_FormTemplate_PublicCode ON forms.FormTemplates(Code) WHERE IsPublic = 1 AND IsDeleted = 0` + fail-loud pre-flight `STRING_AGG` abort if cross-tenant public-Code dupes exist (non-destructive). App-guard in `SetFormTemplatePublicAccessCommandHandler` returns 409 `FormErrors.PublicCodeConflict` (new code GOV_FRM_019) when enabling IsPublic on Code already public in another tenant (uses `public_forms_bypass` Dapper probe). Doc-only note added to `IReadDbConnection.QueryFirstOrDefaultWithPublicFormsBypassAsync`. Contract-stable (PATCH endpoint, Command, DTO, 3 public by-Code SQLs, per-tenant index unchanged). Build 0W/0E, 67/67 + 8 least-priv-guard-probe tests PASS. **Sibling note:** origin FU `PUBLIC-FORMS-ISPUBLIC-ADMIN-TOGGLE` + `PUBLIC-FORMS-LEAST-PRIV-ANON-READ` shipped same session (commit not yet made; git-manager will cite `Closes: PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS`).

- **PUBLIC-FORMS-SETCODE-PUBLIC-RENAME-409** — ✅ RESOLVED 2026-07-09 (🔵 T4, UX-only, index-backstopped; FU of `PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS`) — extended the friendly 409 to the SetCode rename path (`UpdateFormTemplateCodeCommandHandler`), mirroring the `SetFormTemplatePublicAccessCommandHandler` sibling. Guard `if (template.IsPublic && Status==Draft)` → `public_forms_bypass` probe on `request.NewCode` (`Code=@Code AND IsPublic=1 AND IsDeleted=0 AND Id<>@ExcludeId`, mirrors `UX_FormTemplate_PublicCode`) → 409 `FormErrors.PublicCodeConflict` (GOV_FRM_019 reused). Draft-gated (code-review L1): SetCode is Draft-only, so a non-Draft public rename fails GOV_FRM_010 before any UPDATE — probe fires exactly when a raw index-500 is reachable. Contract-stable (command shape, error codes, index unchanged; new `IReadDbConnection` ctor param DI-safe). Build 0W/0E, Forms.Application.Tests 344/344 (4 new; probe-arg pinned per review M1), PublicFormsLeastPriv 9/9 (new Test 9 = Code-UPDATE index collision), code-review 0 Crit/High. Commits `b063f27d` + `8ac7b60f` + `e5b4358c`. **Caveat (review L2, verified):** `UpdateFormTemplateCodeCommand` has no endpoint/dispatch/FE consumer yet — correct-when-wired insurance; FU `FORMS-CODE-RENAME-ENDPOINT-FE-WIRING` (T4, blocked-on product-Q) filed to track the wiring.

- **PUBLIC-FORMS-ISPUBLIC-ADMIN-TOGGLE** — ✅ RESOLVED 2026-07-09 (🔵 T4, FE/BE admin capability) — Form author capability to toggle `FormTemplate.IsPublic` via admin API endpoint, completing the RLS public-read carve-out design from same-day morning (PUBLIC-FORMS-LEAST-PRIV-ANON-READ). **NEW:** (1) `PATCH /api/v1/forms/templates/{publicId:guid}/public-access` (body `{ isPublic: bool }`, returns 204/404) — gated `[Authorize(Policy = PermCodes.Forms.Template.Manage)]` + rate-limit `write-ops`. Command: `SetFormTemplatePublicAccessCommand` → domain `FormTemplate.SetPublicAccess(bool, Guid)` method (idempotent state flip, audit-logged). (2) `FormTemplateDto` gained trailing optional `bool IsPublic = false` (single-template GET projects it; list endpoint intentionally omits). (3) FE consumer: `web/src/features/forms/form-settings-panel.tsx` (Settings tab public-access Switch with confirm-on-enable modal) via new hook `useSetTemplatePublicAccess()` in `form-template-api.ts`. **Tests:** BE integration 13/13 (4 new SetPublicAccess endpoint tests + 9 pre-existing FormTemplatesApiTests, 0 regression) · FE vitest 18/18 · typecheck clean · code-review 0 Critical/High. **Impact:** Form authors now control anonymous public reachability; NEW templates default IsPublic=0 (existing Active=1 from backfill). Unblocks Sóng-0 public-forms feature parity. Carry-fwd: `PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS` (T4 product-Q, cross-tenant by-Code arbitrariness).

- **PUBLIC-FORMS-LEAST-PRIV-ANON-READ** — ✅ RESOLVED 2026-07-09 (🟠 T2, security, RLS carve-out) — anonymous public-forms read paths now work under least-privilege (non-db_owner) DB login. **Root:** `forms.FormTemplates` RLS FILTER (`fn_TenantFilter`) returned 0 rows when SESSION_CONTEXT unset (anonymous) + non-db_owner. **Design chosen:** asymmetric RLS carve-out per `FormTemplate.IsPublic` flag. **NEW:** (1) `FormTemplate.IsPublic` bool (default false) + domain `SetPublicAccess` + migration backfill Active→IsPublic=1; (2) Asymmetric RLS FILTER adds `OR (IsPublic=1 AND SESSION_CONTEXT('public_forms_bypass')='true')`; BLOCK fn prevents flag INSERT mutation; (3) New `IReadDbConnection.QueryFirstOrDefaultWithPublicFormsBypassAsync` (Dapper, bypass context) + `IFormTemplateRepository.GetPublicWithFieldsAsync` (EF, tenant-pinned); (4) 3 handlers updated (GetPublicFormQuery, SubmitPublicFormCommand, GetPublicSignatureDocHashQuery) with `AND IsPublic=1` by-Code filter. **Tests:** `PublicFormsLeastPrivilegeTests` (6, rls_test_app non-db_owner, A/B non-vacuous) + fixture GRANT. Build 0W/0E, 1,791 PASS, code-review APPROVE_WITH_NITS. **Note:** NEW templates IsPublic=0 until admin-toggle ships (existing Active backfilled). **Follow-ups:** `PUBLIC-FORMS-ISPUBLIC-ADMIN-TOGGLE` (T4 FE/BE toggle) RESOLVED same-day · `FORMS-GET-TEMPLATE-PROJECTION-CONSENT-WORKFLOW-GAP` (T4 FE-state) RESOLVED same-day.

- **FORMS-GET-TEMPLATE-PROJECTION-CONSENT-WORKFLOW-GAP** — ✅ RESOLVED 2026-07-09 (🔵 T4, FE-state bug) — Fixed `GetFormTemplateQueryHandler` single-template GET which omitted `RequiresConsent`, `ConsentText`, `ApprovalWorkflowDefinitionId` from its Dapper projection, causing FE Settings-tab consent + approval-workflow toggles to be write-only (PUT persisted, GET returned defaults on reload). **Fix:** Added 3 columns to `templateSql` SELECT + `FormTemplateRow` materialization class + projected via named args into `FormTemplateDto` ctor, mirroring the `IsPublic` projection added earlier same session. Additive, no migration, no public-contract change (DTO already declared the fields as trailing optional params). **Tests:** Forms.Integration 14/14 (new regression test `GetFormTemplateById_ProjectsConsentAndApprovalWorkflow_AfterUpdate` covers create→baseline→PUT-set→GET-reflects), build 0W/0E, code-review APPROVE_WITH_NITS.

---

## 2026-07-09 (Microservices Wave 0 P6 NetworkPolicy Egress Narrowing)

> Security hardening on Helm NetworkPolicy template closing egress exfil hole. Removed to:-less 443/4317/4318 → explicit per-CIDR allowlist for external 443 + gated OTEL behind config flag. New architecture guard prevents future regressions. Follow-up filed for operator-coupling documentation (non-blocking).

- **MSVC-WAVE0-P6-NETWORKPOLICY-EGRESS-NARROW** — ✅ RESOLVED 2026-07-09 (🟡 T3, security, infra) — Helm `infra/helm/aqtcorefw/templates/network-policy.yaml` narrowed critical egress exfil hole: previously ONE egress list-item `- ports: [443, 4317, 4318]` with no `to:` selector was admitting traffic to ANY destination over TLS + OTEL ports, contradicting the `policyTypes: [Ingress, Egress]` default-deny posture. **Fix:** (1) External 443 (Keycloak IdP `auth.aqtcorefw.internal` + remote signing providers) → new values `networkPolicy.egress.httpsCidrs: []`; when populated → per-CIDR `to: ipBlock` strict rules; empty (default) → ONE documented broad-443 fallback (vanilla NetworkPolicy has NO FQDN egress; full lockdown deferred to mesh-pilot P7). (2) OTEL 4317/4318 → gated behind `networkPolicy.egress.otel.enabled` (default false → renders nothing; 0 collector wired today) scoped to `otel.namespace` (default `monitoring`) when enabled. (3) DNS 53 left broad (intentional). (4) **NEW architecture guard** `tests/architecture/AqtCoreFW.Architecture.Tests/NetworkPolicyEgressGuardTests.cs` (3 xUnit facts, static-source YAML assertion — no helm/conftest in toolchain + CI parked, so render test would false-green; static parse verifies no to:-less egress rule remains). Build 0W/0E, Architecture.Tests 161/161, code-review APPROVE_WITH_NITS (no blockers). **Default render provably strictly tighter** than before (removes any-destination 4317/4318; keeps broad 443 with fallback doc). No new egress opened. **Follow-up filed:** `MSVC-P6-OTEL-COUPLING-DOC` (T4, document operator coupling when collector wired; YAGNI until then).

---

## 2026-07-08 (Sóng-0 Phase-2 COMPLETE + Phase-2 R1 Workflow→Forms Status Write Outbox Routing)

> **Phase-2 R1 Addendum (same-session 07-08, post-defect-fix follow-up):** Workflow→Forms FormSubmission status-write (Approve/Reject on workflow completion) routed through Outbox (durable at-least-once) instead of in-process handler that could silently strand submission in Pending on cross-module write failure. **New:** `WorkflowInstanceFinalizedIntegrationEvent` (IExternalDomainEvent + ITenantScopedIntegrationEvent, IDs-only) raised atomically on `WorkflowInstance.ExecuteTransitionCommand` + `SlaBreachCheckerJob` when transitioning to final state (captured by `OutboxInterceptor` into `workflow.OutboxMessages` in same SaveChanges). **New:** `WorkflowFinalizedFormSubmissionConsumer` (MassTransit IConsumer, idempotent by effect) maps final-state → approve/reject via `IFormSubmissionStatusClient`. **Removed:** in-process handler (status write now eventual-durable, behavior change). **No migration** (outbox infra pre-existing). Unblocks microservices extraction (Workflow→Forms now async-only). Code-review found + fixed 1 CRITICAL (dedup ledger would defeat retry-durability → removed). Workflow.Domain 145/145 · Workflow.Application 640/640 · Forms.Application 329/329 · Arch 158/158. **Follow-up filed:** `WF-PARALLEL-JOIN-FINAL-NO-STATUS-WRITE` (T4, parallel-join workflows never fire status-write event).

> **Phase-2 Main (same-session 07-08, defect closure):** Addendum: a 4th Table-mode defect surfaced in code-review + fixed — `SubmitPublicFormCommandHandler` never wrote TableField child rows (anonymous public Table-mode submits silently dropped them). Fixed by injecting `ITableFieldDataRepository` + a per-TableField `InsertRowsAsync` loop inside the atomic span (parity with `SubmitFormCommandHandler`). Resolves filed FU `PUBLIC-FORMS-TABLEFIELD-CHILDROW-DROP`. Regression guard added; Forms integration 37/37.

> Phase-2 scope-audit: the C2 cross-module-write-atomicity fix + tests were ALREADY shipped in Phase-1 (slices 1B-1 `fd2cb118` / 1B-2 `9eefc562`). Phase-2 delivered: (1) write-path consistency inventory artifact (`plans/reports/architect-260708-0823-write-path-consistency-inventory-report.md`; 13 cross-module write-sites classified a/b/c; 0 residual atomic-local gaps); (2) Table-mode atomicity test harness + 2 integration tests (synchronous materialization harness + happy-path + child-row rollback via real `InsertRowsAsync` SqlException → submission+outbox rollback); (3) 3 pre-existing production defects discovered + fixed (Table-mode forms were non-functional end-to-end since Phase-07 T1→T2 entity retier 2026-06-10). **Defect B (blocker):** dynamic Table-mode BI views joined `forms.FormSubmissions` on `s.Id` (bigint) vs table's `SubmissionId uniqueidentifier` (PublicId) → SQL operand-type clash → `FormMaterializationService` swallowed silently → every Table-mode publish stuck `Materializing` forever. Fixed: view SQL joins `s.PublicId` (PublicId FK). **Defect C (blocker):** `FormDataPreprocessor.PreprocessData` default case passed raw `JsonElement` to Dapper → `NotSupportedException` on every Table-mode scalar submit. Fixed: normalize JsonElement→CLR type. **Defect A (medium):** no HTTP path to author TableField child fields (`AddFormFieldRequest` lacked `ParentFieldId`). Fixed: additive optional `ParentFieldId` on DTO + controller wiring. **API contract:** `AddFormFieldRequest` gained optional `parentFieldId` (nullable long) — backward-compatible. **Tests:** Forms.Application 320/320, Forms.Integration 36/36 fresh Testcontainers (2 new Table-mode atomicity tests), code-review APPROVE_WITH_NITS (0 blockers). **Files:** `FormTableMigrator.cs` + `FormTableMigrator.ChildTables.cs` (view fix); `FormDataPreprocessor.cs` (JsonElement fix); `AddFormFieldRequest.cs` + `FormTemplatesController.cs` (ParentFieldId wiring); new `FormSubmissionTableModeAtomicityTests.cs` test file. **Product impact:** Table-mode forms functional again end-to-end. **3 new follow-ups filed:** `PUBLIC-FORMS-TABLEFIELD-CHILDROW-DROP` (T3, public Table-mode silent child-drop), `FORMS-CONSENT-PRE-SPAN-ORPHAN` (T4, pre-span orphan consent), `WF-TRANSITION-STATUS-WRITE-NO-OUTBOX` (T3, workflow-transition no-retry on status write).

- **MSVC-WAVE0-P2-WRITE-PATH-INVENTORY-COMPLETE** — ✅ RESOLVED 2026-07-08 (T2, artifact) — write-path consistency inventory completed. Classified 13 cross-module write-sites (Forms SubmitPublicFormCommand, SubmitFormCommand, FormDataPreprocessor; Identity LockoutEscalationService, AccountAutoDisabledNotification; Audit AuditService; Workflow WorkflowTransitionedFormSubmissionHandler). All classified a/b/c (column-per-module atomicity guarantee tier). **0 residual atomic-local gap** (all paths audited; no "unowned" cross-tenant write found). Artifact: `plans/reports/architect-260708-0823-write-path-consistency-inventory-report.md` (13 write-sites, 3-tier classification, brief rationale per site).

- **MSVC-WAVE0-P2-TABLEFIELD-ATOMICITY-HARNESS** — ✅ RESOLVED 2026-07-08 (T3, testing, reusable) — Table-mode atomicity test harness + 2 integration tests. (1) `FormSubmissionTableModeAtomicityTests.cs` (new file, Forms.Integration.Tests) — reusable synchronous materialization harness: `BuildTableSubmissionAsync`, `ReadMaterializedRowCountAsync`, `ReadChildRowCountAsync` helpers. (2) Happy-path test: Table-mode submit → materialized rows inserted. (3) Rollback test: Table-mode with TableField children, `InsertRowsAsync` throws SqlException → submission+outbox atomically rolled back. Reusable pattern for future cross-module atomicity scenarios. Tests: Forms.Integration 36/36 green (2 new Table-mode tests).

- **WF-TRANSITION-STATUS-WRITE-NO-OUTBOX** — ✅ RESOLVED 2026-07-08 (T3 🟠, phase-2-R1, BE reliability, durable cross-module write) — routed FormSubmission status-write (Approve/Reject on workflow completion) through Outbox (durable at-least-once) instead of in-process handler that could silently strand submission in Pending if cross-module write failed post-workflow-transition commit. **New contract:** `WorkflowInstanceFinalizedIntegrationEvent` (SharedKernel.Contracts/Events, IExternalDomainEvent + ITenantScopedIntegrationEvent, IDs-only) raised atomically by `WorkflowInstance.ExecuteTransitionCommand` + `SlaBreachCheckerJob` when transitioning to final state (captured by `OutboxInterceptor` → `workflow.OutboxMessages` in same SaveChanges). **New consumer:** `WorkflowFinalizedFormSubmissionConsumer` (Forms.Application/EventHandlers, MassTransit IConsumer, idempotent by effect; no dedup ledger — status writes naturally idempotent) maps final-state-name → approve/reject via `IFormSubmissionStatusClient` at-least-once with retry. **Removed:** in-process `WorkflowTransitionedFormSubmissionHandler` (status write now eventual-durable rather than in-request-synchronous — behavior change: fires after broker dispatch, not before response). **No migration needed** (`workflow.OutboxMessages` + outbox schema pre-existing). Impact: cross-module FormSubmission status writes now durable; enables microservices extraction (Workflow→Forms now async-only, not request-scoped). Verify: Workflow.Domain 145/145, Workflow.Application 640/640, Forms.Application 329/329 (9 new consumer tests + 1 parity test), Arch 158/158, build 0W/0E. Code-review found + fixed 1 CRITICAL (dedup ledger would defeat retry-durability → removed; effect-idempotent only). Files: new `WorkflowInstanceFinalizedIntegrationEvent.cs` (Contracts) + new `WorkflowFinalizedFormSubmissionConsumer.cs` (Forms.Application) + new 9 consumer tests. **Carry-fwd:** `WF-PARALLEL-JOIN-FINAL-NO-STATUS-WRITE` (T4, discovered in review: parallel-join workflows never fire status-write event because ApplyParallelJoinTransition never sets CompletedAt).

- **MSVC-WAVE0-P2-TABLEFIELD-DEFECT-FIXES-3X** — ✅ RESOLVED 2026-07-08 (T2, production bugs) — **3 pre-existing Table-mode production defects fixed.** (A) **Defect B (blocker, BI-view join type-mismatch):** dynamic Table-mode BI views joined `forms.FormSubmissions s ON s.Id = ...` (raw bigint id join vs the table's `SubmissionId uniqueidentifier` PublicId addressing) → SQL Server operand-type clash → caught by view definition parser, swallowed silently by `FormMaterializationService` → every Table-mode publish event stuck materializing forever. **Root:** Phase-07 entity retier (2026-06-10) changed `FormSubmission.Id` from int→bigint, added PublicId as the addressing key; BI view join not updated. **Fix:** view SQL now joins `s.PublicId` (PublicId uniqueidentifier FK) — tables `FormTableMigrator.cs` (line 185 view definition) + `FormTableMigrator.ChildTables.cs` (child join). (B) **Defect C (blocker, Dapper type mismatch):** `FormDataPreprocessor.PreprocessData` default case (catch-all for scalar/union/complex fields) passed raw `JsonElement` to Dapper SQL parameters → Dapper `NotSupportedException` (JsonElement not a mapped CLR type in SqlClient) on every Table-mode scalar submit. **Root:** Phase-07 forms.FormTableRows.CellData migrated to JSON column; PreprocessData default case never normalized the JsonElement. **Fix:** `FormDataPreprocessor.cs` `ProcessScalarOrJsonField` method: if value is JsonElement, call `JsonElement.AsJsonValue()` to return a primitive or object that Dapper can serialize. (C) **Defect A (medium, missing HTTP path):** no HTTP path to author Table-mode with TableField child fields (columns); `AddFormFieldRequest` DTO lacked `ParentFieldId` field to specify parent field; controller `POST /api/v1/forms/templates/{publicId}/fields` routing the body directly to a `CreateFormFieldCommand` with no parent-field support. **Fix:** `AddFormFieldRequest.cs` gains optional nullable `long? ParentFieldId` field (backward-compatible); `FormTemplatesController.cs` maps `req.ParentFieldId` → command; `CreateFormFieldCommandHandler` routes to `ITableFieldDataRepository.InsertAsync` if ParentFieldId is set. **Files modified:** FormTableMigrator.cs (view SQL), FormTableMigrator.ChildTables.cs (view SQL), FormDataPreprocessor.cs (JsonElement normalization), AddFormFieldRequest.cs (ParentFieldId field), FormTemplatesController.cs (DTO→command mapping). **Verify:** Forms.Application 320/320, Forms.Integration 36/36 (fresh Testcontainers), code-review APPROVE_WITH_NITS (0 blockers). **Impact:** Table-mode forms functional again end-to-end (were dead since retier). Unblocks Table-mode feature usage.

> **Same-day FU closures (07-08 late):** the consent/child-drop FU pair + the AUTH-SUBMIT contract fix + the SharedKernel cref-hygiene FU, all closed 07-08. Commits `c736d25a` (consent-atomicity enlist), `4839c102` (auth-consent DTO), `65235023` (cref-hygiene).

- **PUBLIC-FORMS-TABLEFIELD-CHILDROW-DROP** — ✅ **RESOLVED 2026-07-08 (same session, fix option a — user chose support over reject)** — `SubmitPublicFormCommandHandler` now injects `ITableFieldDataRepository` and writes TableField child rows (`forms.SubmTableRows_{templateId}_{tableFieldId}`) inside its atomic submit span, mirroring `SubmitFormCommandHandler` (public-path parity). Anonymous public Table-mode submissions with TableFields now persist child rows atomically — a child-row failure rolls back the parent row + outbox event together (previously the parent row persisted while all child rows were silently dropped). Regression guard: `FormSubmissionTableModeAtomicityTests.Submit_TableMode_PublicPath_AnonymousHttp_InsertsChildRowAndEnqueuesExactlyOneOutboxRow`. Verified Forms unit 320/320 · Forms integration 37/37.

- **FORMS-CONSENT-PRE-SPAN-ORPHAN** — ✅ **RESOLVED 2026-07-08 (fix-a, user-chosen over document-and-accept)** `c736d25a` — the PDPL consent write is now enlisted onto the atomic submit span instead of committing pre-span on its own IdentityDbContext transaction. `ConsentRepository` made enlist-aware (optional `ICrossModuleUnitOfWork` + `EnsureEnlistedAsync` before `SaveChangesAsync`, mirroring `GenericRepository`); the consent block (`RecordConsentAsync` + `LinkConsent`) moved inside the `try` span in both handlers, before `AddAsync`. A rolled-back submit now leaves NO orphaned consent (consent + submission + outbox commit/roll-back together, cross-schema on one local SQL transaction — the Sóng-0 CrossModuleUnitOfWork primitive applied to a real site). The row's suggested `uow.ExecuteAsync` API does not exist; the real seam is the enlist-aware repo. Regression guard: `FormSubmissionConsentAtomicityTests` (incl. `PublicSubmit_ConsentThenInSpanFailure_RollsBackConsentAndSubmission` = 0 orphan proof) + 11 call-ordering unit tests. Verified Forms.Integration 41/41 · Forms.Application 340/340 · Identity.Application 426/426 · build 0/0. code-review APPROVE (enlist-timing safe — `AuditDbContext` prod precedent). Plan `plans/260708-1502-forms-consent-pre-span-orphan-atomicity/`.

- **AUTH-SUBMIT-NO-CONSENTGIVEN** — ✅ **RESOLVED 2026-07-08 (re-tiered T4→T3 after empirical scout disproved "product-shape question" — live FE↔BE contract break)** `4839c102` — the authenticated `SubmitFormRequest` DTO (`FormSubmissionsController`, `POST /api/v1/forms/submissions`) had NO `ConsentGiven` field, so System.Text.Json silently dropped the `consentGiven` property from the FE request → the controller always built `SubmitFormCommand` with `ConsentGiven=false`, causing any PII+`RequiresConsent=true` submit to always reject 422 GOV_FRM_020. FE `form-submit-modal.tsx:72` + `form-submission-api.ts:162` prove authenticated PII+consent is intended. **Fix:** `SubmitFormRequest` gained `bool ConsentGiven = false` (between `Data` and `Signature`, mirroring `PublicSubmitRequest`); `Submit` action wires `ConsentGiven: request.ConsentGiven`. No FE change, no migration, no contract change (`SubmitFormCommand.ConsentGiven` already existed). Verified Forms.Integration 43/43 (2 new tests: authenticated PII+consent→200 + one linked ConsentRecord; unconsented→422 GOV_FRM_020), Forms.Application 340/340, build 0 errors. code-review APPROVE_WITH_NITS. Plan `plans/260708-1621-auth-submit-consentgiven-contract-fix/`.

- **SHAREDKERNEL-BEGINASYNC-CREF-AMBIGUOUS** — ✅ **RESOLVED 2026-07-08** `65235023` — the 4 CS0419-ambiguous `<see cref="BeginAsync"/>` (3× `ICrossModuleUnitOfWork.cs` + 1× `IOutboxWriter.cs`) qualified to the canonical `BeginAsync(CancellationToken)` overload (mirroring the already-correct `:47` usage). SharedKernel now builds clean under BOTH normal and `-warnaserror` (0 warnings, 0 errors). Doc-comment only — no code/behavior/contract change. code-review APPROVE (no stragglers repo-wide). Lesson: [[feedback-method-overload-breaks-unqualified-doc-cref]].

---

## 2026-07-07 (Sóng-0 Phase-1 COMPLETE — 1B-3 WONTFIX decision + doc fix + Phase-1 close-out)

> All 5 Phase-1 slices resolved (1A–1E committed; 1B-3 WONTFIX-recorded). 1B-3 decision: route `LockoutEscalationService` → `AccountAutoDisabledIntegrationEvent` through outbox → WONTFIX Option A (keep direct at-most-once). Rationale: low materiality (auto-disable persists independently on UserManager write; notifications are best-effort by design; separate audit path is at-least-once); blocker pattern (UserManager not-UoW; PII at-rest risk on durable outbox). Doc correction: `AccountAutoDisabledIntegrationEvent` XML-doc fixed to accurately state it is transient direct-publish (never persisted → PII acceptable) with full WONTFIX rationale in code comment. Contracts suite 47/47 green (PII IDs-only guard correctly exempts non-external-domain events).

- **MSVC-WAVE0-P2-OUTBOX-LOCKOUT-1B3-WONTFIX-DECISION** — ✅ RESOLVED 2026-07-07 (T2, **decision final**) — route `LockoutEscalationService:82` → `AccountAutoDisabledIntegrationEvent` through outbox → **WONTFIX Option A: KEEP DIRECT AT-MOST-ONCE.** **Rationale:** (1) **Low materiality:** `AccountAutoDisabled` itself persists independently via `UserManager.SaveChanges` (no UoW seam for IOutboxWriter co-write) → event loss drops only courtesy notification, not security action. (2) **Notifications best-effort by design:** `NotificationsConsumer` swallows email failures, zero retries (at-most-once by contract = outbox routing adds 0 benefit). (3) **Audit path separate:** auto-disable audit-logged on the now-at-least-once audit path (Slice 1B-2) → no audit gap under S2 extraction. (4) **Two real blockers:** B1 = `UserManager` owns its own `SaveChanges` on Identity DbContext (no seam for atomic `IOutboxWriter` co-write, unlike Forms/Audit); B2 = event carries `UserName`+`Email` (PII) → routing through durable outbox persists PII at-rest (violates IDs-only invariant), requiring strip-to-IDs + consumer re-hydration (design tax). **Doc fix:** corrected `AccountAutoDisabledIntegrationEvent` XML-doc (falsely claimed "IDs + metadata only, NO PII payloads" while carrying UserName+Email) to accurately describe transient direct-publish event with PII acceptable for in-transit notification (never persisted); added matching WONTFIX rationale code-comment at publish site in `LockoutEscalationService`. Build clean; Contracts suite 47/47 (IDs-only guard correctly exempts this event — it is NOT `IExternalDomainEvent`, so it is never outbox-captured).

---

## 2026-07-07 (Sóng-0 Phase-1 Slice 1E shipped — comprehensive at-least-once test)

> Test-only comprehensive suite for outbox at-least-once guarantee under broker failure + network recovery. 3 new test files (white-box dispatcher spy, redelivery+dedup integration, schema-allowlist smoke). Phase-1 now COMPLETE (5/5 slices 1A–1D committed, 1E test-only). Only 1B-3 Lockout deferred (decision-blocked). All LOCAL, NOT pushed. Plan `plans/260706-1237-song0-foundation-outbox-atomicity-authz-decouple/`.

- **MSVC-WAVE0-P2-OUTBOX-AT-LEAST-ONCE-SLICE-1E** — ✅ SHIPPED 2026-07-07 (TEST-ONLY) — **Comprehensive at-least-once test suite:** 3 new test files + 1 fixture edit, 0 production code. (1) `OutboxAtLeastOnceDispatchTests.cs` (Messaging.Integration.Tests) — WHITE-BOX: real `OutboxDispatcher` + real SQL `OutboxStore` + fault-injecting `IBus` spy. Proves broker unavailable → row survives SQL (`ProcessedAtUtc` NULL, `RetryCount++`, claim released), re-claimed on broker recovery. (2) `OutboxRedeliveryIdempotencyTests.cs` (Messaging.Integration.Tests) — at-least-once redelivery + real `IProcessedMessageStore` ledger dedup = effect-once. (3) `OutboxSchemaAllowlistSmokeTests.cs` (Startup.SmokeTests) — booted `ApiHostFixture` resolves REAL `IOutboxSchemaRegistry`, asserts {forms, audit} ⊆ allowlist (guards `OutboxInterceptor`-path silent-loss footgun). (4) `OutboxStoreFixture.cs` additive helpers only (BuildDispatcher, SeedResolvableRowAsync, ReadRowStateAsync, OutboxRowState record). **Verify:** Messaging.Integration 12/12 ×2 runs, SmokeTests pass, code-review DONE (3 minor non-blocking). Tests compile + run, no false-green (bus spy proves broker-down caught). **Phase-1 COMPLETE:** slices 1A–1E shipped (1A version-token, 1B-1 Forms+UoW-H1, 1B-2 Audit+UoW-H2, 1C idempotency-ledger, 1D consumer-scope-tenant-pin, 1E comprehensive-test). **Remaining defer:** 1B-3 Lockout (decision-blocked UserManager-not-UoW pattern). Build 0W/0E, LOCAL commits (not pushed).

---

## 2026-07-04 (Signing provider-allowlist guard + BE enrollment-kind mapping + audit/metric)

> Security hardening: fail-closed early guard in `SignatureOrchestrator.RequestSignatureAsync` prevents any signing request for a provider the assigned signer is not enrolled for. NEW infrastructure service `ProviderEnrollmentKindMap` maintains the mapping of providerKey to `EnrollmentKind` (per D6 compliance, vendor keys stay outside Domain/Application layer). Audit logging + Prometheus metrics for all denials. Data audit clean: 0 signers newly-denied, both enrollment kinds (USB/Mysign) already mapped. VNPT SmartCA revival now unblocked by safe-by-default allowlist behavior — adapter/config preserved, deprecation reversed. All suites green (426+339+150+169 tests), full build 0W/0E, PUSHED. Commits PUSHED. Follow-up VNPT revival checklist documented in backlog.

- **SIGN-REQUEST-PROVIDERKEY-NO-TENANT-ALLOWLIST** — ✅ RESOLVED 2026-07-04 (T4 🔵, BE security hardening) — **Fail-closed early provider allowlist:** denies 403 ForbiddenError any signing request (`SignatureOrchestrator.RequestSignatureAsync`) where the assigned signer has NO Approved+Active enrollment matching the requested provider's `EnrollmentKind`. Handles all 3 provider-key cases: (1) unknown key → 404 (preexisting); (2) registered-but-unmapped provider → 403 (NEW guard); (3) known enrollment kind mismatch → 403 (NEW guard). 0-enrollment signers defer to late binding guard (backward-compat, no surprise breaks). **NEW files:** `IProviderEnrollmentKindMap` (Application interface, Identity.Application/Signing/ProviderEnrollmentKindMap.cs) — contract for the kind lookup; `ProviderEnrollmentKindMap` (Infrastructure, Identity.Infrastructure/Services/Signing/ProviderEnrollmentKindMap.cs) — table `["usb-vgca"]=EnrollmentKind.UsbTokenVgca, ["remote-viettel-mysign"]=EnrollmentKind.RemoteViettelMysign` (vendor literals live here per D6 compliance boundary); `SigningProviderAllowlistGuard` (Application, Identity.Application/Signing/SigningProviderAllowlistGuard.cs) — the guard logic (enrollment lookup + deny); `SigningMetrics` (Application, Identity.Application/Telemetry/SigningMetrics.cs) — NEW OTel meter `AqtCoreFW.Identity` with counter `signing_provider_allowlist_denied_total{provider,tenant,reason}`. **DI & Config:** `IProviderEnrollmentKindMap` (singleton) + `ISigningProviderAllowlistGuard` (scoped) registered in `IdentityInfrastructureRegistration`; OpenTelemetry meter added to `OpenTelemetryConfiguration.cs`. **Audit:** every deny writes row (action `SIGNING_PROVIDER_ALLOWLIST_DENIED`, audit.SigningEvents). **Alert rule:** `signing_provider_allowlist_denied_total > threshold` — documented ops-doc only (no startup seeding, product decision pending). **Tests:** (unit 9/9) SigningProviderAllowlistGuardTests, ProviderEnrollmentKindMapTests; (arch 1) ProviderEnrollmentKindMapDriftArchTests (asserts every registered provider key is mapped — fail-closed drift guard); (integration 4) SigningProviderAllowlistHttpTests (self-sign 403 + audit row, Forms path 403, unknown→404 fallthrough, 0-enrollment defer to binding guard). **Verify:** build 0W/0E, suites green Identity.Application.Tests 426/426, Identity.Infrastructure.Tests 339/339, Architecture.Tests 150/150, Identity.Integration.Tests 169/169. Data audit (pre-merge gate): 0 Approved signers deny-affected; both enrollment kinds already in place; only 2 orchestrator callers (HTTP + Forms client), both guarded. Commits PUSHED origin/main.

---

## 2026-07-03 (Signature image preview + PAdES stamp corrections + dotnet-ef tool pin)

> Post-commit session wrap-up. FE signature profile image preview endpoint + modal dual-path (upload/canvas draw) unblocks end-to-end self-service visible signing. PAdES stamp rendering: signer name from cert subject (authenticity), stamp time in Vietnam local time (UX clarity), per-profile visibility toggles (admin control), user-editable reason field. Dotnet-ef tool version pin fixes CI/CD launch failures on runtime 10.0.8. All committed + pushed origin/main + bitbucket.

- **SIGN-PROFILE-IMAGE-PREVIEW-ENDPOINT** — ✅ SHIPPED 2026-07-03 (`adc78d0b`, 2nd ship, FE + BE) — NEW `GET /api/v1/me/signature-profiles/{publicId}/images/{visualType}` (GetMySignatureImageQuery) streams stored signature image by slot (visualType "initial"/"full"). Owner-scoped 404 empty/not-owned. FE consumer: `signature-image-upload-modal.tsx` preview `<img>` via `signatures-api.fetchSignatureImageBlob`. DTO: `SignatureProfileDto` gains `hasFullImage` + `hasInitialImage` (per-slot presence). Tests: Identity.Application 11/11 integration. Full suite 1260/1260 + arch 149/149 GREEN.
- **SIGN-PROFILE-IMAGE-UPLOAD-MODAL-FE** — ✅ SHIPPED 2026-07-03 (`adc78d0b`, FE) — dual-path file + canvas draw modal (`signature-image-upload-modal.tsx`, antd Upload + promoted SignatureWidget → `web/src/shared/components/signature-widget.tsx` git rename). Two independent slots (FullSignature/InitialSignature), toggle per slot, canvas→PNG conversion, POST to existing BE endpoint. Tests: vitest 8/8 (image modal 8 + widget re-usage 3 + DTO 3 = 39 total new/modified tests). Full suite 1260/1260 GREEN.
- **PADES-STAMP-SIGNER-NAME-FROM-CERT** — ✅ SHIPPED 2026-07-03 (`adc78d0b`, BE) — PAdES "Ký bởi" stamp now renders cert subject CN/O (via `VgcaCertSubjectParser.Classify`) instead of profile.DisplayName. More authentic cert representation. File: `PadesTransactionInitiator`. Tests: unit 22/22 new.
- **PADES-STAMP-TIME-VIETNAM-LOCAL** — ✅ SHIPPED 2026-07-03 (`adc78d0b`, BE) — Stamp timestamp renders in Vietnam local time (UTC+7 fixed offset) instead of UTC. User clarity: signing ~7h earlier UTC displays correct local time. File: `PadesTransactionInitiator`. Tests: unit 22/22 signature-stamp-composer tests GREEN.
- **PADES-STAMP-REASON-FIELD** — ✅ SHIPPED 2026-07-03 (`adc78d0b`, BE + FE + Contract) — NEW `PadesSignParameters.Reason` + `CreateSigningRequestPadesRequest.Reason` (user-entered "Lý do ký" distinct from Mysign DocumentName, optional, blank omits line). FE: input in `sign-uploaded-file-pades-modal.tsx` → `use-sign-uploaded-file-pades` hook. Tests: unit 22/22 signature-stamp-composer + integration GREEN.
- **PADES-STAMP-VISIBILITY-TOGGLES** — ✅ SHIPPED 2026-07-03 (`adc78d0b`, BE + FE + Entity + Migration) — NEW `UserSignatureProfile.ShowSerial`/`ShowIssuer` bool cols (default true) — admin toggles in profile-form modal (2 Switch) hide Serial/Issuer lines. Migration `20260702153217_AddSignatureProfileStampVisibilityToggles` (2 bit cols). DTO: `SignatureProfileDto` gains `showSerial`/`showIssuer` fields. File: `SignatureStampComposer` skips empty/toggled lines. Tests: unit 22/22 signature-stamp-composer tests GREEN.
- **DOTNET-EF-TOOL-PIN** — ✅ SHIPPED 2026-07-03 (`353bc3ac`, 1st ship, build tooling) — `.config/dotnet-tools.json` fixed: `dotnet-ef` version 10.0.5 → 10.0.3, rollForward false → latestFeature. Resolves launch failure on runtime 10.0.8 (semantic-version semver constraint mismatch). No code change; build-only. Unblocks CI/CD + EF migration workflows.

---

## 2026-07-02 (FE sign-button UX gate + test-infra XHR worker crash fix + cleanup jobs least-priv + RLS policy matrix test)

> Multiple `/ck-debug` + `/ck-cook` session closes. FE `/files` sign-button UX hardening (provider-capability + enrollment-status gating, no BE change). Test-infra hardening (XHR network guard + classification static import). Background-job security follow-ups (per-tenant session context pinning via TenantScopedSweep helper). All code-reviewer APPROVE_WITH_NITS, 0 blockers. Commits NOT pushed (local session work).

- **FILES-SIGN-BUTTON-PROVIDER-ELIGIBILITY-GATE** — ✅ RESOLVED 2026-07-02 (T4 🔵, FE UX) — `/files` sign buttons gated by provider capability + enrollment status (USB requires `ClientSideSigning`, PAdES requires `ServerSideSigning` + active+approved `RemoteViettelMysign` enrollment). No BE change, no API contract change. Disabled state shows guidance tooltip. Tests 11/11 vitest, full suite 1255/1255 GREEN; code-reviewer APPROVE_WITH_NITS (isActive-omission + revoked-test fixed). Committed local.
- **FE-TEST-SUITE-XHR-WORKER-CRASH** — ✅ RESOLVED 2026-07-02 (T4, test-infra) — synchronous network guard in `web/src/test/setup.ts` intercepts XHR.open/.send + stubs fetch; converts unmocked requests into clean promise rejections instead of async worker-fatal errors. Also fixed latent async timeout in `data-classification/__tests__/classification-page.test.tsx` (static import). Suite 1255/1255 GREEN ×2 consecutive runs (was intermittent crash exit 127). Code-reviewer APPROVE_WITH_NITS (all 3 nits applied).

---

## 2026-07-01 (plan `260623-1950` self-service signature-profile image management shipped)

> `/ck-cook` final slice of the signature-profile visible-stamp feature arc. User added FE image-upload UI (dual-path file + canvas draw) to finalize end-to-end self-service visible signing. BE endpoints ready since Phase 4 (`POST /api/v1/me/signature-profiles/{publicId}/images`). Feature-complete: profile CRUD (P6A) · enrollment approval (P6C) · visible-stamp placement picker (P6D) · multi-provider enroll (P6B) · image upload/draw (new) = all self-service signing UX shipped. Commits PUSHED.

- **SIGN-PROFILE-IMAGE-UPLOAD-FE** — ✅ SHIPPED 2026-07-01 (plan `260623-1950` P6 Post-Slice, FE) — **Dual-path signature image management:** file upload (antd Upload, PNG/JPEG, ≤500KB via existing magic-byte validation) + canvas draw (reusing `SignatureWidget` promoted to shared). **New file:** `web/src/features/digital-signature/images/signature-image-upload-modal.tsx` (Modal wrapper, 2 slots FullSignature/InitialSignature, toggle upload/draw, `dataUrlToFile` canvas→PNG conversion, POST to existing BE endpoint). **Widget promotion:** `signature-widget.tsx` mv `web/src/features/forms/builder/` → `web/src/shared/components/` (git rename); 2 importers repointed (`formfield-editor.tsx`, `signature-field-renderer.tsx`). **BE DTO additions (Application-only, NO migration):** `SignatureProfileDto` gained `HasFullImage`/`HasInitialImage` bools — exposed by `SignatureOverviewQuery` + `ListMySignatureProfilesQuery` for FE per-slot "has image" badges. **i18n:** new keys `profile.signatures.image.*` + `col.images` (vi+en). **Tests:** FE vitest 39/39 (image modal 8 tests + widget re-usage 3 tests + profile queries 3 new DTO contract tests). **Verify:** build 0E/0W; BE Identity.Application 415/415, Arch 149/149; FE typecheck clean, eslint 0; code-reviewer DONE 0-blocking, all AC met. **Impact:** Unblocks end-to-end self-service visible signing — profile image is a hard prerequisite of `PadesTransactionInitiator` (SIGNING_PADES_PROFILE_IMAGE_MISSING gate).

---

## 2026-06-30 (plan `260623-1950` Phase 6 Slice C FE admin dashboard shipped)

> `/cook` Phase 6 Slice C FE (admin dashboard for self-service signing cert enrollment approval review). Feature-complete admin workflow: paginated pending-enrollment queue (kind/serial/issuer/enrolledAt), Approve/Reject actions with Popconfirm and reason-required modal, tenant-scoped (separate route from cross-tenant config admin). **Status:** full suite 1229/1229 + typecheck + eslint all GREEN; code-reviewer APPROVE_WITH_NITS (0 blockers). Commits: NOT pushed (part of larger plan). Remaining slices: P6B (multi-provider enroll, BLOCKED on missing set-CCCD endpoint) · P6D (placement UI + signing-prompt).

- **P6-SLICE-C-FE-ADMIN-ENROLLMENT-APPROVAL** — ✅ SHIPPED 2026-06-30 (plan `260623-1950` P6 Slice C, FE) — standalone admin route `/admin/signature-enrollments` (JWT-tenant-scoped, separate from cross-tenant override-driven config admin to preserve scope isolation). **Design decision:** original plan said "fold into /admin/signature-config" but reversed — SignerCertEnrollmentAdminController is tenant-scoped (H4 from Phase 3, ignores X-Tenant-Id override) whereas config is cross-tenant; mixing scopes would violate authorization model. User approved deviation. **New files:** `web/src/features/digital-signature/enrollments/` (signature-enrollment-admin-types.ts, signature-enrollment-admin-api.ts hand-written react-query hooks, reject-enrollment-modal.tsx, signature-enrollment-admin-page.tsx, __tests__/ 4 tests) + routes/admin-signature-enrollments-page.tsx + app/router.ts registration + layouts/admin-menu-entries.ts (menu under Security & Compliance) + i18n keys vi+en. **BE API contracts consumed (already existed):** `GET /api/v1/admin/signer-cert-enrollments/pending?page&pageSize` (nested-meta PagedResult), `POST .../{{publicId}}/approve` (204), `POST .../{{publicId}}/reject` (204, {reason}). Gate: SYSTEM.CONFIG.MANAGE + RequiresMfa. **Tests:** 4 vitest covering enroll list render, approve with Popconfirm, reject with modal + reason, pagination. **Verify:** build 0E/0W; full suite 1229/1229; typecheck app + test; eslint clean; arch 149/149 no new cross-module adds. **Code-review:** APPROVE_WITH_NITS (MED: SECURITY_PATHS + 2 Low fixed).

---

## 2026-06-30 (plan `260623-1950` Phase 6 Slice A FE shipped)

> `/cook` Phase 6 Slice A FE (user profile signature management tab). Feature-complete foundation: self-service read/CRUD of signature profiles + enrollment status list. **Status:** full suite 1225/1225 + typecheck + eslint all GREEN; code-reviewer APPROVE_WITH_NITS (0 blockers). Commits: NOT pushed (part of larger plan). Remaining slices: P6B (multi-provider enroll, BLOCKED on missing set-CCCD endpoint) · P6C (admin approval fold) · P6D (placement UI + signing-prompt).

- **P6-SLICE-A-FE-PROFILE-SIGNATURES-MANAGEMENT** — ✅ SHIPPED 2026-06-30 (plan `260623-1950` P6 Slice A, FE) — refactored `profile-page.tsx` from flat layout to antd Tabs; existing "Thông tin" tab (user info + change-password) preserved. **NEW "Chữ ký số" tab:** `SignatureManagementTab` component + child components (profile CRUD form modal, enrollment list with Pending/Approved/Rejected status badges). **New files:** `web/src/features/profile/signatures/` (signature-types.ts, signatures-api.ts hand-written react-query, enrollment-list.tsx, signature-profile-form-modal.tsx, signature-management-tab.tsx) + i18n keys. **BE API contracts:** `GET /api/v1/me/signature-overview`, `GET/POST/PUT/DELETE /api/v1/me/signature-profiles`, `GET /api/v1/me/signature-cert-enrollments`. **Tests:** 3 vitest files (8 tests, all profile CRUD + enrollment render). **Verify:** build 0E/0W; full suite 1225/1225; typecheck app + test; eslint clean; arch 149/149 no new cross-module adds. **Code-review:** APPROVE_WITH_NITS (minor nits applied, 0 blockers).

---

## 2026-06-23 (backlog stale-archive pass — 24 inline-closed `[x]` rows extracted from 5 mixed legacy buckets + orphan-duplicate dedup, INVARIANT 260522-A GATE)

> Archived from `backlog.md` during `/cook "Stale-audit legacy buckets + inline-closed rows"` (light archive + dedup mode; deep empirical 49-row sweep already ran 2026-06-20). Each bucket below was MIXED (open `[ ]` rows remain) → only the `[x]`/CLOSED-STALE leaves were extracted; bucket headers + every open/deferred row left in place. **Structural dedup:** the 5 Audit-HMAC follow-ups (`P4-FU`/`P5-FU`/`FU-HMAC-ROTATION`/`FU-AUDIT-HMACKEY-LEGACY-REMOVE`/`FU-AUDIT-REPLAY-OPS`) existed as an orphan-duplicate block outside their canonical bucket — the orphan copy was removed, the canonical bucket retained. GATE: every moved-ID present here; every open `[ ]`-ID re-grepped present in `backlog.md` post-edit; tier counts T1–T5 unchanged.

**§ Phase-02b Workflow Client Deferred (04-17→04-18) — 9 rows (open kept: D1, D3, D8b)**
- **D2** — ✅ Done 2026-04-17 (HIGH prod-blocker) — rename `UpdateWithConcurrencyCheckAsync→UpdateWithStateGuardAsync` + EF `IsConcurrencyToken` on `WorkflowInstance.CurrentStateId`; layered concurrency (app FromStateId GOV_WFL_003 + DB token GOV_WFL_013); `WorkflowInstanceConcurrencyTests` 2 scenarios. Plan `260417-1413`. No schema change.
- **D4** — ✅ Done 2026-04-17 — `TryResolveInstanceForEntityAsync(entityType,entityId,tenantId,ct)` on `IWorkflowModuleClient` (PR #29 `1027bb03`, alongside `CreateInstanceByDefinitionKeyAsync`).
- **D5** — ✅ Done 2026-04-18 — rename `WorkflowTransition.RequiredRoleCode→RequiredPermissionCode` + migration `20260418025028` + raw-SQL callsites + DTO/Application/cross-module-client cascade. `d34549f9` (28 files, +1461/-72).
- **D6** — ✅ Done 2026-04-18 — `GetAvailableTransitionsQueryHandler` unified to permission-set filter (injects `IIdentityModuleClient`, `permissionCodes.Contains(RequiredPermissionCode)`); controller drops `actorRoleCode`; 6 tests. `509aa119`.
- **D7** — ✅ Done 2026-04-18 — remove `ActorRoleCode` from REST DTOs + FE cascade (11 FE files + 3 e2e specs; i18n `requiredRole*→requiredPermission*`). `375ee7f0`(BE)/`51b8fda8`(FE)/`67b88d8f`(http).
- **D8** — ✅ Done 2026-04-18 — pin `orval` to `7.21.0` exact (v8.6.2 = wholesale output rewrite, response-shape ripple); zero caller churn, typecheck 0 errors. (v8 migration deferred → open `D8b`.)
- **D8c** — ✅ Done 2026-04-18 — BE OpenAPI title `OpenApiVietnameseTransformer.cs:129` → `"AqtCoreFW API v1"`; stable schema filename `aqtCoreFWApiV1.schemas.ts`, zero caller churn.
- **D9** — ✅ Done 2026-04-18 — central `TestEffectivePermissionService` (~60 LOC, `X-Test-Roles` header → `PermCodes.All` 122 codes / empty→403) in `ApiFactory.ConfigureTestServices`; Workflow+Cases filter 14/14. Branch `feat/d9-main-integration-permission-stub`.
- **D9-FU** — ✅ Done 2026-04-18 — 6 adjacent 401s = single root (`ResolveTenantId()` throws when JWT `tenant_id` claim missing), not 6 distinct causes; 5 one-line `tenantId:` fixes + 1 broken-premise test delete. Integration 101/101.

**§ Test Infrastructure (04-17→04-25) — 5 rows (open kept: Re-enable CI integration tests, IDENTITY-VAULT-ENABLED-DEAD-CONFIG)**
- **xUnit local `[CollectionDefinition]` discovery fix** — ✅ Done 2026-04-17 — `SqlServerCollectionLocal.cs` added to 15 integration test projects; closes xUnit v2 cross-assembly discovery gap. (xUnit v3 migration = delete all 17 + `[AssemblyFixture]`.)
- **Integration test 403 stub closure (Audit+Forms+Organization+Cases)** — ✅ Done 2026-04-18 — stub `IEffectivePermissionService` (PR #36 pattern) in 4 projects; closed 18-19 HTTP-403 fails from Testcontainers→Local-SQL migration; 33/33 green. PR #38 `73295ce3`.
- **Email channel integration test — `IEmailSender` stub** — ✅ Done 2026-04-18 — bundled `FakeEmailSender.cs`+`FakeSmsProvider.cs` in `tests/shared/AqtCoreFW.Tests.SharedFixtures/`; Notifications 7/7, Integration 101/101.
- **Testcontainers.MsSql → Local SQL Server migration** — ✅ Done 2026-04-18 — hybrid fixture `TEST_USE_LOCAL_SQL=true` (~12s vs ~41s, 3.4×); 4 persistent test DBs. CI integration tests disabled per user. Plan `260418-0558`.
- **SQL Server dev runtime native → Docker Desktop** — ✅ Done 2026-04-25 (v2.132) — hard revert of v2.114 native; `sqlserver` container from `infra/docker/docker-compose.yml`; Identity smoke 12/12. ADR-006, plan `260425-0024`.

**§ Audit HMAC Vault Seeder Sweep (05-13, v2.137p15) — 2 rows (open kept: P4-FU, P5-FU, FU-HMAC-ROTATION, FU-AUDIT-HMACKEY-LEGACY-REMOVE, FU-AUDIT-REPLAY-OPS — canonical bucket)**
- **P3b-FU1** — ✅ Done 2026-05-13 22:55 ICT — dev `VAULT_ADDR=http://vault:8200`+`VAULT_TOKEN=dev-only-token` added to `api`+`authserver` env blocks in docker-compose; native `dotnet run` already covered by launchSettings. Prod unaffected.
- **P3b-FU2** — ✅ Done 2026-05-13 22:55 ICT — `#### Dev Vault re-seed after restart` subsection in `docs/deployment-guide.md` §5 (idempotent `vault-init` one-liner).

**§ FE Polling/Retry Storm (v2.133p16→p17) — 2 rows (open kept: POLL-FU2, POLL-FU2.5/INGRESS-SAME-ORIGIN, POLL-FU3)**
- **POLL-FU1** — ✅ SHIPPED v2.133p17 (2026-04-30) — module-scoped Axios circuit breaker in `api-client.ts` (≥3 status=0 fails/10s → 30s pause, normalized `BACKEND_UNAVAILABLE`, dedupe toast); 6 new + 13 existing tests pass; review 9.2/10. Plan `260430-2136`. 3rd defense-in-depth layer.
- **POLL-FU4** — ✅ SHIPPED 2026-04-30 — "Frontend Polling Guidelines (v2.133p16)" section in `docs/code-standards.md` (~110 lines, 4 rules + checklist). Docs-only.

**§ Outbox Dispatcher (v2.133p1→p15) — 5 rows (open kept: OUTBOX-DISP-FU2, OUTBOX-DISP-FU4)**
- **OUTBOX-1** — ✅ SHIPPED v2.133p11 (2026-04-29) — unique filtered index `UX_WorkflowInstances_Active_Entity` on `(EntityType,EntityId,TenantId) WHERE CompletedAt IS NULL` + idempotent consumer catch (`IsDbUpdateUniqueConstraintViolation`, SqlException 2627/2601). T1 450/450. Review 8/10.
- **OUTBOX-1-FU** — ✅ SHIPPED v2.133p15 — `UniqueActiveInstanceIndexPreflightCleanupTests.cs` (3) verify preflight cleanup + index uniqueness post-INSERT.
- **OUTBOX-1-DISP** — ✅ SHIPPED v2.133p12 (2026-04-29) — single-statement CTE atomic claim `WITH (UPDLOCK,READPAST,ROWLOCK)` + new `ClaimedAtUtc datetimeoffset NULL` on 14 outbox tables + `OutboxOptions.ClaimTimeout` (5min stale recovery); rolling-deploy safe. Review 8.5/10.
- **OUTBOX-DISP-FU1** — ✅ SHIPPED 2026-04-30 — clock-skew note expanded in `OutboxStore.cs:32-38` comments (C# `staleCutoff` vs `SYSUTCDATETIME()` benign at 5min; switch to server-side `DATEADD` if NTP drift). Comment-only.
- **OUTBOX-DISP-FU3** — ✅ SHIPPED 2026-04-30 — `OutboxStoreFixture.SeedRowsAsync` N INSERTs → chunked batch (size 100); race tests 7/7; round-trips 20→1, 250→3.

**§ BusinessProcess Definition MVP follow-ups (05-27) — 1 row**
- **P4-2B-FU3** — ✅ CLOSED-STALE 2026-05-27 (audit 260522-A) — the 3 anticipated runtime read methods all shipped WITH Slice 2C (v2.137p27f) on `IBusinessProcessRuntimeQueries` (`GetRuntimeContextAsync`/`GetStateFormTemplateIdsAsync`/`GetTransitionFormTemplateIdsAsync`); blocker self-resolved by the slice it gated on. Nothing to build (YAGNI).

## 2026-06-22 (backlog optimization — 24 closed rows archived + header line-3 condensed, INVARIANT 260522-A GATE)

> Archived from `backlog.md` during `/cook "Backlog optimization"` after empirical per-row verify (all ✅ DONE/CLOSED/FIXED, 0 live remainder). Open/blocked/deferred rows + the in-flight `VIETTEL-PENDINGTX-USERID-FROM-CCCD` (active plan P02-P04, go-live gate `callbackUrl` UNVERIFIED) left in place. Full narrative → `docs/project-changelog.md` + `docs/journals/` + cited commits. GATE: every KEEP-ID re-grepped present in `backlog.md` post-edit; every moved-ID present here.

**§ Backend Architecture & DI Remediation (06-21)**
- **ARCH-AUTHSERVER-MIGRATE-SUBSET-GUARD** — ✅ DONE 2026-06-21 (`/cook`) — each host declares startup-migrate set as data (`Api.StartupMigrationSets.Contexts`=20 · `AuthServer`=Identity+Messaging); `MigrateSetSubsetGuardTests` (4) asserts AuthServer⊆Api + tripwire; brittle `ModuleStartupWiringTests` text-scan rewired to reflection. arch 116→120. Review APPROVE_WITH_NITS.

**§ USB DocFlow signing E2E (06-17)**
- **SIGN-DOCFLOW-E2E-USER-VERIFY** — ✅ DONE 2026-06-18 — real-USB E2E confirmed by user; clean PDF signs end-to-end (BE `recordId=10003` one cycle, retry gone). 8-layer chain (route-404→probe-load→arity→FileName-URL→JWT-sub-claim→encrypt-gate→error-surface→camelCase) closed.
- **SIGN-DOCFLOW-HAPPYPATH-INTEGRATION-TEST** — ✅ DONE (upload half) 2026-06-18 — `Upload_ValidSignedPdf_ReturnsPascalCaseVgcaContract` posts signed PDF through real MVC+MinIO → 200 PascalCase + record Completed. Download half stays desktop-tool E2E (optional, lower value).
- **FE-BLOB-ERROR-MESSAGE-NORMALIZE** — ✅ DONE 2026-06-19 (`/cook`) — `api-client.ts` reads Blob error body (`readBlobErrorBody`) before `normalizeError` so typed BE codes survive `responseType:'blob'`; 2 interceptor tests. api-client 17/17.
- **SIGN-P5-PADES-SIGNED-PDF-DOWNLOAD-INTEGRATION** — ✅ DONE 2026-06-19 (`/cook`) — `PadesSignedPdfDownloadHttpTests` (real-SQL) 4 cases through full MVC: 200 %PDF · cross-tenant 404 · `SIGNING_PADES_PDF_NOT_READY` 400 · `SIGNING_PADES_NO_SIGNED_PDF` 400. Test-only.
- **SIGN-P5-PADES-COMPLETION-INTEGRATION** — ✅ DONE 2026-06-19 (`/cook`, partial-stale reframed) — (1)(2) already shipped in spike via `PadesCompletionConcurrencyTests`; (3) reframed → new `PendingTransactionCleanupJobTests` (real-SQL, real `RunOnceAsync` via InternalsVisibleTo) 4 tests pin TTL-expiry invariants. Test-only.

**§ Polish-audit Workflow/Forms/BP themes B-E (06-15)**
- **POLISH-B-SWALLOWED-EXCEPTIONS** — ✅ CLOSED 2026-06-16 — 2 stale OUT + BE throw-orphan asymmetry closed (`catch` publishes `FormSubmissionWorkflowLinkPendingEvent`, new `ExceptionFailureCode=WF_LINK_EXCEPTION`) + FE `extract-api-error.ts` surfaces BE reason at 3 sites. BE 261/261 · FE 12/12. Review APPROVE_WITH_NITS.
- **POLISH-C-DIGSIG-UNAVAILABLE-UX** — ✅ CLOSED 2026-06-16 (`eefff78f`) — provider-absent split warning/info on `signingProvider!=null`; 3-state `probeAgentStatus` (not-installed/unreachable/available) kills dead catch. vitest 1058/1058.
- **POLISH-D-FILE-UPLOAD-SCAN-UX** — ✅ CLOSED 2026-06-16 — silent upload-error now `appMessage.error(extractApiErrorMessage…)`; `useFileScanStatus` returns `{timedOut,retryScan}` + `'timeout'` FilePhase + Thử lại button. files+forms 101/101. Review APPROVE.
- **POLISH-E-I18N-HARDCODED** — ✅ CLOSED 2026-06-16 — `task-detail-drawer` status/priority routed through existing i18n keys + fixed pre-existing broken `returnObjects` label; `form-attachment-preview` 2×`#999`→`<Text type=secondary>`. vitest 1075/1075. Review APPROVE.
- **POLISH-A-FORMS-FIELDOPTIONS-KEY-FACTORY** — ✅ CLOSED 2026-06-16 — `formQueryKeys.fieldOptionsAll` factory replaces raw literal in `usePublishTemplate`; byte-identical key shape. vitest 1075/1075.
- **WEB-TYPECHECK-TEST-FORM-ATTACHMENT-PREVIEW** — ✅ CLOSED 2026-06-19 (`/cook`, STALE 6×) — real root = Vitest-4 `vi.fn` migration drift (36 errors/8 files, not 6/1; claimed root cause never existed); `typecheck:test` gate 36→0. Typing-only, 0 runtime change.

**§ Security hardening red-team (06-10)**
- **IDENTITY-SEEDER-ROLETYPE-SYSTEM** — ✅ CLOSED 2026-06-10 — `OpenIddictDataSeeder` tags Admin/SystemAdmin `RoleType.System` on create; activates RoleType leg of AssignRole/SyncUserRoles privilege guards. Build 0W/0E.
- **DELEG-DOCSTRING-STALE** — ✅ CLOSED 2026-06-10 — corrected 3 approve docstrings to "delegator or SystemAdmin (4-eyes)"; surfaced the missing revoke guard → DELEG-REVOKE-AUTHZ.
- **DELEG-REVOKE-AUTHZ** — ✅ FIXED 2026-06-10 (A01 broken-access) — `RevokeDelegationCommandHandler` had NO actor authz (any authed caller could revoke another's delegation); guard now `actorId==DelegatorId` OR Admin/SystemAdmin +403. 6 tests. Identity.Application 276/276.
- **DELEG-LIST-OWNERSHIP-SCOPE** — ✅ CLOSED 2026-06-10 (cross-tenant A01) — `UserDelegation` had no TenantId/RLS → `/delegations` leaked across tenants; query-level scoping (non-priv forced `Delegator/Delegate=@me`, priv tenant-bound EXISTS). integ 6/6. Review APPROVE.
- **WEB-AXIOS-SEC-BUMP** — ✅ CLOSED 2026-06-11 (`aec38144`/`f3084908`) — axios 1.13.6→1.16.1 + scoped pnpm overrides (fast-uri/ws/brace-expansion); FE audit 28→1; 3 collateral FE bugs fixed. FE 1005/1005.
- **EF-TENANTSIGCONFIG-ISENABLED-DEFAULT** — ✅ CLOSED 2026-06-12 — migration `20260612110000_DropTenantSignatureConfigIsEnabledDefault` drops dead `DEFAULT 0` (sentinel unsafe — config created both enabled+disabled states). MigrationConsistency 2/2 · signing integ 85/85.
- **IPFILTERRULE-PROD-FLIP-DATA-AUDIT** — ✅ CLOSED 2026-06-12 — runbook `docs/ops/ipfilterrule-flip-data-audit-runbook.md` + `scripts/260612-ipfilterrule-insert-flip-remediation.sql` (inverted-query corrected, cache-flush step added); dev DB 0 rows verified. No app code.

**§ Legacy T4-sweep + dependency drift (06-13→06-20)**
- **BE-NUGET-MESSAGEPACK-NU1903** — T2 ✅ CLOSED 2026-06-13 — pinned transitive MessagePack→2.5.302 via CPM both `Directory.Packages.props`; NU1903×26→0; 0 code (purely transitive). Clears one CI-DISABLED blocker.
- **FORMS-DIGSIG-FE-REMAINING** — T2 ✅ CLOSED 2026-06-14 — 04e FE field type (`cf1d2be2`) + 04g BP-run sign + 04f docs all shipped; live 8-cell smoke executed by user, feature accepted. Anonymous-signing carried separately.
- **VIETTEL-CCCD-LOG-PII** — T3 ✅ CLOSED 2026-06-14 (`/cook`) — scope 5→14 sites/4 files; `SigningLogMasking.MaskUserId` (last-4) wraps all CCCD logs. Identity.Infrastructure 217/217. Filed VIETTEL-VENDOR-ECHO-LOG-PII (kept).
- **CCCD-PUT-ABSENT-MEANS-UNCHANGED** — T4 ⏸️ WONTFIX-by-design RE-AFFIRMED 2026-06-20 — sentinel fix would break "clear-by-emptying"; FE gate is correct layer. Regression test added (`user-form-modal.test.tsx` 3/3).
- **USERS-EMAIL-SEARCH-LIKE-INERT** — T4 ✅ CLOSED 2026-06-20 — dropped dead `Email LIKE` at 2 sites (plaintext vs AES-GCM column → always false → 0 behavior change); FullName search preserved. Identity.Application 13/13.
- **ORGUNIT-UPDATE-TENANT-VALIDATION** — T4 ✅ CLOSED 2026-06-20 — `UpdateUserCommandHandler` verifies non-null OrgUnitId resolves in target tenant via `IOrgUnitModuleClient` → 422 on miss. Unit 5/5.
- **COLLAB-BROADCAST-FAILURE-METRIC** — T4 ✅ CLOSED 2026-06-20 — `CollaborationMetrics` Meter + `collab.signalr_broadcast.failures` counter in swallow-catch; Prometheus-registered. Tests 4/4.
- **WFE-RATELIMIT-TEST-DISABLE** — T4 ✅ CLOSED 2026-06-20 — root = WAF config-timing gap; fix sets `RateLimiting__Enabled=false` env-var in `DatabaseFixture` (test-harness only, 0 prod). `WorkflowParallelBranchRuntimeTests` 5/5 ×3. Collateral `SigningStepQueryService.cs:310` null-guard; NCalcSync major-bump reverted → BE-NUGET-NCALCSYNC-NU1902 (kept).

## 2026-06-20 (deep backlog audit — 49-row /cook sweep, INVARIANT 260522-A)

> Archived from `backlog.md` after empirical 7-agent ground-truth audit. 2 stale rows (claim contradicted by repo), 2 fixed-this-session, 12 fully-closable legacy buckets (shipped/obsolete). Deferred-on-trigger slivers carried forward in backlog.md.

- **SIGN-P5-PADES-EMBED-REMOTE** — T2 ✅ CLOSED 2026-06-20 (STALE) — claimed "ready-to-spike" but spike SHIPPED + merged PR #117 (`9790033f`) + #118 (`3f4e27cf`) on 2026-06-19; `Services/Signing/Pades/{PadesCmsBuilder,PdfIncrementalSignatureWriter,PadesTransactionInitiator}.cs` + 8 test files present. Spike complete.
- **T3-MENU-SEC** — P3 ✅ CLOSED 2026-06-20 (STALE) — closed 2026-05-22 v2.137p97 (`4ab1f6dd`); SECURITY menu seeded full at `E2ETestDataSeeder.Menus.cs:465-478`. (L2 product-policy on IP_RULE.VIEW carried to backlog as a thin FU note.)
- **IDENTITY-UPDATEUSER-ORGUNIT-NOOP** — T4 ✅ FIXED 2026-06-20 (`/cook`) — `UpdateUserCommandHandler` now assigns `user.PrimaryOrgUnitId = cmd.OrgUnitId` (was silent no-op despite cmd carrying OrgUnitId). 3 new `UpdateUserCommandHandlerTests` (assign+round-trip · null-clears · not-found) green.
- **COLLAB-BROADCAST-RESILIENCE** — T4 ✅ FIXED 2026-06-20 (`/cook`) — `MessageSentSignalRHandler` wraps read-back+broadcast in try/catch log-and-swallow (+`ILogger` injected, `BroadcastAsync` extracted); a realtime fault no longer turns a committed write into a 500. 2 new unit tests (swallow+log · missing-message) green. `MessageRow` made internal + `InternalsVisibleTo` for the test.
- **Oracle 19c Development bucket** — ✅ CLOSED 2026-06-20 — work moved to separate repo (v2.156 shipped 2026-04-27, 11 phases); AqtCoreFW tracks SQL Server only, 0 items.
- **RBAC v2.133p8 Follow-ups bucket** — ✅ CLOSED 2026-06-20 — RBAC-1/2/3/5 shipped (`88e72dff`/`40192c61`); RBAC-4 (seeder orphan cleanup, ~1h, deferred-intent) carried to backlog.
- **Cross-Schema Query Cleanup bucket** — ✅ CLOSED 2026-06-20 — CSQ-1/2 shipped (`69421d6a`), CSQ-5 obsolete (Cases removed); CSQ-3/4 (known-limitation, deferred-trigger) carried to backlog.
- **BP MVP P1 Follow-ups bucket** — ✅ CLOSED 2026-06-20 — P1-FU RepublishFormTemplate shipped v2.137p24.
- **Audit Module Microservice Extraction bucket** — ✅ CLOSED 2026-06-20 — AUD-2/3/4 shipped (`ba77b79a`); AUD-1 (noop consumer, 0.5d on-trigger) carried to backlog.
- **Auth Session Redirect Follow-ups bucket** — ✅ CLOSED 2026-06-20 — AUTH-RDR-FU1/2/3 all shipped v2.137p20-21 (`cf2cdde7`/`74466230`).
- **Migration Preflight Cleanup bucket** — ✅ CLOSED 2026-06-20 — shipped v2.133p15 (`e499380c`), 3 integration tests verify.
- **Architecture Test Pre-existing Failures bucket** — ✅ CLOSED 2026-06-20 — ARCH-1 interface-extraction shipped v2.132p11 (`69421d6a`), guard green.
- **Security & Infrastructure Hotfixes bucket** — ✅ CLOSED 2026-06-20 — OpenTelemetry NuGet bump shipped v2.132p6.
- **Phase-02 Follow-up Items v2.127p3 bucket** — ✅ CLOSED 2026-06-20 — sole item (manual 422 smoke) covered by unit validator path.
- **Cross-Phase Tech Debt P7 bucket** — ✅ CLOSED 2026-06-20 — ARCH-FU + TEST-FU both shipped 2026-05-17.
- **Phase-04 Follow-ups bucket** — ✅ CLOSED 2026-06-20 — all 4 items done 2026-04-17/18, 1 obsolete (Cases removed).

## 2026-06-16 (signing phase-0 + polish slices — closed during /aqt-stop)

> Archived from `backlog.md` during `/aqt-stop` wrap-up. Individual ✅ CLOSED / ✅ RESOLVED / ✅ SHIPPED bullet rows extracted from the "Newly filed" sections (open rows left in place). Verbose close-notes also live in `docs/project-changelog.md` + `docs/journals/` + the cited commits.

- **SIGN-DOCFLOW-ORPHAN-BLOB-SWEEP** — T4 🔵 ✅ **CLOSED 2026-06-16** (`/cook`, BE Files+host) — audit found the row undersold scope: the orphan key is **never persisted** (it only lived in the losing/crashed request's memory at `FileUploadHandlerController.cs:272`), so the sweep cannot be DB-only — it MUST enumerate the bucket, but `IFileStorageService` had no list method. User chose **Option A (additive sweep)**. Shipped: (1) `IFileStorageService.ListObjectsAsync(bucket,prefix)` + `StorageObjectInfo` record (Files.Domain) + MinIO impl (`ListObjectsEnumAsync`, bucket-absent→empty); (2) new host job `SigningDocFlowOrphanBlobSweepJob` (cross-module: Identity records + Files storage → placed in host like `FileUploadHandlerController`, avoids an Identity→Files dep) — lists `signing-docflow`, groups by `{tenantId}/` prefix, pins **per-tenant SESSION_CONTEXT** before reading referenced `SignedStorageKey`s (RLS-correct under future least-priv), deletes blobs >24h old & unreferenced; (3) daily 03:00 UTC recurring, `default` queue (no new `BackgroundJobQueues` const → arch-guard untouched). **Deletion-safety (reviewer-verified sound across db_owner-today + least-priv-future):** originals are a separate bucket (`signing-original`); `IgnoreQueryFilters()` keeps soft-deleted records' blobs referenced; 24h grace protects the UploadAsync→Complete in-flight gap; **circuit-breaker** skips a tenant with deletion-candidates but zero referenced keys (guards a silent RLS empty-read from mass-deleting); `[DisableConcurrentExecution]`+`[AutomaticRetry(0)]`. Verify: Files.Infra 0W/0E · host 0W/0E · 5 pure `SelectOrphanKeys` tests + Files.Infra 37 + Files.App 114 + Arch 109 = green · review APPROVE (DONE_WITH_CONCERNS, both Mediums applied). Source: Slice-1 (H3) review L2.
- **SIGN-DOCFLOW-LEASTPRIV-SESSIONCTX** — T3 🟡 ✅ **CLOSED 2026-06-16** (`/cook`, audit-only — INVARIANT 260522-A) — audit-at-slice-start found the row's 4 asks largely already-satisfied or moot, only a thin cutover-gated remainder survives (carried to `SIGN-DOCFLOW-LEASTPRIV-E2E-TEST`): **(1)+(2) shipped** — SESSION_CONTEXT-from-JWT + open-guard live in both endpoints (`FileUploadHandlerController.cs:78`/`:196`, impl `:352-365`, open-guard `:359`), commit C2 `7cd044bc`. **(3) clear-on-return covered** — cross-request bleed is prevented unconditionally by pool-reset (`sp_reset_connection` always clears SESSION_CONTEXT), documented `:356-357`; not cutover-dependent. **(4) `Connection Reset` posture MOOT** — the row was framed in legacy `System.Data.SqlClient` terms where `Connection Reset=false` could disable reset; the project uses **`Microsoft.Data.SqlClient` 6.1.1** (`Directory.Packages.props:91`) which **removed that keyword** — pool-reset is unconditional and cannot be turned off, so there is no prod posture to confirm. User chose document-and-close over belt-and-suspenders explicit clear (YAGNI on a guarantee the in-use client already enforces). No code change. Source: Slice-2 (C2) review H1 + open Q.
- **SIGN-ENROLL-USER-EXISTS-CHECK** — T4 🔵 ✅ **CLOSED 2026-06-16** (`/cook`, BE) — `EnrollSignerCertCommandHandler` now resolves the target signer (`UserManager.FindByIdAsync`) **before** the dup-check/insert and fails `NotFound` when the user is missing **or** belongs to another tenant — a single error masks both cases (no cross-tenant existence oracle to the SystemAdmin caller). Audit-at-slice-start confirmed the gap real (handler did dup-check→insert only) and the threat is data-integrity (orphan row), not security (RLS already scopes the row); the explicit `user.TenantId != cmd.TenantId` check is **not** redundant with RLS because the endpoint's SystemAdmin tenant-override may not align the RLS session-context with `cmd.TenantId`. Pattern mirrors `LockUser`/`UpdateUser`. Added `[ProducesResponseType(404)]` to `EnrollCert` (OpenAPI accuracy; `ToApiResponse` already maps it). 5 new `SigningHandlerTests` cases (in-tenant→enroll, missing→404+no-insert, cross-tenant→404+no-insert, null-tenant global-user→404+no-insert, duplicate→400 still fires). Verify: Identity.Application **308/308** green · src 0 warnings · code-review APPROVE 0 Crit/High/Med. Filed follow-up `SIGN-ENROLL-TEST-CS4014-FALSEGREEN`.
- **SIGN-ENROLL-TEST-CS4014-FALSEGREEN** — T4 🔵 ✅ **CLOSED 2026-06-16** (`/cook`, test hygiene) — added `await` to the fire-and-forget `remoteProvider.DidNotReceive().SignAsync(...)` at `SigningHandlerTests.cs:450` (`SignPayload_UserWithoutCccd_FailsClosedWithSignerIdentityUnresolved`). The un-awaited assertion was a latent false-green (may not run before the test returns) and the lone CS4014 warning in the test project's build. Verify: build warning gone, `SigningHandlerTests` 25/25 green (the now-awaited `DidNotReceive` confirms the provider isn't called on the fail-closed path). Discovered during SIGN-ENROLL-USER-EXISTS-CHECK.
- **SIGN-FORMS-DIGSIG-INTEGRATION-COVERAGE** — T3 🟡 ✅ **CLOSED 2026-06-16** (`/cook`, test-only) — audit-at-slice-start found the row half-right: the handler decision logic was **already exhaustively unit-covered** (10 dig-sig cases in `SubmitFormCommandHandlerTests`, mocked client) → a full 3–5-case integration suite would mostly duplicate. The genuine gaps were narrower: **(A)** no test pinned `verifyRequest.SignerUserId == SubmittedBy` (the C1 anti-impersonation binding — "covered transitively"); **(B)** no integration test proved E2E HTTP base64→DTO→command→SQL persistence + hard-block → real HTTP status + zero rows. Shipped (user chose targeted A+B): (A) added unit `Handle_VerifyRequestUsesSignerUserIdFromSubmitter`; (B) new `FormDigitalSignatureSubmitTests` (3 Testcontainers cases) — happy→200 + all 6 evidence cols persisted in SQL + SignerUserId/TenantId server-resolved; verify-fail→**422 GOV_FRM_022** + 0 rows; required-missing→**422 GOV_FRM_021** + 0 rows. `IIdentitySigningClient` mocked at the Forms boundary (crypto/binding verdict is Identity's tested concern). **Row said "400" — real contract is 422** (`ToApiResponse` + `[ProducesResponseType(422)]`). Verify: 3 integ + 17 full integ + 259 unit green · 0 warnings · code-review APPROVE. Note: `FormValidationService.SkipValidationTypes` excludes Section/Label/Divider/Formula but **not** DigitalSignature → a required dig-sig field needs a non-empty `data[key]` to clear data-validation before the signature gates (tests account for this; FE already supplies one).
- **ARCH-MFA-CLAIM-CONST-LOCATION** — T4 🔵 ✅ **CLOSED 2026-06-16** (`/cook`, test-only fix) — refactor `cbe92117` extracted `MfaClaimEvaluator` (static) and moved the `mfa_at` claim const + recency logic out of `MfaRequirementHandler` (now delegates to `MfaClaimEvaluator.IsMfaRecent`), but `Qd742Phase03ComplianceTests.Compliance_1_6_b_MfaRequirementHandler_ValidatesMfaAtClaim` still reflected `typeof(MfaRequirementHandler).GetField("MfaAtClaimType", NonPublic|Static)` → null → RED (empirically confirmed: "Expected claimTypeField not to be <null>"). Row's "private const" claim had **drifted** — the const is now **public** on `MfaClaimEvaluator`, used by 3 sites, so relocating it would break `AuthorizationController`/`MfaChallengeController` → the correct fix was retargeting the **test**, not moving the const. Fix: reflection now points at `typeof(MfaClaimEvaluator).GetField(nameof(MfaClaimEvaluator.MfaAtClaimType), Public|Static)`; handler exists+sealed assertions kept; comments updated. No production change. Verify: `Qd742Phase03ComplianceTests` **5/5** green (was 4/5, target test RED→GREEN). Filed follow-up `MFA-AT-CLAIM-LITERAL-DRY`.
- **AUDIT-CHAIN-BACKFILL-RICHER-EVIDENCE** — ✅ CLOSED 2026-06-15 (`/cook`) — the `chain-backfill` audit entry now records caller IP + correlationId (dedicated columns AND the JSON blob for a self-contained forensic record) + per-tenant **pre/post chain-head signatures**. `RunSigningPassAsync` returns `SigningPassResult(Count, PreHead, PostHead)` (pre-head read from the prior anchor before reset, under the same applock+RLS context); new private `BackfillAllTenantsWithEvidenceAsync` aggregates per-tenant evidence; `BuildBackfillEvidenceJson` (System.Text.Json, replaces fragile interpolation). `IHmacChainBackfiller.BackfillAllTenantsAsync` (`Task<long>`) + HTTP endpoint/202 UNCHANGED. build API host 0W/0E · Audit unit/app/domain 262/262 + Audit.Integration 61/61 (incl new evidence test) + prod-least-priv 4/4 · review APPROVE_WITH_NITS (M1 correlationId-in-JSON + IP proxy caveat comment folded). IP caveat: `RemoteIpAddress` = proxy IP absent ForwardedHeaders → corroborating evidence, `UserId` authoritative.
- **AUDIT-MIGRATION-SQL-MIRROR-TEST-STALE** — ✅ CLOSED 2026-06-15 (`/cook`) — root cause = the guarded migration `20260501004056_AddAuditCategoryAndSeverity_M1.cs` (commit `45743e29`) was **deliberately deleted** by the Phase-06 squash (commit `1f344588` "delete 217 migration files"); the greenfield `InitialCreateFoundationV1` creates audit Category/Severity as first-class NOT NULL columns (no data-backfill SQL CASE survives anywhere — grep = 0). The `Migration_Sql_File_Mentions_Every_Mirror_Key` test + `LocateMigrationFile` helper guarded a non-existent artifact → **deleted** (dead guard). The real mapper↔mirror drift guard `Mirror_Matches_AuditCategoryMapper_For_Every_AuditAction_Constant` is preserved. Unit test 1/1 green · review APPROVE_WITH_NITS.
- **RLS-POLICY-COUNT-EXPECTED-STALE** — ✅ CLOSED 2026-06-15 (`/cook`) — root cause = 2 RLS policies added by migrations dated AFTER the array's last update were never reflected: `forms.FormTemplateVersions` (`20260514102007_AddFormTemplateVersion`) + `workflow.WorkflowTaskCandidateIndex` (`20260519015732_AddWorkflowTransitionAssignmentAndPoolTask`) — NOT BP-MVP as the row guessed (BP's 3 policies were already in the array). Added the 2 `(schema, table)` entries + updated count 39→41. Full source enumeration (16 `GenerateRlsPolicyStatements` + 3 nullable-tenant) = 41, reviewer-cross-checked. Integration test 2/2 green against live DB (count=41).
- **HANGFIRE-AUDIT-QUEUE-ORPHANED** — ✅ RESOLVED 2026-06-14 (`/ck:cook`, WIRE chosen) — added `BackgroundJobQueues.Audit` const; `HangfireAuditJobEnqueuer.EnqueueHmacChain` now `BackgroundJob.Enqueue(BackgroundJobQueues.Audit, …)`; `opt.Queues` raw `"audit"`→const (anti-drift). Static-call kept (the enqueuer IS the Hangfire adapter behind `IBackgroundAuditJobEnqueuer`; routing through `IBackgroundJobService` = adapter-calling-adapter, reviewer confirmed). +arch guard `BackgroundJobQueueRegistrationTests` (every `BackgroundJobQueues` const must be in `opt.Queues` → prevents the stuck-job class). build API host 0E · arch guard 1/1 (suite 108/109, 1 pre-existing MFA fail unrelated) · Audit.App 54/54 · review APPROVE 0-block.
- **CLAMAV-BYPASS-FLAG-COLLAPSE** — ✅ RESOLVED 2026-06-14 (`/ck:cook`, after `/ck:ask`) — collapsed the two virus-scan knobs to one. Removed `ClamAvOptions.BypassWhenUnavailable` (deploy-time "fail-open when clamd down" foot-gun); scanner (`ClamAvVirusScanner`) now unconditionally **fail-closed** — unreachable clamd → `VirusScanResult.Error` (never Clean) → `ClamAvScanJob` retries, file stays Quarantined. Remaining control = `feature:files.virus-scan` (intent, runtime/per-tenant). Behavior-identical in every configured env (Dev already `bypass=false`; Benchmark never seeds the flag → scanner never invoked → removed branch was dead config). `ClamAvStartupHealthCheck` now always pings EXCEPT Benchmark (`IsEnvironment("Benchmark")` skip, avoids a misleading startup CRITICAL where no clamd is provisioned). Removed config key from 2 appsettings. build API host 0W/0E `/warnaserror` · Files.Infra.Tests 37/37 (+2 fail-closed scanner tests) · Files integ 7/7 · review APPROVE_WITH_NITS (3 Low cosmetic).
- **CLAMAV-FIRSTBOOT-FLAG-RACE** — ✅ RESOLVED 2026-06-14 (`/ck:cook`) — `SystemParamSeeder` now warms the feature-flag L0 cache after writing: if any newly-seeded key is `feature:`-prefixed, it calls `IFeatureFlagService.ReloadAllAsync(ct)` post-`SaveChangesAsync`. Collapses the first-boot window (initial `ReloadAllAsync` ran before the row existed → flag read false until the 5-min refresh) to the seeding instant. Idempotent (a later periodic reload re-reads the same rows); guard scoped to `toAdd` so a warm DB / non-feature-only seed doesn't reload needlessly. Chose explicit reload over hosted-service reorder (intent-revealing, not DI-order-fragile). build 0E · SystemParams.Tests 41/41 (+3 new seeder-warm tests) · review APPROVE_WITH_NITS 0-block.
- **HANGFIRE-DASHBOARD-AUTHSERVER-RELOCATE** — ✅ SHIPPED 2026-06-14 (`/ck:ask`→`/ck:brainstorm`→`/ck:plan --tdd`→red-team→validate→`/ck:cook`; plan `plans/260614-1115-hangfire-dashboard-authserver-relocate/`) — moved the Hangfire dashboard from the API host (Bearer/MultiAuth → unreachable from the SPA in prod, same class as the `<a href>`→401 bug) to the **AuthServer** host (cookie-native ASP.NET Identity → SystemAdmin role resolves from `HttpContext.User`). Reuses existing Hangfire SQL storage (AuthServer is enqueue-only, no server — dashboard reads the shared store, API-host server appears in it) + Identity cookie + `HangfireAuthorizationFilter`. **Hardened the filter**: extracted `public static IsAuthorized(ClaimsPrincipal?)` + **dropped the `IsDevelopment()→true` dev-open bypass** → SystemAdmin required in every env. SPA: SystemAdmin-gated external-link nav item (`admin-menu-entries.ts`, `requiredRole`) → `${OIDC_AUTHORITY}/hangfire` new tab. **Prod posture (user): full control in prod** (requeue/delete/trigger; SRS updated). **Red-team findings folded**: F1 SameSite=Strict link-out works only same-eTLD+1 (confirmed prod `*.aqtcorefw.gov.vn`) — validated by a local 2-subdomain harness (Phase 3, manual); F2 dashboard-POST CSRF defense = the Strict cookie (don't relax to Lax); F3 SRS read-only-prod → full-control. Filter unit 4/4 · route-probe 2/2 (anon `/hangfire` denied in Dev on AuthServer + API host 404) · FE layouts 28/28 (+4) · tsc/eslint 0E · both hosts build `/warnaserror` 0E. **REMAINING (manual, user-owned):** Phase 3 local 2-subdomain TLS harness to eyeball the Strict cross-subdomain cookie carry on the link-out (`app.aqt.local`+`auth.aqt.local`, mkcert) — automated localhost can't exercise it. Follow-up `HANGFIRE-DASHBOARD-F1-MANUAL-SMOKE` (T4) until run.
- **FORMS-ATTACHMENT-PREVIEW-SCANSTATUS** — ✅ RESOLVED 2026-06-14 (`/ck:cook`) — `AttachmentPreview` (`form-attachment-preview.tsx`) now reads scan status via the shared `useFileScanStatus` (was a local metadata query) and gates the render on `file.status`: Quarantined → "Đang quét virus…" tag + no link; Rejected → rejected tag + no link; Available → unchanged link/thumbnail (image-blob fetch also gated on Available). Kills the broken download link a still-quarantined file showed in detail views (BE `DownloadFileQueryHandler:41` 404s non-Available). Bonus: terminal-status files don't poll; a Quarantined→Available transition now auto-refreshes (old 5-min staleTime would have shown a stale dead-link). Fixes both consumers (`form-attachment-preview` + `bp-run-submissions-card:72`). tsc/eslint 0E · FE forms+files suites 150/150 (+4 new) · review APPROVE_WITH_NITS (M1 i18n applied — tags routed through `t()`).

## 2026-06-12 (Viettel Mysign sign-smoke arc — closed during `/aqt-stop`)

> Full detail in `docs/project-changelog.md` "Viettel Mysign Remote Signing — E2E Smoke Complete" + journals `journal-260612-2145-*` / `-2230-*`.

- **VIETTEL-FE-RAW-CERT** — T3 ✅ RESOLVED 2026-06-12 (`795612c6`). FE registry never registered `remote-viettel-mysign` key + `remoteSign` dropped raw signer cert → CMS-parse failure. Fixed FE adapter (`isRawProvider` + cert wiring), no BE change. **P2 live OTP sign-smoke RAN + PASSED** after the five request-shape fixes below (live verify IsValid=true).
- **VIETTEL-SIGNHASH-STATUS-NUMERIC-RISK** — T3 ✅ RESOLVED 2026-06-12 (`5aac3caa`). `FlexibleStringJsonConverter` (String|Number→string, raw-literal read) on `MysignSignHashResponse.Status` + 3 `error` fields → async status-poll tolerates numeric tokens. Property-level covers both clients; mapper unchanged. 56/56 Signing unit; code-review H1 (GetDecimal throws non-JsonException) fixed in-slice.

## 2026-06-12 (backlog optimization — 9 closed Workflow/CI sections archived; full detail in changelog/journals/git)

> Archived from `backlog.md` during `/aqt-stop` wrap-up. All ✅ CLOSED `###` sections (06-08 → 06-10) that were interleaved among still-open rows. One-line each below; verbose close-notes live in `docs/project-changelog.md` + `docs/journals/` + the cited commits.

- **WFE-TAXONOMY-MIGRATION-TEST-FLAKE** — T3 ✅ CLOSED 2026-06-09 (`c735fc2e` + `669b99e5`). Row premise wrong (INVARIANT 260522-A): 5 genuine stale-mock per-test breakages fixed (workflow-migration ×4 + 3 sibling SUT-contract-drift tests); taxonomy trio = env contention (→ WFE-FULLSUITE-CONTENTION). Full suite 993/993.
- **WFE-FULLSUITE-CONTENTION-TIMEOUT** — T3 ✅ CLOSED 2026-06-10 (`d354440f`). `testTimeout` 5000→20000 + Vitest 4 pool-rework (`poolOptions.forks` silently ignored → `maxWorkers:2`). 993/993 ×2, 0 deprecation warnings.
- **WORKFLOW-CLONE-STALE-UNIT-TESTS** — ✅ CLOSED 2026-06-10 (`888e1fae` + `fbf17a6d`). Clone-arc left 2 stale unit-test families (CI dark since April); fixed 2-arg `Clone()` (Domain 145/145) + rewrote 3 handler tests to delegated `ReplaceGraphAsync` design (App 640/640). 🔴 rogue tester edited source mid-read-only-run → reverted.
- **CI-FE-NPM-LOCKFILE-STALE** — T3 ✅ CLOSED 2026-06-10 (`64e98a69`). Policy (b) pnpm-everywhere: pin `pnpm@10.33.0`, commit `pnpm-lock.yaml`, drop `package-lock.json`, all 3 web ci.yml jobs + `web/Dockerfile` → pnpm. Local frozen-lockfile/tsc/vitest/build green.
- **WF-CLONE-AFTER-DELETED-VERSION-500** — T3 ✅ CLOSED 2026-06-10 (`375d682f`). Delete-version IsLatest handoff + clone version-collision (`GetMaxVersionByKeyAsync` over all rows incl soft-deleted). 7/7 integ. + E2E orphan-leak root-caused (`46c75756`, cleanup moved to `test.afterAll`).
- **WF-MUTATION-RESPONSES-UNNORMALIZED-IDS** — T4 ✅ CLOSED 2026-06-10 (`6783badf`). Piped all 4 definition-returning mutation responses through `normalizeDefinitionIds` (untyped `put` hid the 4th). FE-only, no live consumer.
- **WF-DAGRE-COORDS-SAVE-VERIFY** — T4 ✅ CLOSED 2026-06-10 (`1935f9f0`). Verify EXPOSED a real bug: branch refs not string-coerced at API boundary → every visual-designer fork-save was 400-broken. Fixed + 3 regression tests; live round-trip ✅.
- **WF-PARALLEL-RUNTIME-ENABLEMENT** — T5 ✅ EPIC COMPLETE 7/7 (DONE 2026-06-08; P1-P3 06-06, P4 engine 06-07, P5+P6+P7 06-08, all pushed). Parallel-branch workflows runnable e2e (activate→fork→pool/claim→AND-join→continuation→close). Plan `plans/20260604-2205-parallel-branch-runtime-enablement/`. Open follow-ups (separate rows): WFE-RATELIMIT-TEST-DISABLE, admin-reassign-to-eligible.
- **WF-PARALLEL-BRANCH-BROWSER-E2E** — T4 ✅ CLOSED 2026-06-10. API-contract regression guard `web/e2e/workflow-parallel-branch.spec.ts` 6/6 + canvas-render verify + drag float-exact round-trip (DB `718.3167…`). Remaining = ~2min optional manual eyeball only.

## 2026-06-09 (delete-bug follow-ups all closed — e2e stale-save + create-rule coverage + T2 repo audit)

> The 3 follow-ups the delete-bug fix (`adde5afc`) spun off are now all closed, plus a sibling spec found en route.

### WFE-DEFS-SPEC-STALE-SAVE — ✅ DONE 2026-06-09 (`1adcd5ea`, pushed)
- `web/e2e/workflow-definitions.spec.ts` → **9/9 green** (6 downstream tests were dormant under the 422 serial-skip). 🔴 INVARIANT 260522-A: filed root cause WRONG — captured the actual 422 body = `GOV_SYS_002` non-canonical state `InReview` (canonical `Generic` vocab uses `UnderReview`), not (only) missing assignment. Both fixes needed: rename + add ByPermission `assignment` (R2-strict genuinely requires it; vocab gate fires first). Lesson: capture the real 422 before trusting a row's "root cause (likely)".

### WF-CREATE-RULE-HANDLERS-COVERAGE — ✅ DONE 2026-06-09 (`1adcd5ea`, pushed)
- New `WorkflowCreateRuleHandlersDefinitionResolutionTests` (3/3 real-SQL): proves CreateAssignmentRule/CreateNotificationConfig/CreateEscalationRule resolve WorkflowDefinition by Guid PublicId → internal long FK (the path the delete fix's `GetByIdAsync(Guid)` override repaired). assignment+notification via HTTP; escalation handler-direct (no create endpoint). Non-vacuous: pre-fix the Guid FindAsync throws → no row. Unit mocks were structurally blind.

### WFE-DYNAMIC-INSTANCES-SPEC-STALE — ✅ DONE 2026-06-09 (`1adcd5ea`, pushed; user opted fix-now)
- `web/e2e/workflow-dynamic-instances.spec.ts` → **10/10 green** (was 0/10, setup 422). Dormant-spec layered rot — 4 independent test-only staleness fixes each exposed after fixing the prior: `InReview`→`UnderReview`, missing transition `assignment`, fixed `ENTITY_ID`→unique-per-run (active-instance uniqueness 409), create-task body `workflowInstanceId`→`workflowInstancePublicId` (Guid AK). Open-Q: confirm whether e2e specs run in CI (if so these were silently red).

### WF-T2-REPO-FIND-AUDIT — ✅ DONE 2026-06-09 (`3f66351f`/`44a0f07a`, pushed)
- Audit of the delete-bug pattern (repo keyed `IRepository<T,Guid>` whose EF PK is `long`). Verdict: exactly **3** T2 Guid-keyed repos (WorkflowDefinition/WorkflowInstance/FormSubmission); **no open/closed-generic `IRepository<T2,Guid>` registration → broken base never injectable** (DI containment is the real safety; all T2 via specific iface w/ override). Get-by-id already safe on all 3. Only latent gap: `DeleteAsync(Guid)` unoverridden on WorkflowInstance + FormSubmission, **0 call sites** (never hard-deleted). User chose defensive overrides (option B) → added `DeleteAsync(Guid)` to both (resolve by PublicId → soft-delete; mirrors WorkflowDefinitionRepository) + 2 integ tests (2/2, non-vacuous). Bonus: cross-tenant delete now no-ops (tenant-isolation). Lesson: audit a "broad root cause" by enumerating the EXACT surface (grep + DI regs), not assuming — real scope 10× smaller than the row feared.

## 2026-06-09 (WF clone-fix merged + parallel-branch e2e + definition delete/i18n fix)

### WF-CLONE-DROPS-FORK-AND-ASSIGNMENTS — ✅ MERGED 2026-06-09 (PR #116 → main `6f0efdaf`)
- Clone produced an un-saveable/un-activatable Draft (5 validation violations). Root cause: incomplete deep-copy — the graph is 4 entity families but only 2 live inside the `WorkflowDefinition` aggregate; the 2 satellites (`WorkflowTransitionAssignment` keyed by TransitionId, `WorkflowParallelBranch` aggregate) were dropped.
- HYBRID fix: delegate full 4-family copy to canonical `ReplaceGraphAsync` via new `CloneGraphProjection` entity→spec mappers + new `GetParallelBranchesForCloneAsync` (validation loader omitted resolutions → caught a 2nd-order resolution-drop) + retain bespoke `AutoTransitionOnTimeoutId` remap. Branch `fix/wf-clone-deep-copy` (`f76e1cf6`) → PR #116 → `--no-ff` merge `6f0efdaf`, origin+bitbucket synced. 3 clone integ facts + Workflow integ 108/108 · 0C/0H. Report `plans/reports/debug-260609-wf-clone-drops-fork-and-assignments.md`.
- Spun off: `WF-PARALLEL-BRANCH-BROWSER-E2E` (🟡 mostly-done — automated guard shipped, ~2min manual drag eyeball remains).

### WF-DEF-DELETE-GUID-PK-AND-I18N — ✅ FIXED 2026-06-09 (`adde5afc`/`fc06b3e8`/`dbb1c4f1`/`b3860b98`, pushed)
- **Bug 2 (HIGH):** `DELETE /workflow/definitions/{guid}` → 400 GOV_SYS_002 (Guid vs long PK). Root cause: `GenericRepository.GetByIdAsync/DeleteAsync` use EF `FindAsync` (targets long PK) but repo TKey=Guid PublicId(AK). Fix: override both in `WorkflowDefinitionRepository` to resolve by PublicId. Bonus: un-breaks 3 sibling Create handlers (broken→working) + tenant-isolation positive (cross-tenant delete→404).
- **Bug 1 (LOW):** `common.confirmDelete` raw key → added to vi/en.json (verified UI).
- build 0W/0E · integ 13/13 (+new delete-by-PublicId) · broad Workflow integ 107/109 (2=`WFE-RATELIMIT` flake, pass 3/3 isolation) · e2e delete asserts tightened `[200,204]` · 0C/0H. Report `plans/reports/debug-260609-workflow-delete-guid-pk-and-i18n-confirmdelete.md`. Follow-ups: `WF-T2-REPO-FIND-AUDIT`, `WF-CREATE-RULE-HANDLERS-COVERAGE`, `WFE-DEFS-SPEC-STALE-SAVE` (still open in backlog).

## 2026-06-08 (WF parallel-fork runtime hardening — P5-FOLLOWUP closed + RB5 shipped)

### WF-PARALLEL-RUNTIME-P5-FOLLOWUP — ✅ DONE 2026-06-08 (fork runtime hardening complete; 🛑 checkpoint=PASS)
- **All 5 sub-items closed:** H2 concurrency, RT2, RT4-runtime, RT13, H1 reconciler.
- **(a) H2** `WorkflowParallelBranchConcurrencyTests` (3) — surfaced+fixed a real engine defect: `ParallelBranchService.cs:164` reload-after-save was a TRACKING re-query → stale identity-map → concurrent resolvers saw AND-join incomplete → instance stranded InProgress. Fix `AsNoTracking()`. Commits `959e6753`→`ce591a14`.
- **(c/e) RT2+RT4** `WorkflowParallelBranchNegativeTests` (3) — all 3 original premises refuted by verified engine behavior (resolver pushes a predicate regardless of membership); reframed to actual reachable behavior (empty-refs fork-entry 422 / empty-continuation strand / unheld-role AND-join deadlock). Commit `21fd675a`.
- **(d) RT13** `ExecuteBpTransitionTransactionTests` (1) — BP-run transition into fork-entry stages fork + advances run under `ICrossModuleUnitOfWork`, no DTC-500. Honest scope: shared SqlConnection+SqlTransaction → proves commit path only; rollback atomicity = fault-injection gap. Commit `a258289b`.
- **(b) H1 reconciler** NEW `BranchJoinReconcilerJob` (recurring 5min) finishes stranded joins (satisfied but instance not advanced); thin finder-delegator → `IBranchJoinCompletionService.CompleteJoinAsync` (no reimplementation). Narrow predicate (timed-out excluded). Empty-continuation = throttled-warn (IMemoryCache Size=1, no retry). 🔴 Bug found empirical: `cache.Set` w/o Size threw → swallowed by per-candidate catch as Skipped. 🔴 #1 risk tenant/RLS cleared (`BackgroundJobTenantContext` pass-through, verified by test + code-review trace). Tests 4/4 + full Workflow integ 103/103. Commits `393257a4` feat + `813cc5b5` docs + `ea4cb893` journal, pushed ahead=0. Ships DORMANT.
- **Carry-fwd:** BP fork-rollback fault-injection; reconciler doesn't cover stranded-then-timed-out (needs failure-target-state feature); `WFE-RATELIMIT-TEST-DISABLE` flake.

### WF-PARALLEL-RB5-RESOLVE-TO-CLAIMANT — ✅ SHIPPED 2026-06-08 (`/cook --auto`, design-time option a)
- **Shipped:** RB5-Activate resolves each exclusively-ByRole branch child against live identity (`IIdentityModuleClient.ResolveActiveUsersInRolesAsync` = direct-AspNetUserRoles reverse of claim-side `GetUserRoleCodesAsync`, PQ-1 A); 0 active claimants → RB5 422 at activation, def stays inactive. Closes the proven `ForkChildPoolWithUnheldRole` deadlock. `WorkflowGraphValidator.ValidateForActivateAsync` made async + `ValidateBranchChildRoleClaimantsForActivateAsync` + `ParseRoleCodes`. Commits `49150b16` fix + `e353032b` docs + `5101ea52` journal, pushed ahead=0.
- **Scope:** ByPermission NOT checked (deferred `WF-NEXT-HANDLER-PERMISSION-USERS`); `AllowAssignorOverride` unwired → code forward-notes `&& !AllowAssignorOverride`. Tests: unit `WorkflowGraphValidatorBranchClaimantRulesTests` 6 (success-branch only provable at unit layer) + integration rewrite `ForkChildPoolWithUnheldRole_IsRejectedAtActivation`. Build 0W/0E, code-review APPROVE_WITH_CONCERNS 0C/0H.

### WF-SAVEGRAPH-TXN-ROLLBACK-TEST — ✅ SHIPPED 2026-06-08 (`/cook` interactive, test-only)
- **Shipped:** NEW `WorkflowDefinitionRepositoryReplaceGraphBranchRollbackTests` (2 facts) proving `ReplaceGraphAsync`'s `TransactionScope` rolls back the parallel-BRANCH phase — the gap the 4 happy-path branch facts never covered. `ReplaceGraphAsync` = 4 raw-SQL DELETEs + 4 SaveChanges (#1 states · #2 transitions · #3 transition-asg · #4 branches) in one scope. Reuses existing `ThrowOnNthSaveInterceptor` (DRY, same namespace).
- **Fact B (load-bearing):** commit a graph w/ branch → re-save a different graph that throws on #4 → original branch (by captured Id) + original states **restored**. Proves the leading `DELETE FROM WorkflowParallelBranches`/`WorkflowStates` enlist in the scope and roll back — the production-critical "failed re-save must not wipe the definition" property P3_A4's fresh seed never exercised (its DELETEs deleted nothing). **Fact A:** fresh-insert throw on #4 → phases 1-3 rows (states/transitions/transition-asg written on #1-#3) roll back.
- 🔴 **Advisor caught vacuity (own RT13 lesson recurring):** interceptor throws in `SavingChangesAsync` PRE-save hook → on #4 branch rows never reach DB → `branches==0` in Fact A is trivially true. Kept it labeled confirmatory-only; discriminating load carried by phase-1-3 counts (Fact A) + original-graph-restored (Fact B). Code-review verified non-vacuous, applied Low-1 `.WithMessage("*ThrowOnNthSaveInterceptor*")` to pin throw origin vs a name-drift `InvalidOperationException`.
- New class 2/2 · all `ReplaceGraph*` 10/10 together (no shared-interceptor/DB interference) · build 0W/0E · review DONE_WITH_CONCERNS 0C/0H (2 Low optional). Atomicity HOLDS — no prod hole. Zero production code. Commits — see changelog 2026-06-08.

### BP-RUN-SUBMISSIONS-TEST-VERIFY — ✅ CLOSED 2026-06-08 (verify-only, no code change)
- **Verdict:** `GetBpRunSubmissionsQueryHandlerTests` **6/6 PASS** (Failed 0, Skipped 0, 741ms), build 0W/0E. `GetBpRunSubmissionsQueryHandler` verified runnable — facts pin: cross-tenant guard (no client calls on mismatch), empty-run → empty list, happy-path label+value assembly in `displayOrder`, snapshot-as-label-authority (drop data-only keys / null missing values), intake-first ordering (wall-clock-independent), missing-snapshot skip (siblings still render). Commit `cec69a1e` (docs(backlog)), pushed origin+bitbucket ahead=0.
- **Audit (INVARIANT 260522-A):** row accurate — test file (183L) present only in `a043cacb`, never re-run/modified (git log on file = single commit); handler + endpoint exist → category (d) real-ready, claim "shipped but unrun" confirmed. No silent prior-close. Pure unit (NSubstitute, no DB); zero impl/contract change.

## 2026-06-06 (optimize pass — 11 closed sections moved from backlog.md)

### WF-BP-MULTI-RUN-500 — ✅ SHIPPED v2.137p157 2026-06-06 (filed 2026-06-05)
- **Fix:** nullable per-run `WorkflowInstance.DossierId` (Guid v7 minted unconditionally in `StartBusinessProcessRunCommandHandler` — literal-independent) + index filter B' (`[CompletedAt] IS NULL AND [DossierId] IS NULL`) → N concurrent BP runs, non-BP one-active preserved byte-identical. Migration `20260606083540` (Down=3b fail-loud). Layer-1 `SaveChangesAsync` 2601/2627→`GOV_WFL_020` 409 (scoped to index name). FE per-attempt `X-Request-Token` + 409 toast. Commits `2f5f80bd` feat + `d8ac8f74` incidental contract-test fix, PUSHED ahead=0.
- **Tests:** 10 real-SQL integ + 6 BP unit PASS · preflight 3-const synced B' · build 0E/0W · review APPROVE_WITH_CONCERNS (HIGH-1 FE flat-status + MEDIUM-1 guard index-scope fixed+re-verified).
- **Carry-fwd:** `BP-DOUBLE-SUBMIT-TOKEN-BE-TEST` (AC6 automated BE-integ test descoped — harness lacks AntiDoubleSubmit/Redis; production path verified-by-inspection: middleware registered `Program.cs:674` + reads `X-Request-Token` + FE sends it).

### BP-DOUBLE-SUBMIT-TOKEN-BE-TEST — ✅ SHIPPED v2.137p158 (2026-06-06)
- **Ask:** BE-integration test for the StartRun double-submit guard — same `X-Request-Token` within 5s → 2nd 409, no 2nd WorkflowInstance; two DISTINCT tokens → both 200 + 2 runs.
- **Audit verdict (premise STALE):** the row's "harness lacks AntiDoubleSubmit/Redis" was an unverified p157 assumption. Empirically: `app.UseAntiDoubleSubmit()` (`Program.cs:674`) is unconditional → runs in the "Testing" env (WebApplicationFactory = real `Program.cs` pipeline); `ApiFactory` does NOT replace `ICacheService` → real `TwoTierCacheService` (L1 + L2 Redis) on the live TestContainers Redis. Task was assertion-only, not harness-wiring.
- **Shipped:** `BpMultiRunHttpTests` +3 double-submit facts (same-token→409+count==1; distinct→2 runs; same-token-2-tenants→both 200) + 1 idempotency fact. Build 0E/0W · 5 PASS/1 skip/0 fail · `AntiDoubleSubmitMiddlewareTests` 14/14 · `IdempotencyMiddlewareTests` 3/3.
- **Bonus prod fixes (user-approved scope-expand, 2 one-liners):** `AntiDoubleSubmitMiddleware` + `IdempotencyMiddleware` both read the wrong tenant claim (`"tenantId"` vs canonical `"tenant_id"`) → per-tenant cache key collapsed to `"anon"` in prod. Idempotency twin was higher-severity (cached 2xx **body** → cross-tenant leak). Both fixed `tenant_id ?? tenantId ?? "anon"` + regression-lock facts. `IDEMPOTENCY-TENANT-CLAIM-COLLAPSE` finding → resolved in-scope.

### VAULT-INIT-MISSING-BNV-PEPPER — ✅ RESOLVED 2026-06-06 (filed 2026-06-05, ~5min, found during /aqt-runapp)
- **Symptom:** API floods 1498 `VaultApiException: Failed to fetch email pepper` on boot — ALL for tenant `a1b2c3d4…b7c2` (BNV). `EmailHashBackfillService` iterates every tenant's users at startup → fails each BNV user.
- **Root cause:** `vault-init.sh:28` `DEV_TENANTS` lists only `…0000`,`…0001`,`…b7c1`(BTC) — omits `…b7c2`(BNV), a real seeded demo tenant.
- **Impact:** non-blocking for DevTenant demo (`…0000` pepper present → admin/officer/viewer@dev.local login OK; BNV demo users not loginable) — but boot log noise + BNV email-ops would fail.
- **Workaround applied (ephemeral):** seeded `secret/data/aqt/encryption/email-pepper-…b7c2`=DEV_PEPPER into running Vault (HTTP 200). LOST on Vault container restart (dev in-memory backend).
- **Durable fix APPLIED:** added `a1b2c3d400000000000000000000b7c2` (BNV) to `vault-init.sh` `DEV_TENANTS` + self-documenting comment. BNV pepper now seeds on every Vault init (no more ephemeral workaround). Verified tenant id vs `TenantSeedProfiles.cs:40`.
- **Status:** RESOLVED — durable fix committed. Takes effect on next Vault container init (`reset-all` or fresh `docker compose up vault-init`).

### BP-FORM-OPTIONS-SNAPSHOT — SHIPPED v2.137p148 (2026-06-03): version-snapshot carries EnumRef field options
- **Shipped:** `FormTemplateVersionFactory` now serializes static field options (`OptionPayload` Id/Value/LabelVi/LabelEn/DisplayOrder, sorted by DisplayOrder) as an additive-nullable `Options` key on `FieldPayload` (SchemaEnvelopeVersion stays 1, PrefillSource precedent). `form-snapshot-adapter.ts` maps `f.options` → `FormFieldDto.options` (String(id)); `renderInteractiveField` EnumRef already reads inline `field.options` → Select renders choices. Full chain verified: producer eager-loads `.ThenInclude(f=>f.Options)`; all 3 BP handlers (intake/state/transition) pass raw schemaJson through; zero `UnmappedMemberHandling.Disallow` in src → additive key safe for every reader. BE `FormTemplateVersionFactoryTests` 9/9 + Forms.Domain 132/132 · FE tsc 0E + adapter 5/5 + forms suite 201/201 · code-review APPROVE 0 Crit/High/Med. No migration/entity/API/depmap.
- **Backward-compat:** old optionless snapshots parse to null (tested both sides); Active templates self-heal on republish via shared `Snapshot()` factory (no backfill, YAGNI). Dev caveat: `DemoFormTemplateSeeder` `muc_do_uu_tien` snapshot optionless until next `reset-all` regenerates it. No prod template affected.
- **Note:** `Radio` is NOT a FormFieldType — EnumRef is the only static-choice type (scope correctly EnumRef-only; original row's "Select/Radio" framing was imprecise).

### BP-TRANSITION-FORM-E2E — SHIPPED v2.137p150 (2026-06-03): live e2e for transition requiresForms path
- **Shipped:** NEW `ExecuteBusinessProcessTransitionHttpTests.cs` (BE integration, 2 facts) + NEW `business-process-transition-form.spec.ts` (FE Playwright). BE drives real `POST /api/v1/business-processes/runs/{id}/transitions` over WebApplicationFactory + Testcontainers SQL 2022 → Fact 1 `requiresForms=true` + inline snapshot whose `schemaJson` contains the seeded field key (real un-mocked version fetch) + run **measured** unchanged in initial state (short-circuit before tx); Fact 2 form-supplied + unbound template → 422 binding-guard pre-authz (gate cleared). Self-contained seed mirrors `BusinessProcessRepositoryFormMapsTests.SeedAsync`; tenant-aligned to the auth client. FE spec opens the confirm modal ("Xác nhận chuyển trạng thái") with inline form when a form-gated transition is reachable, graceful-skip otherwise. BE 2/2 PASS (46s) · build 0E/0W · FE eslint + playwright-compile clean · review APPROVE 0 Crit/High/Med. Zero prod change, no migration/entity/contract/depmap.
- **Audit-at-slice-start — stale premise:** row's "no seed configures one" was stale (`DemoBusinessProcessSeeder.cs:203-205` now binds a required form to Submit), but only proves the binding persists; the live HTTP requiresForms path + the two un-mocked Dapper queries were still uncovered → category (b) partial-stale, proceeded.
- **Scope note — submit+advance descoped from BE (covered elsewhere):** the submit-then-advance path is fail-CLOSED (`ExecuteTransitionCommandHandler.cs:154-161`, no `WorkflowTransitionAssignment` → GOV_WFL_004); standing up that authz for a T4-Low item is disproportionate. Covered by the FE spec + handler unit happy-path-with-form + Workflow `ExecuteTransitionContractTests` (seeds authz, asserts advance). FE spec dormant against the out-of-box Draft seed until the demo BP is activated (noted in-spec).

### GEO-WARD-DATA-34-PROVINCES — DONE-with-residual v2.137p145 (2026-06-03): seeded all 34 provinces' wards
- **Shipped:** `admin-units-wards-34.json` 387→3351 rows (34 L1 + 3317 L2). 31 new provinces sourced from per-province Nghị quyết UBTVQH15 (NQ 1654–1687) via xaydungchinhsach.chinhphu.vn. **Count-gate per province** (extracted == NQ-stated total) + **independent script re-verify** against shipped JSON (not trusting harvester self-report) — caught + fixed 2 truncations (Nghệ An 104→130, Đắk Lắk 83→102). National 3317 reconciles cleanly (3321 official − 4 HCM Dầu-Tiếng dedup). Seeder code unchanged; old 387 rows byte-preserved. `AdministrativeUnitSeederTests` 4/4 real-SQL · build 0E/0W · code-review APPROVE 0 Crit/High/Med. Provenance + per-province NQ/count audit: `plans/260530-0803-reseed-all-modules-demo-data/research/dia-gioi-31-tinh-source.md` + `research/geo-fragments/` (gitignored — see changelog v2.137p145).
- **Residual (accepted, named):** per-ward **name-level** ground-truth re-fetch done only for Cao Bằng + Đắk Lắk + Quảng Trị; the other 28 provinces' names rest on harvester faithfulness to cited chinhphu.vn sources (count-gate is blind to hallucination-at-correct-count). Bar OK for T4 demo/dev data; revisit trigger = production-grade address accuracy requirement → exhaustive per-province re-fetch + diff. Cosmetic: intra-province ward ordering not uniform (phường-first vs xã-first across provinces) — does not affect correctness/codes/dropdown.

### GEO-WIDGET-FILTER-ACCENT-INSENSITIVE — SHIPPED v2.137p146 (2026-06-03): diacritic-insensitive address-widget search
- **Shipped:** new `foldDiacritics()` shared util (`web/src/shared/utils/fold-diacritics.ts`) — lowercase → NFD → strip combining marks (U+0300–U+036F, incl. horn + dot-below) → map `đ`→`d` (U+0111 no-NFD trap) → trim. `address-field-widget.tsx` `filterOption` now folds BOTH input + label → "nghe" matches "Nghệ An", "phuong" matches "Phường …". Exact-match preserved. `fold-diacritics.test.ts` 14/14 vitest (one case per Unicode trap category: đ/horn/dot-below/breve/hook-above/tilde/uppercase/null-empty/search-match) · `tsc --noEmit` 0E · code-review APPROVE 0 Crit/High. FE-only, no BE/contract/migration/entity → no depmap change.
- **Scope discipline:** util reusable but applied only to the address widget; ~13 other Selects filter small/non-geo lists (accent-sensitivity tolerable). Same-scale sibling filed → `MASTERDATA-PAGE-FILTER-ACCENT-INSENSITIVE`.

### MASTERDATA-PAGE-FILTER-ACCENT-INSENSITIVE — SHIPPED v2.137p147 (2026-06-03): diacritic-insensitive search on admin master-data page
- **Shipped:** swapped ALL 5 accent-sensitive search points on `master-data-page.tsx` to `foldDiacritics(…).includes(foldDiacritics(…))` (reuse v2.137p146 util): 2 Select `filterOption` (province `:159` / district `:171`) + 3 `AdminTableToolbar` row-search filters (`Object.values(item).some(…)` at `:201/:229/:257`). "nghe" now matches "Nghệ An" in both Select and the primary table search.
- **Scope expansion (user-approved):** row originally scoped to the 2 Selects; surfaced that the page had 5 same-data search points and a 2-of-5 fix = inconsistent half-fix → user opted for all 5. Folding non-name columns (id/code/numeric) harmless; `.trim()` minor improvement. `tsc --noEmit` 0E · util `fold-diacritics.test.ts` 14/14 (engine unchanged) · 1 unrelated pre-existing shared-suite failure (`admin-tenant-selector` race) proven independent via `git stash`. FE-only, no BE/contract/migration → no depmap change.

### SEED-ORPHANPURGER-TEST-DATA-STALE — SHIPPED v2.137p149 (2026-06-03): SeedOrphanPurgerTests 4/4 green
- **Shipped:** repaired the 3 raw-SQL INSERT bugs in `SeedOrphanPurgerTests.cs` (test-only). Audit-at-slice-start (column-by-column vs migrations) found THREE, not the 2 the row named: (1) `InsertIdentityUserAsync` omitted `MustChangePassword` (bit NOT-NULL no-default) → added =0; (2) `OrgUnitId` is `bigint` (no FK), helpers bound a Guid → signatures now `long orgUnitId`, callers pass `1L`; (3) **unnamed by row:** both INSERTs supplied `Id = Guid.NewGuid()` into a `bigint IDENTITY` PK → dropped `Id` so SQL auto-assigns. All 4 facts failed at setup before any assertion ran; the fix exercises the full `PurgeAsync` assertion path (orphan removal incl. history, valid-row preserve, ceiling-throw, cross-tenant isolation) for the first time. `dotnet test --filter SeedOrphanPurger` via Testcontainers SQL Server 2022 → **4/4 PASS** (1m41s) · build 0E/0W. Zero prod change, no migration/entity/contract/depmap.
- **Lesson:** the row named 2 bugs; the column-by-column audit caught a 3rd (Id IDENTITY) — same shape as the WFE-AUTH-FAIL "unnamed siblings" reinforcement. Don't trust the row's bug list; verify the full INSERT against schema.

### WF-DEMO-SEED-SMOKE — DONE 2026-06-02 (live reset-all smoke PASS): all 6 demo seeders converge + write correct data both tenants
- **Origin:** P4–P7b demo seeders (v2.137p137–p142). Owed a live reset-all smoke — automated seed-correctness was real-SQL-proven per seeder, but the live boot path (gated `AddHostedService` + `$MK_*` marker convergence) and end-to-end were static-only.
- **Ran (2026-06-02):** `scripts/reset-all.ps1 -Force -SkipBuild` (user-authorized nuke; DB was pre-seeder — last reset 2026-06-01 predated all 6 seeders, audit-confirmed 0 demo rows before). **All 10 markers converged** at real host boot — 4 core (`TenantUser`/`MasterData`/`OrgTree`/`TenantAssignment`) + 6 demo (`[DemoWorkflowSeeder]`/`[DemoFormTemplateSeeder]`/`[DemoBusinessProcessSeeder]`/`[DemoIntegrationSeeder]`/`[DemoRulesSeeder]`/`[DemoReportingSeeder]` `All tenants complete`), incl. the expected 2-restart convergence for the `TenantAssignmentSeeder` cross-module boot-order race. Verify: 3 tenants / 11,506 users / 1,166 orgunits.
- **DB data-verify PASS (both tenants BTC `…b7c1` + BNV `…b7c2`):** Workflow 1 Draft def/tenant (12 states + 12 transitions = 6×2) · Forms 1 Active `demo-intake-form`/tenant (14 fields = 7×2, 2 versions) · BP 1 Draft `demo-business-process`/tenant · Integration 1 Active `demo-partner`/tenant + 2 contracts · Rules 1 Draft RuleSet/tenant (4 rules + 2 decision tables) · Reporting 1 Active def/tenant (string-slug TenantId `a1b2c3d4-…-b7c1/b7c2`) + 2 widgets + 2 dashboards + **1 GLOBAL** query-catalog (load-bearing global-once design confirmed).
- **Scope note:** user-scoped depth = marker convergence + DB data-shape verify. The optional e2e instance-routing (activate→instance→task-routing; BP activate→run) was NOT driven live this round — already covered by integration tests (`DemoWorkflowSeederTests` T4 activate-via-endpoint 204 / R1-R4; `DemoBusinessProcessSeederTests` R1-R4). Not a correctness gap.
- **Known gap observed (tracked separately):** `BP-FORM-OPTIONS-SNAPSHOT` — form version snapshot omits `FormFieldOption`, so the EnumRef dropdown renders empty at BP-run. Expected, not a regression. (Not exercised live — BP-run UI was out of the chosen depth.)

### SEED-TXN-WRAP-P4-P5 — SHIPPED v2.137p143 (2026-06-02): phase-1/phase-2 transaction wrap on DemoWorkflowSeeder + DemoFormTemplateSeeder
- **Origin:** P6 code-review M1. `DemoBusinessProcessSeeder` wraps its 2-phase save (root → form maps) in `BeginTransactionAsync`/`CommitAsync` so a phase-2 fault cannot leave a committed root with zero children that the idempotency probe (keyed on the root) then skips forever on a plain restart.
- **Gap (closed):** `DemoWorkflowSeeder` (3-phase: def+states → transitions → assignments) + `DemoFormTemplateSeeder` (2-phase: template+fields → publish+snapshot) shared the same probe-on-root + multi-SaveChanges shape with NO enclosing transaction. A mid-seed crash on a live DB (not reset-all, which nukes first) could leave a half-wired def/template that re-seed never repairs.
- **Shipped:** wrapped each seeder's phases in a single `BeginTransactionAsync`/`CommitAsync` (begin after the existence-probe early-return, commit after the last SaveChanges) — exact mirror of P6. Probe + read stay outside the tx; per-tenant transaction scoped inside the static seed method (one tenant's failure can't roll back another's). Audit-at-slice-start confirmed both seeders had the gap empirically (3 + 2 SaveChanges, no tx). Retry-strategy throw ruled out — no `EnableRetryOnFailure` on either prod registration (`WorkflowInfrastructureModule.cs:39` / `FormsInfrastructureModule.cs:26`). Build slnx 0E/0W · `DemoWorkflowSeederTests` 4/4 + `DemoFormTemplateSeederTests` 4/4 real-SQL (new tx path genuinely exercised) · code-review APPROVE 0 Crit/High/Med/Low. No migration / no entity / no signature change. Brings P4+P5 to txn-wrap parity with P6/P7c/P7a/P7b.
- **Residual (accepted, unchanged from P6):** transaction-rollback + StartAsync boot path not fault-injection-tested. Manual reset-all smoke still owed via `WF-DEMO-SEED-SMOKE`.

---

## 2026-06-04 (per-node fork authoring shipped)

### ~~WF-BRANCH-I18N-COMMIT~~ RESOLVED (commit `fb856276`, parallel-branch i18n arc)
- **Resolved:** the parallel-branch authoring i18n keys (`workflow.branch.*` + `workflow.designer.{addFork,addForkHint,addForkDisabled,gatewayProperties,transitionProperties}`) were committed cleanly in `fb856276` (en+vi, NO BP-run entanglement) — UI no longer relies on inline `t(key, fallback)` defaults. Confirmed via `git show fb856276`. The unrelated in-flight BP-run/migration i18n hunks (`common.actions/deleted/saved`, `migration.totalCount/skippedCount`) were a SEPARATE concern, later **bundled** into this session's `57de9dfb` (per-node fork authoring) — user chose bundle over surgical split; additive/harmless. Zero depmap change.

---

## 2026-06-02 (optimize pass — 12 closed sections moved from backlog.md)

### ~~WEB-TYPECHECK-TEST-CI-GATE~~ CLOSED 2026-05-28 (STALE — gate already exists since 2026-04-14)
- **Closed:** 2026-05-28 — STALE per 260522-A slice-start audit. The row's premise ("`typecheck:test` is NOT in any CI workflow") is false at HEAD: the CI `frontend` job already runs the exact command at `.github/workflows/ci.yml:260-261` (`npx tsc --noEmit -p tsconfig.test.json` == `typecheck:test` per `web/package.json:18`). Wired by `bb555c1e` (2026-04-14, "wire CI typecheck:test gate (T5-21-C)") — six weeks before this row was filed. Frontend job is active (push main + PR, no `if:false`).
- **Real gap (≠ row's remedy):** the 26 errors slipped despite the gate because the gate is not *enforced* — repo is GitHub private-free, so branch protection / required-status-checks are unavailable (`gh api .../required_status_checks` → 403 "Upgrade to Pro or make public"). CI goes red but does not block push/merge to main. The row's remedy ("add to CI job") would not have prevented the rot even if the step were missing. Achieving the goal needs either GitHub Pro/public (branch protection) or a local pre-push hook (husky — new devDep, dev-workflow change); both are larger decisions, neither scheduled here.
- **Note:** the 2026-05-28 state-audit (`thursday-eve-260528-full-state-audit-reconciliation`) listed this OPEN-REAL ~30min based on a subagent grep for the alias name `typecheck` (false negative — CI uses the inline `tsc` command, not the `npm run` alias). Corrected here per 260527-F.

### ~~WF-ASSIGNMENT-I18N-KEYS~~ CLOSED 2026-05-28: add i18n keys for assignment apply feedback
- **Was:** `workflow.designer.applied` / `workflow.designer.applyValidationFailed` used inline fallbacks (commit `921d01f5`). Add to `vi.json`/`en.json` for consistency. Non-blocking (fallbacks work).
- **Closed:** ✅ 2026-05-28 — SHIPPED (locale-only, 0 code change). Scope expanded +2 (transparent): `properties-sidebar.tsx` had **4** fallback-only `workflow.designer.*` keys all missing from both locales, not the 2 named — added `applied`, `applyValidationFailed`, `stateProperties`, `transitionProperties` to `web/src/core/i18n/locales/{vi,en}.json` (same component, same defect class, zero-risk to bundle). Second `designer` block (rules namespace, vi.json:1539) untouched. Verified: both JSON parse clean + FE `typecheck` 0 errors.

### ~~WEB-DTC-FORMTEMPLATE-STALE-TESTS~~ CLOSED 2026-05-28: fix stale FormTemplatesApiTests
- **Was:** `FormTemplatesApiTests` used `.GetInt64()` on the form-template id, but the API returns a Guid PublicId since the Phase-07 T2 retier (all routes `{publicId:guid}`). `PublishFormTemplate_WithoutAdminRole_Returns403` 404s on the numeric `/12345/publish`.
- **Closed:** ✅ 2026-05-28 — SHIPPED (test-only, 0 production change). Row understated scope (claimed only the 403 test): empirically **5 break sites** — 4× `.GetInt64()` on the now-Guid `data.id` (lines 100/164/177/206) + the numeric `/12345/publish` route. Swapped all to the Guid flow mirroring `WorkflowDefinitionsApiTests` (T2 sibling): `.GetGuid().Should().NotBe(Guid.Empty)` / `.GetGuid().Should().Be(templateId)`; 403 test now hits `/{Guid.NewGuid()}/publish` so the `:guid` route matches and policy denies pre-handler (403, not 404). Auth verified non-issue: harness `TestEffectivePermissionService` maps SystemAdmin → full perms (`Forms.Template.Manage`), CaseOfficer → empty → 403 (`ApiFactory.cs:97-103`). Also refreshed the class XML-doc header (route + policy contract). **Verified: `FormTemplatesApiTests` 9/9 PASS** (real DB via TestContainers, 3s), build 0W/0E.

### ~~SEED-BTC-NAMING-CLEANUP~~ CLOSED 2026-06-02 (SHIPPED v2.137p135): rename `Btc*` shared seeder helpers to generic names
- **Origin:** P2.3c (v2.137p133, `6eb9be0e`) — final slice deleted all BTC-only seeders. Four `Btc`-prefixed artifacts survived as tenant-agnostic shared helpers (misnomer).
- **Shipped:** `BtcOrphanPurger`→`SeedOrphanPurger` · `BtcSeederTenantContext`→`SystemSeederTenantContext` · `BtcSeedHelpers`→`OrgSeedHelpers` · `BtcSeedConstants` DELETED, folded into `TenantSeedProfiles.BoTaiChinh` (`.TenantId` byte-identical; `.TenantName` was dead, `.TenantCode`/`.SeedVersion` test-only). Updated all refs + filename-keyed cross-schema whitelist (`CrossSchemaSqlScanner.cs:63`) + comments + 2 test `using`. `BtcSeedConstantsTests.cs` removed (tautological). `git mv` preserved history.
- **Verify:** build `src/AqtCoreFW.slnx` 0E · architecture cross-schema guard PASS (rebuilt — Architecture.Tests is OUTSIDE slnx, `--no-build` used stale DLL) · behavior-equivalent tests `TenantAssignmentSeederTests`+`OrgTreeSeederEquivalenceTests` 9/9 + `OrgSeedHelpersExtendedTests` 5/5 PASS · code-review APPROVE-WITH-NITS (6/6 dimensions, ops-script sweep clean). Zero `Btc(OrphanPurger|SeederTenantContext|SeedHelpers|SeedConstants)` left in `*.cs`.

### ~~P6-DEFER-4 (5b-M2, T3 P2): I3/I4 BP HTTP integration tests~~ CLOSED 2026-05-28 (SHIPPED)
- **Origin:** 5b plan called for I3 (BP-path happy) + I4 (BP-path empty-pool) HTTP integration tests.
- **Shipped:** NEW `src/tests/AqtCoreFW.Tests.Integration/BusinessProcess/StartBusinessProcessRunFirstTaskTests.cs` — 2 facts driving real SQL via the BP-Run endpoint (`POST /api/v1/business-processes/{id}/runs`):
  - **I3** happy path → 200 + exactly 1 Open first-task staged atomically (CandidateRefsJson != null, AssigneeId null, tenant match) — distinct from the DTC harness which only asserts instance+form.
  - **I4** reframed (user-approved): the BP path blocks no-handler EARLIER than the workflow-instances path. BP activation runs the R1-R4 graph gate (`ValidateDefinitionGraphAsync`, Phase 5c shipped AFTER the 5b spec); R2 rejects a non-final state whose outgoing transition has no assignment → activation 422 GOV_WFL_019 + BP stays inactive. The originally-spec'd run-start GOV_WFL_014 is UNREACHABLE on the BP path (a no-assignment def can never activate). I4 asserts the real gate.
  - Also fixed a stale comment in `WorkflowInstancesCreateFirstTaskTests.cs` that referenced a non-existent unit-test file (`InProcessWorkflowModuleClientFirstTaskTests`, A7a/b/c) — the claimed BP-path substitute coverage was fictional; replaced with a correct pointer.
- **Validation:** filtered 2/2; full `AqtCoreFW.Tests.Integration` 161 passed / 3 failed (the 3 PRE-EXISTING + unrelated: Identity `UsersApiTests` 500s, `SchemaUniversalsTests` datetime2 precision — outside blast radius). Build 0W/0E. Review 9.4/10. Test-only; zero production change.

### ~~WF-POOL-TASK-CANDIDATE-INDEX-WRITE-GAP~~ CLOSED v2.137p121 2026-05-27: Pool tasks invisible in production — producer never writes WorkflowTaskCandidateIndex
- **Closed:** 2026-05-27 v2.137p121 commit pending. Fix (Option C) shipped: producer wired in `WorkflowTaskFactory.ApplyCandidateSnapshot` (Phase 01) + regression guard `WorkflowPoolTaskProducerIndexIntegrationTests` + architecture guard `ParallelBranchPoolPathGuardTests` (Phase 02) + backfill migration `20260527072445_BackfillWorkflowTaskCandidateIndexFromSnapshot` (Phase 03).
- **Status:** ✅ CLOSED — plan `plans/260527-1230-pool-task-candidate-index-write-fix/`, changelog `v2.137p121`.

---
### ~~T2-02: Test Plan Phases 4-6~~ CLOSED 2026-05-28 (STALE — all 3 phases shipped-scattered)
- **Closed:** 2026-05-28 — STALE per 260522-A audit-at-slice-start (two empirical scouts, trait-counts + file-existence verified, NOT trusting backlog/roadmap claims). All three phases of the comprehensive-test-plan framework are operational and actively maintained; the row tracked them as "PLANNED" because nobody updated it after the v2.13/v2.15 ships.
  - **Phase 4 (Contract/regression):** ✅ shipped v2.13 (roadmap Phase 18). `tests/contracts/AqtCoreFW.Tests.Contracts/` — **38** `[Trait("Category","Contract")]` methods / 15 files (9 API-envelope + 6 domain-event-schema). Trait-based selective execution. Last-touched 2026-05-27.
  - **Phase 5 (Security):** ✅ shipped (scattered). `tests/integration/AqtCoreFW.Security.Tests/` — **53** `[Trait("Category","Security")]` methods / 8 files (Auth/Injection/Leakage/LogSanitization/Upload) + **49** compliance tests under `tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/`. Last-touched 2026-05-27.
  - **Phase 6 (Performance):** ✅ shipped v2.15 (roadmap Phase 20). `tests/performance/` — **21** k6 scripts + `run-all.sh` harness + `docs/slo-definitions.md` (p95 200/400ms thresholds). Last-touched 2026-05-01.
- **Residual gaps (all defensible YAGNI — framework-guaranteed, NOT re-opened):** no explicit SQLi/XSS injection tests (EF parameterized queries + ASP.NET/React auto-escaping), no standalone crypto/hashing tests (.NET BCL), no API-version-diff backward-compat tests (no API versioning in use), no committed perf-baseline trend data (`tests/performance/results/` near-empty).
- **Real remaining test-framework work** (genuinely greenfield, NOT this row): comprehensive-test-plan **Phases 7-10** — Reliability/Resilience, Accessibility (WCAG 2.2 AA), Installation/Migration, CI/CD gates. Open a fresh epic if/when prioritized.
- **Note:** referenced source spec `Tieu_chuan_Test_He_thong.md` (roadmap line 377) does not exist in repo.

---

### ~~RT2-15-PHASE-01-FU~~ CLOSED v2.137p122 2026-05-27: Integration test E2E coverage for PreviousHandler producer

- **Closed:** 2026-05-27 v2.137p122 — SHIPPED. New `WorkflowPreviousHandlerProducerIntegrationTests` (2 tests: P1 happy-path assigns next task to prior completer + single-assignee shape; P2 fail-closed 422 on no prior completed task) + seed helper `InlineWorkflowDefinitionFactory.PreviousHandler.cs`. Drives the REAL `WorkflowTaskRepository.GetLastCompletedTaskAsync` query (mocked unit suite `WorkflowHandlerResolverTests.cs:400-478` could not). P1 hardened (review Med-1): three distinct identities so it discriminates PreviousHandler from the Initiator clause mode. Full WF.Integration 74/74, build 0W/0E.
- **Status:** ✅ CLOSED. The row's named unit file `WorkflowTransitionAssignmentPreviousHandlerProducerTests.cs` was a stale claim — actual PreviousHandler unit coverage lives in `WorkflowHandlerResolverTests.cs:400-478`; the "blocked-on Phase 02-04 gate" expired (all phases shipped).
- **Residual (Low, non-blocking):** ordering-with-multiple-completed-tasks (`ORDER BY CompletedAt DESC` with >1 completed row) not exercised — single completed task makes direction moot; query is trivially correct.

### ~~RT2-15-PHASE-03-FU~~ CLOSED v2.137p122 2026-05-27 (stale — covered split-layer): Integration test for ByRole predicate-pool resolver

- **Closed:** 2026-05-27 v2.137p122 — STALE. Audit (260522-A, two read-only scouts) found all 6 acceptance points already covered, split across the architecturally-correct two layers:
  - Points 1,3,4,5 + point 6 direct-member arm → `WorkflowPoolTaskProducerIndexIntegrationTests` (shipped v2.137p121): drives real ByRole producer → index row with EXACT `PrincipalRefId="REVIEWER"` → inbox include/exclude + claim 200/403 + tenant isolation.
  - Points 2 + 6 group-inherited arm (PQ-1 A "ByRole=direct-only, NOT group-inherited") → `IdentityModuleClientRoleNamesTests.GetUserRoleNamesAsync_UserWithRoleViaGroupOnly_ExcludesRoleName` (`tests/integration/AqtCoreFW.Identity.Integration.Tests/IdentityModuleClientRoleNamesTests.cs:59`, harness `SeedUserWithGroupOnlyRoleAsync:188`). Production query `InProcessIdentityModuleClient.cs:152-159` confirmed direct-only (`JOIN AspNetUserRoles`, no `GroupRoleAssignment` union).
- **Status:** ✅ CLOSED. The row assumed both arms belonged in ONE workflow integration test; a stubbed-`IIdentityModuleClient` workflow test CANNOT prove the direct-only role semantic (the stub returns whatever it's told) — that contract is correctly owned by the Identity-module test against the real query. Composition holds: inbox matches on `GetUserRoleNamesAsync` (proven direct-only) → group-inherited user receives no matching role → excluded from ByRole pool.

### WF-TESTER-FULL-ASSEMBLY-GATE (process, NEW 2026-05-26, note-only): Enforce full-assembly test suite runs on clause-mode slices

- **Discovered:** 2026-05-26 RT2-15 Phase 04-FU close audit. Phase 04 tester agent ran filtered test subset (`--filter`), produced filtered-green pass, missed RED `WorkflowGraphPublishValidationTests.I7` stale on main. Same pattern occurred Phase 02 ("deferred to Phase X" claim masking real failures per INVARIANT 260525-C).
- **Scope:** Process note — tester prompts on RT2-15 clause-mode slices (Phases 01-04 + Phase 05 cross-mode sweep) MUST run full `WF.Integration` + `WF.Application` assemblies without `--filter` subsets before claiming test-green status. Clause-mode changes ripple across multiple test domains (read-side `WorkflowTaskRepository`, validator `WorkflowGraphPublishValidationTests`, handler integration). Filtered subsets hide cross-domain failures.
- **Effort:** N/A (process guidance only, no code change).
- **Status:** ✅ RESOLVED 2026-05-27 — generalized (not hardcoded): tester subagent gained Mapping-Strategy **F (cross-domain spread → full module assemblies)** + project mirror in `docs/code-standards.md` §Cross-Domain Test Selection. Clause-mode-specific framing dropped (arc closed p116); rule is now domain-agnostic (≥2 distinct test-domain dirs → full `*.Integration`+`*.Application`, no `--filter`). Archive-eligible at next optimize pass.
- **Reinforces:** INVARIANT 260525-C (tester full-assembly gate prevents filtered-green masking failures) + INVARIANT 260525-G (when pattern-mirroring, mirror test-LAYER-counts not just production-layer counts).
- **Linked:** [[monday-pm-260525-readside-extension-group]] (260525-G observational origin).

### ~~WF-CONCURRENCY-FLAKE~~ CLOSED 2026-05-28: Deterministic state-conflict race coverage for concurrency-token guard

- **Was:** probabilistic full-stack `Task.WhenAll` race test `SimultaneousRace_ExactlyOneSucceeds_OtherFailsWithStateConflict` (`[Fact(Skip)]`); assertion `totalStateConflict.Should().BeGreaterThan(0)` flaky because app-layer `InvalidTransition` guard frequently won all 5 attempts before the DB-layer concurrency token could fire.
- **Closed:** ✅ 2026-05-28 — SHIPPED (test-only, 0 production change). User-approved Option A: relocated coverage to deterministic infra-test. NEW `tests/unit/AqtCoreFW.Workflow.Infrastructure.Tests/Persistence/WorkflowInstanceConcurrencyTokenTests.cs` (1 test) **reuses existing `WinnerFirstInterceptor`** (no new interceptor) mirroring `ClaimGuardExceptionDiscriminationTests`: winner ctx commits Draft→Submitted first; loser ctx (interceptor-gated) saves via `WorkflowInstanceRepository.SaveChangesAsync` against stale `CurrentStateId` token → `DbUpdateConcurrencyException` → `StateConflictAtBoundary` (GOV_WFL_013). Anti-tautology asserts `!=InvalidTransition`. Removed the flaky Skip-test from the integration file (kept `SequentialRace` app-layer guard test). **Verified:** new test 2/2 deterministic, full `Workflow.Infrastructure.Tests` 85/85, integration `SequentialRace` 1/1, builds 0E. Review 9/10 (1 Low §5 label fixed, 2 nits non-blocking).
- **Note:** row understated scope — literal "reuse interceptor" impossible in full-stack DI test (can't pre-designate winner/loser); infra-layer relocation reusing `WinnerFirstInterceptor` verbatim was the actual deterministic path.

### ~~BACKLOG-MIXED-SECTION-TRIM~~ ✅ CLOSED 2026-05-27 (P4 housekeeping): Trim shipped narrative from MIXED backlog sections
- **Scope (corrected at slice-start per audit 260522-A — row premise had drifted):**
  - `BusinessProcess Definition MVP` — row claimed "P0 shipped, P1–P8 in scope"; reality = **ALL P0–P8 SHIPPED** (+ ARCH-FU2.x program). Collapsed entire shipped narrative → 1 pointer + verbatim snapshot in `completed.md`; kept **open follow-ups only** (28 P*/Post-MVP bullets — zero dropped).
  - `T3-API-ENVELOPE-FE-RENDER` — row claimed "PARTIAL-CLOSED p118, keep (a)(c)(d)"; reality = **FULLY CLOSED p120** (a+c+d shipped, b resolved p119). Whole-section move to `completed.md`, nothing kept active.
  - **Defect fixed in-pass:** orphaned `WF-CONCURRENCY-FLAKE` block (had lost its `###` header, sat below the moved T3 section) — header restored so the open DEFERRED row survives the move.
- **Result:** `backlog.md` 552→508 lines; `completed.md` +94 (verbatim shipped snapshots). Zero open item dropped.

---

## 2026-05-28 (BP cross-module DTC fix + carry-forward closures — moved from backlog.md)

### ✅ BP-RUN-DTC-FIX (T2 🟠 High) — SHIPPED 2026-05-28 (`0a675d3c`→`9072eb88` + `4a1d6fd7`, pushed origin+bitbucket)
- **Was:** "Khởi tạo quy trình thất bại" / HTTP 500 `NotSupportedException: Implicit distributed transactions have not been enabled` — 2 BP handlers wrapped cross-module DbContext writes in one `TransactionScope` → DTC promotion (.NET 10 default `ImplicitDistributedTransactions=false`, reproduces on ALL platforms, not just Linux).
- **Fixed:** Approach A ambient `ICrossModuleUnitOfWork` (one shared SqlConnection + local tx; enlist first-touch via `SetDbConnection`+`UseTransaction`; `DapperReadDbConnection` ambient-aware; session_context once on Begin; MARS precondition + `MarsConnectionStringGuardTests`; both IDisposable+IAsyncDisposable). 2 handlers + 4 repos + base `GenericRepository` enlist guard. Cooperative-rollback contract (`HasAmbientTransaction`) preserved.
- **Proven:** 4 real-SQL integration tests green (200+both rows / atomic rollback 0-orphan / transition 200 / read-then-write no-DTC); regression Workflow.Integration 74/74 · Identity 212/212 · Forms 9/9 · WF.App 587 · BP.App 95 · Arch 108 · build 0E. Review APPROVE-WITH-NITS; H1 (base write-method enlist guard) fixed-now by centralising in `GenericRepository`.
- **Bonus pre-existing bug fixed (`29498a73`):** `WorkflowInstanceRepository.GetByIdAsync(Guid)` keyed the long PK via base `FindAsync` → Guid lookup threw ArgumentException masked as 400; 3 prod call paths (GetTenantForInstance/ResolveTransition/CreateWorkflowTask) silently broken → query by PublicId.
- **Process lesson:** tester subagent reported "Ready for CI" on build-clean WITHOUT running tests (didn't pass seeding); the IDisposable startup bug only surfaced via a full host-booting assembly (74→0). Journal `docs/journals/journal-260528-1415-bp-cross-module-dtc-fix-shipped.md`.

### ✅ WEB-TYPECHECK-TEST-COMMIT (T4 🔵) — DONE 2026-05-28 (`1a56c684`, pushed)
- 4 FE test-infra files fixed `typecheck:test` 26→0 (node types in tsconfig.test.json, Vitest-4 `Mock<AuthFailureDeps[...]>`, `@xyflow/react` Required NodeProps, unused-import/redeclare). Re-verified before commit: typecheck 0 / 3 specs 22/22 runtime / lint 0. Follow-up `WEB-TYPECHECK-TEST-CI-GATE` remains open in backlog.

### ✅ WF-PUSH-ASSIGNMENT-ARC (T4 🔵) — DONE 2026-05-28
- `7789de3a` / `921d01f5` / `88be845a` (assignment round-trip arc) + the DTC-fix commits pushed origin + bitbucket.

## 2026-05-27 (mixed-section trim — moved from backlog.md)

Shipped narrative relocated from two MIXED active sections so `backlog.md` holds open work only. Verbatim snapshots below (incl. then-open follow-ups for context); the still-open follow-ups remain tracked in `backlog.md`.

### BusinessProcess Definition MVP — P0–P8 ALL SHIPPED (v2.137p22→p31, 2026-05-14→05-17) — full shipped snapshot

## 📋 BusinessProcess Definition MVP — **P0 SHIPPED 2026-05-14 17:00 ICT, P1–P8 IN SCOPE**

**Vision:** Low-code platform — Admin định nghĩa Workflow + Forms, bundle thành "Quy trình nghiệp vụ" (BusinessProcessDefinition). User tạo hồ sơ chọn BP → system render intake + state forms + transition forms theo config.

**Plan:** `plans/260420-1957-business-process-definition-mvp/` (gitignored, local) | **Latest ship:** v2.137p22 BP MVP P0 OTel-only — workflow.execute_transition.duration_ms + workflow.allowed_transitions.query_duration_ms histograms, 4 files ~50 LOC additive, no schema change.

**Scope:** MVP = P0-P8 (~14.5d BE+FE, ~8-10d parallel)
- [x] **P0: Workflow OTel baseline (0.5-1d)** — ✅ SHIPPED 2026-05-14 v2.137p22 | 2 histograms + Meter registration. P4 scout discovered proposed indexes redundant; OTel-only per plan Decision-D4 (live telemetry → future index justification post-MVP).
- [x] **P1: FormTemplateVersion entity + Publish snapshot (1.5d planned · ~0.5d actual)** — ✅ SHIPPED 2026-05-14 ~17:15 ICT | Immutable snapshot table + `FormTemplateVersionFactory` JSON envelope v1; atomic `AddVersionAsync` + `UpdateAsync` single SaveChanges. RLS-parity migration (`20260514102007_AddFormTemplateVersion`). Defense-in-depth tenant filter on JOIN. Forms.Domain 116/116 + Forms.Application 143/143 + Architecture 97/97 PASS, build 0E/0W. **P1-FU ✅ SHIPPED 2026-05-14 ~22:10 ICT v2.137p24 — RepublishFormTemplate** (Active→v_n+1 snapshot, no clone-to-Draft; semantic = snapshot label).

- [x] **P2: FormSubmission BP-Run linkage + query-latest pattern (0.5d planned · ~2h actual)** — ✅ SHIPPED 2026-05-14 ~23:55 ICT v2.137p25 | 12 files (5 new, 7 modified). Added `long? WorkflowStateId` (T1 FK, NOT Guid — scout caught) + factory `CreateForBpRun` on FormSubmission. Migration `20260514153042_AddRunLinkageToFormSubmission` with filtered covering index `IX_FormSubmissions_LatestStateForm` (4 cols INCLUDE 3 + WHERE both BP cols NOT NULL). NEW `IFormSubmissionRunClient` (4 methods) + `FormSubmissionSummaryDto` in Forms.Contracts. NEW `IWorkflowModuleClient.GetTenantForInstanceAsync` for R14 cross-tenant guard (arch test caught initial cross-schema raw SQL — refactored to client method). `SubmitFormCommand` extended with optional BP fields; handler branches: BP path uses CreateForBpRun + skips ApprovalWorkflow instance-create. Tests: Forms.Domain 130/130 (+7) + Forms.Application 155/155 (+4) + Architecture 97/97 PASS, build 0E/0W. Code-review 8.7/10. **P2-FU filed:** FU1 (P3, ~0.2d) consent duplication on repeated BP state-form edits; FU2 (P3, ~0.1d) doc/seal SubmitFormCommand.WorkflowInstanceId internal-use; FU3 ✅ SHIPPED 2026-05-15 v2.137p26 (~1.5h actual) — `FormSubmissionRunClientTenantIsolationTests` 3/3 PASS: instance-not-found rejects GOV_BP_002, tenant-mismatch rejects GOV_BP_002, happy-path dispatches SubmitFormCommand with BP fields and returns submission Id. NSubstitute boundary tests (no DB fixture — R14 guard is a cross-module client boundary, not an EF filter); placed in `TenantIsolation.Tests` per FU intent. **Late-evening rule (c) override note:** post-22:10 + 10th ship of day, user explicitly overrode scout-decision in hand. Migration regen incident: `dotnet ef migrations remove --no-build` removed P1 migration unexpectedly; recovered via `git checkout HEAD --` pre-commit. **Lesson:** never run ef migrations with --no-build after entity edits.
- [x] **P3: BusinessProcess module scaffold (1d+1-2h planned · ~3.5h actual)** — ✅ SHIPPED 2026-05-15 ~21:08-00:30 ICT v2.137p27 | 18 new files + 7 modified (~750 LOC). NEW `AqtCoreFW.BusinessProcess.{Domain,Application,Infrastructure,Presentation}` 4 csprojs + slnx `/modules/businessprocess/` folder. 3 entities (`BusinessProcessDefinition` root T2 with `byte[] RowVersion` + paired `IntakeFormTemplateId/Version` GOV_BP_010 both-or-neither factory invariant + 2 children carrying duplicated `TenantId` for RLS defense-in-depth per Forms P1 pattern). `BusinessProcessCreatedEvent` via `BusinessProcessDomainEventBase` (mirrors Rules pattern). `BusinessProcessErrorCodes` (GOV_BP_001..010). `IBusinessProcessRepository` + `BusinessProcessRepository` (Pattern-A R13: inline tenant filter on reads with SystemAdmin bypass, strict throw on writes). `BusinessProcessDbContext` inherits `ModuleDbContext` schema=businessprocess. 3 EF configs with composite indexes (auto from base) + unique `(TenantId, Code)` on root + `(BpId, StateId|TransitionId, FormId)` on children + cascade FK + RowVersion concurrency. Migration `20260515142934_InitialBusinessProcess` — 3 tables + indexes + RLS block embedded (D3 Session-1 lock): `RlsMigrationHelper.GenerateRlsPolicyStatements("businessprocess", 3 tables)` batch-isolated per-statement (no GO, per `phase6-rls-restore` precedent). `Down()` mirrors with `DropRlsPolicyStatements` before `DropTable`. `OutboxSchemaRegistry` row `businessprocess` registered (D5 Session-1). Api wiring: 3 ProjectRefs in Api csproj + `AddBusinessProcessModule().AddBusinessProcessInfrastructure(builder.Configuration)` after Integration in Program.cs. TenantIsolation.Tests: BP.Infrastructure ProjectRef + `BusinessProcessDbContext` migration step in `RlsPolicyPresenceFixture` + `RlsPolicyPresenceTests.ExpectedPolicies` matrix extended with 3 BP rows (count comment 36→39, method renamed `_Equals_ThirtySix` → `_Equals_Expected`). Tests: Architecture 97/97 + Forms.Domain 130/130 + Forms.Application 155/155 PASS, build 0E/0W. **Late-evening rule (c) override #3** — user explicitly overrode "fresh-mind morning P3" memory directive after explicit risk gating; baselines hold = override held.
  - **P3-FU1** (P3, ~3-4h, OPEN): Full `BusinessProcessTenantIsolationTests` (Audit-style enforcement) — cross-tenant SELECT denial via `SESSION_CONTEXT` (mirrors `RuleSetTenantIsolationTests`). Requires dedicated fixture. The `RlsPolicyPresenceTests` extension already locks the policy-presence regression class (main risk surface); enforcement-level test is belt-and-braces.
  - **P3-FU2 ✅ SHIPPED 2026-05-15 v2.137p27a** (~1h actual, 19 tests): `AqtCoreFW.BusinessProcess.Domain.Tests` unit project — factory invariants (`IntakeFormTemplate*` both-or-neither rule GOV_BP_010, `<=0` arg guards, `versionNumber >= 1`, domain event raise, immutability of `Code`/`WorkflowDefinitionId`, Activate/Deactivate idempotent, RowVersion bump, UpdateMetadata partial). All 19 facts PASS.
  - **P3-FU3 ✅ SHIPPED 2026-05-15 v2.137p27a** (~0.5h actual, 2 tests): Repository Pattern-A R13 cross-tenant write rejection — `AddAsync` + `UpdateAsync` both throw GOV_BP_002 on ambient-tenant mismatch (before EF tracking). 2 facts PASS; no positive-control happy-path test per YAGNI (InMemory provider doesn't honor IsRowVersion).
  - **P3-FU2.5** (P3, LOW, reviewer-suggested): Parametrised child `StateId` range guards (boundary value testing).
  - **P3-FU2.6** (P3, LOW, reviewer-suggested): ArchUnit rule against direct `DbSet.Add` bypass on aggregate root (enforce via repo only).
- P4: BP REST API + Run lifecycle + 3 permissions (~2.5d total, PARTIAL-SHIPPED)
  - **P4 Slice 1 ✅ SHIPPED 2026-05-15 v2.137p27b** (~30 min actual): perms scaffold only — 3 PermCodes constants (`BUSINESSPROCESS.BP.VIEW/MANAGE/ACTIVATE`) + delta-merge seeder + 3 VN names + PermCodesTests bump 125→128. Tests 10/10 + Architecture 97/97 PASS, build 0E/0W. Deliberate scope cut from full P4 at ship #8 / 22:30 ICT — 5th consecutive fatigue rule-c override (user-confirmed twice). Clean break point: 128-baseline ready for Slice 2 to add controller + handlers without test-count contention.
  - **P4 Slice 2A-Read ✅ SHIPPED 2026-05-16 v2.137p27c** (~1.5h actual): 2 query handlers (`ListBusinessProcesses` + `GetBusinessProcessById` with Pattern A tenant guard) + 3 GET endpoints (`/api/v1/business-processes`, `/active`, `/{id}`) + authorization policies wired. 5 unit tests (Pattern A cross-tenant reject + SystemAdmin bypass + pagination clamp + NotFound). BP.Application.Tests 5/5 + Architecture 97/97 + PermCodesTests 10/10 + BP.Domain 19/19 + BP.Infrastructure 2/2 PASS, build 0E/0W. Code-review 9.4/10 APPROVE_WITH_NITS. **P4-FU (Slice 2A-Read):** L-1 LIKE-escape `_` and `[` in filters (defer to 2A-Write), L-3 ListActive pagination cap doc (LOW), L-6 SQL emission integration test (defer to 2B). Cache deferred to 2A-Write (bundled with invalidation logic, YAGNI no separate read-side caching pre-write-side mutation visibility).
  - **P4 Slice 2A-Write ✅ SHIPPED 2026-05-16 v2.137p27d** (~1.5h actual): 4 command handlers (Create/Update/Activate/Deactivate) + Pattern-A guards + audit log + 2 outbox events (`BusinessProcessActivatedEvent`, `BusinessProcessDeactivatedEvent` per D5) emitted idempotently (no-op Activate on already-active = zero events); MemoryCache wrap+invalidation (`bp:{tenantId}:active` 5-min TTL per D7) on List + GetById queries, invalidate on all mutations; 4 mutation endpoints (`POST /`, `PUT /{id}`, `POST /{id}/activate`, `POST /{id}/deactivate`) with `[Authorize(Policy = BUSINESSPROCESS.BP.{MANAGE,ACTIVATE})]`; unit tests 22/22 (4 command handlers + partial-update perms); **code-review 9.3/10 APPROVE_WITH_NITS folded M-1 (AuditBehavior verb list Activate/Deactivate coverage + DRY refactor) + M-2/L-1/L-3 inline.** Architecture 97/97 + PermCodesTests 10/10 + SharedKernel.AuditBehavior 9/9 + BP.Domain 19/19 + BP.Infrastructure 2/2 PASS, build 0E/0W. **NEW (project-wide improvement in M-1 fix):** AuditBehavior now covers Activate/Deactivate verbs + DRYed duplicated CommandVerbs array — any future module using these command types gets audit compliance for free.
    - **P4-2A-Write-FU1** (Low, ~1h): TOCTOU 409 on duplicate Code Create — Currently returns 500 (inner EF exception) when Create attempts duplicate (same-tenant, same Code value). Requires new repo method `AddOrFailOnDuplicateAsync(tenantId, code, ...) → Result<BusinessProcessDefinition, AddOrFailError>` returning discriminated outcome (so Application doesn't pull EF dependency for exception matching). Defer until next write-side phase with capacity.
    - **P4-2A-Write-FU2** (Low, Slice 2B dependency): `WorkflowDefinitionId` cross-module existence check — `CreateBusinessProcessCommandValidator` deferred check to 2B (same call brings up inverse `IBusinessProcessQueryClient` for Forms/Workflow consumers). Will wire `IWorkflowModuleClient.ResolveWorkflowDefinitionAsync(wfId, tenantId)` at validator stage.
    - **P4-2A-Read-FU** (Low): L-1 LIKE-escape `_` and `[` in filter literals (defer cross-module sweep); L-3 ListActive pagination cap doc; L-6 SQL emission integration test (defer to 2B).
  - **P4 Slice 2B ✅ SHIPPED 2026-05-16 v2.137p27e** (~0.5h actual, scope-trimmed): `IFormTemplateQueryClient` contract (1 method `GetVersionSchemaByInternalIdAsync`) + `InProcessFormTemplateQueryClient` Dapper impl (Forms.Infrastructure, defense-in-depth TenantId both sides) + `GetIntakeFormSchemaQuery` handler (Pattern-A tenant guard, BP repo lookup, Forms client delegation) + `BusinessProcessIntakeFormDto` DTO + controller `GET /{id}/intake-form` endpoint (200/204/404). **Scope-trim rationale:** Original 5-method `IBusinessProcessQueryClient` deferred — zero current consumer for any method (each future caller pulls matching method when built: R5 DeleteFormTemplate guard + R6 DeactivateDefinition guard + Slice 2C runtime handlers will add methods as needed, avoiding YAGNI gold-plating). Unit tests 6/6 (GetIntakeFormSchemaQueryHandlerTests: Pattern-A reject + NotFound + happy path + null-intake + Forms-client-empty). BP.Application.Tests 28/28 + Architecture 97/97 + PermCodesTests 10/10 + BP.Domain 19/19 + BP.Infrastructure 2/2 PASS, build 0E/0W. Code-review 9.4/10 APPROVE_WITH_NITS. ProjectReference `Forms.Contracts` → BP.Application (confirmed pre-existing).
    - **P4-2B-FU1** (Low, blocked-on-R5): `IsFormTemplateReferencedAsync` cross-module method on future `IBusinessProcessQueryClient` when R5 DeleteFormTemplate guard built. **Scout 260516-1733:** consumer = P7 admin delete-dialog only (red-team R5 rejected BE pre-check at `DeactivateFormTemplateCommandHandler`). Wire as P7 vertical-slice inline — do NOT build in isolation (YAGNI: dead method until FE lands).
    - **P4-2B-FU2** (Low, blocked-on-R6): `ListActiveBpsUsingWorkflowAsync` cross-module method when R6 DeactivateDefinition guard built. **Scout 260516-1733:** `DeactivateWorkflowDefinitionCommandHandler` does NOT exist (aggregate method only at `WorkflowDefinition.cs:169`). Consumer = P7 UI surface per R6 lock; may also be POST-MVP. Wire as P7 inline or defer.
    - **P4-2B-FU3** (Low, blocked-on-Slice2C): 3 BP-internal repo methods (ResolveWorkflowDefinitionId, GetStateFormTemplateIds, GetTransitionFormTemplateIds) when Slice 2C runtime handlers built
  - **P4 Slice 2C ✅ SHIPPED 2026-05-16 v2.137p27f** (~0.6d actual): 3 runtime endpoints on `BusinessProcessesController` (`POST /{id}/runs` Start Run, `POST /runs/{instId}/transitions` discriminated response, `POST /runs/{instId}/state-forms/{stateId}` Submit state form). 2 new methods on `IWorkflowModuleClient` (`CreateInstanceByInternalIdAsync` long→Guid resolve; `ResolveTransitionIdAsync` returning `TransitionLookup(TransitionId,FromStateId)` to support R8 form-before-exec ordering); `WorkflowInstanceResult` + `ExecuteTransitionResult` each gained `*InternalId: long` for FE state-form lookup (vestigial Guid fields kept, Phase-4c cleanup candidate). New `IBusinessProcessRuntimeQueries` (3 same-schema Dapper helpers — `GetRuntimeContextAsync`, `GetStateFormTemplateIdsAsync`, `GetTransitionFormTemplateIdsAsync`) + `BusinessProcessRuntimeQueries` impl. Ambient `TransactionScope` (ReadCommitted, AsyncFlowEnabled) on Start Run + Transition (form-bundled path); single op on Submit State Form. Pattern-A pre-DB tenant guard on all 3 handlers, no SystemAdmin bypass on writes. Unit tests 14 facts (StartBpRun 6 + ExecBpTransition 5 + SubmitStateForm 3) + 1 M-2 regression = 15 total + 3 integration tenant-isolation facts. Code-review 9.3/10 APPROVE_WITH_NITS. M-2 fixed inline (partial transition form payload). Test sweeps: BP.Application 43/43 + BP.Domain 19/19 + BP.Infra 2/2 + Forms.Application 155/155 + Workflow.Application 431/431 + Architecture 97/97 + TenantIsolation BP 3/3 + PermCodes 10/10 PASS. Build 0E/0W.
    - **P4-2C-FU1** (Low): Add FluentValidation validators (non-empty `EntityType`, non-empty `ActionName`) — currently defense flows downstream to Workflow domain factory + ResolveTransitionIdAsync null short-circuit; explicit gate cleaner.
    - **P4-2C-FU2** (Medium, BP-WRITES-WIDE): Module-wide cross-tenant returns `ValidationError → 422` instead of `NotFoundError → 404`; existence-leak signal across all 6 BP write handlers (Create/Update/Activate/Deactivate + 3 runtime). Defer cross-module sweep — established pattern, not regressed by 2C.
    - **P4-2C-FU3** (Low): `BpRuntimeContext.BusinessProcessDefinitionId` field unused (YAGNI drop next touch).
- ~~P5: Cases refactor + CaseStatus string + BP-aware (2.5d)~~ (OBSOLETE — Cases module removed 2026-05-01, PR #71)
- **P6 ✅ SHIPPED 2026-05-16 v2.137p28** (~1d actual): `IFormPrefillContextClient` (SharedKernel.Contracts) + `InProcessFormPrefillContextClient` 7-step orchestrator + `FormField.PrefillSourceJson` column + EF migration `AddPrefillSourceToFormField` + snapshot envelope additive `FieldPayload.PrefillSource` + `IFormSubmissionRunClient.GetFullSubmissionAsync` (Json/Table branch) + `IFormTemplateQueryClient.GetVersionSchemaByPublicIdAsync` + controller `GET /api/v1/business-processes/runs/{instId}/forms/{formId}/prefill-context?version=N` (BP.View, 200/400/404) + `BusinessProcessMetrics.PrefillDurationMs` OTel histogram. 16 new unit + 2 new integration tests; full regression green (BP.App 59/59 + TenantIso 8/8 + Forms.Domain 130 + Forms.App 155 + Arch 97 + Workflow.App 431 + BP.Domain 19), build 0E/0W. Code-review 9.2/10 APPROVE_WITH_NITS. Deviation D1: contract returns nullable `IReadOnlyDictionary<string, object?>` instead of `Result<T>` (SharedKernel.Contracts no-PackageReferences policy beats plan literal); controller wraps null→404.
    - **P6-FU1 ✅ SHIPPED 2026-05-16 v2.137p28a** (~30min actual): FluentValidation rules on `PrefillSourceJson` for `AddFormFieldCommandValidator` + `UpdateFormFieldCommandValidator` — `MaximumLength(2000)` (mirror DB column) + JSON well-formedness via `JsonDocument.Parse` when non-empty (`.When(!IsNullOrWhiteSpace)` preserves null=no-change + empty=clear-pointer semantics). Envelope-shape validation (Guid + SourceFieldKey) **intentionally deferred** to `PrefillSourceMeta.TryParse` at resolve-time — keeps Forms.Application independent of envelope evolution + matches partial-fail-tolerance contract D4. 4 + 5 = 9 new tests; Forms.Application 164→165/165 + Architecture 97/97 PASS, build 0E/0W. Code-review 9.3/10 APPROVE_WITH_NITS, L-3 folded inline (Add validator empty-string symmetric test). Optional carry-overs (non-blocking): L-1 unreachable null-guard in `BeValidJson` (defensive harmless), L-2 helper copy-pasted across 2 validators (extract when 3rd `*Json` rule appears), L-5 magic `2000` duplicated 4 places (entity ctor + 2 validators + EF config — constant-extract candidate).
    - **P6-FU2 ✅ SHIPPED 2026-05-16 v2.137p28b** (~45min actual): `JsonValueNormalizer` static helper (Forms.Application/Services/) — JsonElement→CLR boundary normalizer applied at `InProcessFormSubmissionRunClient.GetFullSubmissionAsync` Json branch. Number ladder `long → decimal → double` mirrors `FormDataPreprocessor.JsonElementToObject` write-path (financial-data round-trip precision). **D9 from P6 plan explicitly invalidated** — xmldoc trail at JsonValueNormalizer + InProcessFormPrefillContextClient. 12 unit facts; Forms.Application 165→177 PASS, Architecture 97 PASS, build 0E/0W. Review 9.0/10 APPROVE_WITH_NITS, N-1 (decimal preferred) + L-1 (perf note) + L-2 (out-of-int64 fact) folded inline. L-4 carry-over (trivial comment on `_ =>` arm).
    - **P6-FU3 ✅ SHIPPED 2026-05-16 v2.137p28b** (~30min actual): Integration round-trip `PrefillJsonElementNormalizationTests` (Forms.Integration.Tests) using existing `WebAppFixture` + Testcontainers SQL — submits typed-value form via public API, resolves `IFormSubmissionRunClient` from DI scope, calls `GetFullSubmissionAsync` directly, asserts no JsonElement + native CLR types. **Scope clarification (documented in test file xmldoc):** memory's "4-mode storage matrix (Json/Table × source/target)" framing was incorrect — target storage is irrelevant to prefill resolution; the real axis is source backend = 2 modes, and Table-mode already returns native CLR via Dapper. One Json-mode fact suffices to prove FU2 closed the asymmetry. Table-mode regression implicitly covered by `FormEndpointsTests.FormLifecycle_CreatePublishSubmitQuery_Succeeds`. L-3 carry-over (nested-object fact, deferred pending P7/P8 FE MVP-scope clarification).
- **P7 ✅ SHIPPED 2026-05-16 v2.137p31** (~2.5d actual, reduced scope): Admin UI — BP CRUD list/detail pages + 3-step wizard (basics → workflow+intake → state+transition form maps read-only). 12 new FE files + 6 modified (~420 LOC). BP List page + wizard component steps + Antd Form + react-hook-form for BPDefinitionDto binding. Orval integration for BP API endpoints (Create/Update/Activate/Deactivate/GetById/List). State/transition form maps shown read-only in detail page (full form-map designer deferred — requires BE write endpoints not yet built). TanStack Query for caching BP state + form lists. I18n keys `page.adminBpDesigner.*`. Code-review 9.5/10 APPROVE_WITH_NITS (lifted from 8.5 after C1 Rules-of-Hooks fix + H2 error surfacing + M3 i18n). Tests: Playwright e2e smoke. **P7-FU filed:**
    - **P7-FU1 ✅ SHIPPED 2026-05-16 v2.137p33** (~3d actual): Form-map designer (wizard step 3) — BE write endpoints for state-form + transition-form map atomic replace + FE editable tables with dnd-kit reorder + React Flow read-only visualization. 3 new commands (ReplaceFormMaps) + handler + validator + controller PUT endpoint + 6 modified BE + 3 modified FE + 2 i18n. Atomic replace-all semantics via ClearXxx + AddXxx inside single SaveChanges. RowVersion ETag concurrency. Tests: BP.App 78/78 (+10), BP.Domain 25/25 (+6), AuditBehavior 10/10 (+1), Arch 97/97. Build 0E/0W. Code-review 9.0/10 APPROVE_WITH_NITS (lifted 7.8→9.0 after C-1 audit verb fix). **Plan-deviation corrected:** T1 entities (WorkflowState/WorkflowTransition) have no Guid AK — pivoted to `GetDefinitionStructureAsync` returning (long, Guid) pairs for injection defense instead of assuming Guid-resolve impossible. **4 follow-ups filed (P7-FU1.1/1.2/1.3/1.4).**
    - **P7-FU1.1 ✅ SHIPPED 2026-05-17 v2.137p41** (~15min actual, KISS path): Bump picker pageSize 100 → 500 via shared `PICKER_PAGE_SIZE` constant in `business-process-admin-api.ts`; applied to both `useFormTemplatePicker` + `useWorkflowDefinitionPicker`. Soft-cap documented; tenants >500 templates trigger admin re-pick on back-fill. 2 files +13/-3 LOC. FE typecheck PASS. Code-review 9.4/10 DONE_WITH_CONCERNS (BE `Math.Clamp` cap deferred as P7-FU1.1-FU).
    - **P7-FU1.1-FU ✅ SHIPPED 2026-05-17 v2.137p42** (~20min actual): `Math.Clamp(PageSize, 1, 1000)` + `Math.Max(1, Page)` on `GetFormTemplatesQueryHandler` and `GetWorkflowDefinitionsQueryHandler`. Replaced `request.PageSize`/`request.Page` with clamped locals throughout (Offset, FETCH NEXT, meta echo). 4 new unit facts (2 Forms + 2 Workflow). Forms 8→10 + Workflow 431→433 + Architecture 97/97 PASS. Code-review 9.4/10 DONE_WITH_CONCERNS (M-1 folded inline = Workflow test parity; M-2 filed as ARCH-FU2 below).
    - **ARCH-FU2 ✅ SHIPPED 2026-05-17 v2.137p43** (~50min actual, **scope split per advisor**): Helper + ArchUnit rule shipped; **handler sweep deferred to per-module FUs** because advisor caught that some FE callers depend on absence of cap (`useRuleSets pageSize=9999`, `useBackupRecords pageSize=9999`, `useWorkflowTaxonomies pageSize=200`) — blanket 100-cap would break those flows. New `SharedKernel.Application.Pagination.PaginationDefaults` helper (2 tiers: `AdminMax=100` + `PickerMax=1000` + `DefaultPageSize=20` + `Normalize(page, pageSize, max)` returning `(int Page, int PageSize)`). New `PaginationClampScanner` + `PaginationClampScannerTests` — regex `\b(request|query|req|q|cmd|filter)\.PageSize\b` against handler files, compliant if file contains `Math.Clamp`/`Math.Min`/`PaginationDefaults`/`InclusiveBetween(1,`; skips Migrations/bin/obj + Controllers/ + Commands/ (false-positive layers). Allowlist 31 currently-unclamped handlers; count-snapshot test prevents silent expansion (forcing function). Architecture 97→99 + SharedKernel 199/199 PASS. Code-review 9.4/10 DONE_WITH_CONCERNS — N-1 (`req` alias) folded inline; M-1 (`Math.Min` defense-token weakness) + N-5 (FE-9999-dep inline comments) filed forward as ARCH-FU2.1. **Carry-over:** ARCH-FU2.x per-module sweep FUs (audit → identity → masterdata → forms → notifications → integration → collaboration → rules → reporting → ai → files); each FU shrinks allowlist count + decrements snapshot.
    - **ARCH-FU2.1 ✅ SHIPPED 2026-05-17 v2.137p44** (~30min actual): (a) Dropped `Math.Min` from `PaginationClampScanner.DefenseTokens` — only handler `GetMigrationItemsQueryHandler` affected, migrated to `Math.Clamp(_, 1, 200)` for consistency. (b) Added inline FE-9999-dep comments in PaginationClampScanner XML doc next to WorkflowTaxonomy-200 grandfathered entry (Backup has no handler, useBackupRecords skip N/A). 3 files +7/-4, Arch 99/99 + Workflow 433/433 PASS. Review 9.6/10 DONE_WITH_CONCERNS (N-1 folded inline; N-2 optional XML traceability link deferred).
    - **ARCH-FU2.2 ✅ SHIPPED 2026-05-17 v2.137p45** (~25min actual): Audit module pagination clamp sweep. 5 audit handlers (`GetAuditLogs`, `GetRtbfRequests`, `GetAiPromptTraces`, `GetLoginAudit`, `GetSecurityIncidents`) migrated from allowlist to `PaginationDefaults.Normalize(_, _, PaginationDefaults.AdminMax=100)`. Aligns BE with documented OpenAPI contract `pageSize (1-100)`. `PaginationClampScanner.cs` allowlist 31→26 (5 audit entries removed, section header annotated "SWEPT 2026-05-17"); `PaginationClampScannerTests.cs` `expectedCount: 31→26`; `PaginationDefaults.cs` XML doc fold. +2 canonical clamp tests in `GetAuditLogsQueryHandlerTests.cs`. Architecture 99/99 + Audit.Application 52→54 PASS. Review 9.6/10 DONE_WITH_CONCERNS (no folds required).
    - **ARCH-FU2.3 ✅ SHIPPED 2026-05-17 v2.137p46** (~30min actual): Identity module pagination clamp sweep. 7 identity handlers (`ListPendingAccessReviews`, `ListActiveSessions`, `ListCredentialPolicies`, `ListExternalIdentities`, `ListJitProviderConfigs`, `ListTenantsAdmin`, `ListUsers`) migrated from allowlist to `PaginationDefaults.Normalize(_, _, PaginationDefaults.AdminMax=100)`. FE pre-scout: all identity hooks pass pageSize<=100 (largest 100 at `external-identities-page` + `jit-provider-config-api`). `PaginationClampScanner.cs` allowlist 26→19 (both identity sections collapsed into single SWEPT marker); `PaginationClampScannerTests.cs` `expectedCount: 26→19`. +2 canonical clamp tests in `ListUsersQueryHandlerTests.cs` (PageSize=9999→AdminMax, Page=0→1). Architecture 99/99 + Identity.Application 243→245 PASS. Review 9.6/10 DONE clean (no folds). **Carry-over:** ARCH-FU2.4 masterdata (5, WorkflowTaxonomy FE-200-coord); .5-.x small modules (notifications/integration/collaboration/reporting/ai/files); rules BLOCKED on FE useRuleSets=9999 coord.
    - **ARCH-FU2.4 ✅ SHIPPED 2026-05-17 v2.137p47** (~25min actual): Masterdata module pagination clamp sweep — **first FU using two-tier pattern**. 3 handlers (`GetDictionaries`, `GetDictionaryItems`, `GetExternalMappings`) → `PaginationDefaults.AdminMax=100` (FE callers ≤30). 2 handlers (`WorkflowActionTaxonomyQueries`, `WorkflowStateTaxonomyQueries`) → `PaginationDefaults.PickerMax=1000` (FE `useWorkflowStateTaxonomies`/`useWorkflowActionTaxonomies` default pageSize=200 bulk-picker; AdminMax would truncate). Taxonomy handlers preserve Redis cache hit on canonical 200-row request by keeping `isDefaultParams` check on RAW `query.PageSize`. `BuildPaged(items, query, total?)` → `BuildPaged(items, page, pageSize, total?)` drop query-shape leak. `PaginationClampScanner.cs` allowlist 19→14 (masterdata block collapsed into SWEPT marker citing both tiers); `PaginationClampScannerTests.cs` `expectedCount: 19→14`. Architecture 99/99 PASS. No `MasterData.Application.Tests` project exists (only Integration DB-bound) — canonical clamp tests skipped per YAGNI; scanner+count snapshot serves as regression guard. Review 9.6/10 DONE clean (3rd consecutive). **Carry-over:** ARCH-FU2.5-.x small modules: notifications(2), integration(3), collaboration(3), reporting(1), ai(1), files(1). Rules BLOCKED on FE useRuleSets=9999 coord.
    - **ARCH-FU2.6 ✅ SHIPPED 2026-05-17 v2.137p49 — PROGRAM COMPLETE** (~20min actual): **Closes ARCH-FU2.x chain — allowlist 1→0.** `ListRuleSetsQueryHandler` migrated to `PaginationDefaults.Normalize(_, _, PaginationDefaults.PickerMax=1000)`. **FE coordinated in same FU:** `rule-set-list-page.tsx:40` `useRuleSets({pageSize: 9999})` → `useRuleSets({pageSize: 1000})` — RuleSet is config entity (tens typical), 1000-cap = 10x headroom. FE comment updated cite FU + future-refactor pointer (server-side search if count approaches 1000). Scanner XML doc body refreshed (stale `useRuleSets pageSize=9999` example removed; SWEPT marker + program-complete note added). 4 files. Architecture 99/99 PASS, Rules.Application 0E/0W, FE typecheck PASS. Review 9.6/10 DONE clean (**5th consecutive on ARCH chain**); L-1 stale-doc nit folded inline. **Final tier distribution: 28 @ AdminMax=100, 3 @ PickerMax=1000 (workflow taxonomies + rule-sets).** Allowlist deliberately retained empty as forcing function. **ARCH-FU2.x program SHIPPED end-to-end: 31 handlers across 11 modules, 6 FUs, ~3h cumulative wallclock, 5×9.6/10 reviews, zero regressions.**
    - **ARCH-FU3 ✅ SHIPPED 2026-05-17 v2.137p50** (~25min actual): **Direct-Math.Clamp audit — post-program normalization.** Audited all `Math.Clamp(_, _, PageSize)` sites for migration to `PaginationDefaults.Normalize` helper. 13 sites found; 7 migrated, 6 kept (justified). **Migrated (6 Page+PageSize handlers → `Normalize`):** `ListSodViolationLogsQueryHandler` (AdminMax; drops local `MaxPageSize=100` + unused `DefaultPageSize=50`), `GetWorkflowDefinitionsQueryHandler` (PickerMax; drops local `MaxPageSize=1000`), `ListDelegationsQueryHandler` (AdminMax), `ListBusinessProcessesQueryHandler` (AdminMax; cache key unaffected — checks raw `request.PageSize == 100` for caller-intent semantics), `GetFormTemplatesQueryHandler` (PickerMax; drops local const), `ListFilesQueryHandler` (AdminMax; +Pagination using). **Migrated (1 cursor handler — literal→tier const):** `WorkflowInstanceQueryHandlers.cs:30` `Math.Clamp(_, 1, 100)` → `Math.Clamp(_, 1, PaginationDefaults.AdminMax)` (no `Normalize` since no Page param; mirrors Collaboration cursor pattern). **Kept w/ justification:** `GetMigrationItemsQueryHandler` (custom cap=200 + FluentValidation `InclusiveBetween(1,200)` — doubly defended; new tier YAGNI), `SearchUsersQueryHandler` (no Page param — autocomplete/TOP+cap=50, semantically not pagination), `Sql/ElasticsearchSearchService` (shared search infra, OOS for handler-targeted scanner), `GetMessagesQueryHandler` / `SearchMessagesQueryHandler` (already use `PaginationDefaults.AdminMax`). **Pure refactor, zero behavior change.** 7 files. Architecture 99/99 + Identity.App 245 + Workflow.App 433 + Workflow.Infra 58 + BP.App 88 + Forms.App 179 + Files.App 112 PASS (1214 unit + 99 arch). Review **9.7/10 DONE clean** (**6th consecutive ≥9.5 on ARCH chain**) — verified tier preservation, cache-key semantics, no anon-obj shorthand collisions, no public-signature changes. Closes program-tail follow-up surfaced during /aqt-next post-FU2.6 audit.
    - **ARCH-FU2.5+ ✅ SHIPPED 2026-05-17 v2.137p48** (~35min actual): **Largest single FU in ARCH-FU2.x chain — bundled sweep of 13 handlers across 7 modules.** All migrated to `PaginationDefaults.AdminMax=100` (FE pre-scout: all callers ≤100; largest 100 at `usePartners` exactly at ceiling). Handlers: notifications(2 — `GetNotificationTemplates`/`GetUserNotifications`), integration(3 — `ListMessageLogs`/`ListPartners`/`ListContracts`), collaboration(3 — `GetConversations`/`GetMessages`/`SearchMessages`), forms(2 — `ListFormSubmissions`/`ListViewDefinitions`), reporting(1 — `GetSavedQueries`), ai(1 — `GetChatHistory`), files(1 — `GetDocumentTemplates`). **Cursor-based pattern:** `GetMessages` + `SearchMessages` have no Page param (cursor via `BeforeMessageId`/`AfterMessageId`) → `Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax)` directly (scanner accepts both `Math.Clamp` and `PaginationDefaults` defense tokens). `PaginationClampScanner.cs` allowlist **14→1** (7 module sections collapsed into single SWEPT marker; only `ListRuleSets` remains — BLOCKED on FE `useRuleSets=9999` coord); `expectedCount: 14→1`. Architecture 99/99 + Forms.App 179/179 + Ai.App 112/112 + Collab.App 22/22 PASS. Review 9.6/10 DONE clean (**4th consecutive on ARCH chain**). **Program essentially complete — only FE-blocked rules handler remains.** Carry-over: ARCH-FU2.6 rules sweep blocked on FE PR dropping `pageSize: 9999` from `useRuleSets`.
    - **P7-FU1.2 ✅ SHIPPED 2026-05-17 v2.137p34** (~2.5h actual, scope-reduction YAGNI): State/transition picker UX upgrade. Picked **option (b2) scope reduction** — surface `WorkflowDefinitionPublicId Guid?` in `BusinessProcessDetailDto` so FE reuses EXISTING `GET /api/v1/workflow/definitions/{id:guid}` endpoint (already returns full states+transitions with `displayNameVi/En` + `actionLabelVi/En`). ~80% scope cut vs original 2h+1h. BE: `IWorkflowModuleClient.TryGetDefinitionPublicIdAsync(long, Guid, CT) → Guid?` + impl + DTO field + handler dep-inject + cross-module call. FE: 1 DTO field mirrored, swap InputNumber → Select with display names sourced via `useGetApiV1WorkflowDefinitionsId` useMemo'd option arrays. i18n: 5 keys (vi.json + en.json, M-1 folded). Tests: BP.App 78→82 (+4), Workflow.Infra +3, Arch 97/97 + FE typecheck PASS. Review 9.0/10 APPROVE_WITH_NITS, M-1 + M-2 folded inline. **4 carry-overs filed below (P7-FU1.2.1-1.2.4).**
    - **P7-FU1.2.1 (Low, ~10 LOC):** i18n Vi-only fallback in picker labels. `s.displayNameVi || s.name` always picks Vi; en users see Vietnamese state names in dropdowns. Use `i18n.language === 'en' ? displayNameEn : displayNameVi`.
    - **P7-FU1.2.2 (Low, ~20 LOC):** Read-only `ReadOnlyTabs` mode still displays raw bigint state/transition IDs. Same lookup map + fetch already built; thread `bp` prop to ReadOnlyTabs.
    - **P7-FU1.2.3 (Low):** Distinguish "no workflow linked" (`!workflowDefinitionPublicId`) from "fetch failed" in picker `notFoundContent` — currently both render same message.
    - **P7-FU1.2.4 (Nit):** Inline `// safe: bigint IDENTITY << 2^53` comment on `Number(s.id)` conversions — defensive prophylaxis if any future ID scheme switches to snowflake.
    - **P7-FU1.3 (Low, ~1h FE):** ReactFlow workflow visualization placeholder — `BusinessProcessDetailDto` doesn't include workflow graph; consume separate `GET /workflow/definitions/{id}/graph` endpoint and pass to canvas.
    - **P7-FU1.4 (Low, ~1h tests):** Integration test for ETag concurrent-replace race — currently only in-memory pre-check tested; real EF concurrency race relies on GlobalExceptionHandler (→ 409). Add WebAppFixture+Testcontainers integration test for two-session race.
    - **P7-FU2 ✅ SHIPPED 2026-05-16 v2.137p32** (~1h actual): Forms-page delete-guard showing BP refs (R5). BE `IBusinessProcessQueryClient.ListBpsReferencingFormTemplateAsync` endpoint (1 new contract csproj + in-process Dapper impl + controller GET endpoint) + FE delete-confirm pre-flight dialog. 5 new files + 9 modified. Tests BP.App 68/68 + Forms 177/177 + Arch 97/97 PASS. Code-review 9.0→9.5+ (M-1 false alarm on i18n, verified present). **P7-FU2.1 (Low, ~1h FE):** Active form-template deactivate guard surface — current FU2 guard only wired to Draft DELETE path; if FE adds Active-template Deactivate button, wire pre-flight there too. **P7-FU2.2 (Low, ~30min FE):** Consolidate `BpRefBlockModal` + `WfBpRefBlockModal` (95% duplicated). **P7-FU2.3 (Low, ~15min FE):** Defense-in-depth handler guard — check `isFetchingRefs` inside delete handler, not only via Popconfirm.
    - **P7-FU3 (Low):** Workflow deactivate dialog showing BP refs (R6). Needs BE `IBusinessProcessQueryClient.ListBpsReferencingWorkflowDefinitionAsync` endpoint. ~3h BE + 1h FE. (Bundled with FU2 as Phase A; same new contract csproj.)
    - **P7-FU4 (Low):** Form-map view shows raw bigint IDs (state/transition/template) — needs name resolution. ~30min if BE adds names; ~2h if FE-only via parallel queries.

- **P8 ✅ SHIPPED 2026-05-16 v2.137p29** (~2.5d actual): User UI — BP Run picker + intake form + state form tabs + transition action modal + prefill integration. 11 new FE files + 4 modified (~380 LOC). `new-bp-run-wizard.tsx` + `bp-run-detail-page.tsx` + `bp-run-detail-header.tsx` + `form-tabs-container.tsx` + `transition-confirm-modal.tsx` + `use-prefill-context.ts` hook + route entries + nav integration. Orval regen for BP + BP-Runs API. Form rendering via direct composition of `renderInteractiveField` + helpers (YAGNI: no shared wrapper extraction, 2 consumers only). Prefill dict → Antd `Form initialValues` (memoized). AllowedActions polling via TanStack Query (no explicit `refetchInterval` — TanStack defaults). Playwright e2e smoke: start run → fill intake → navigate state forms → execute transition → confirm form modal → advance state. Code-review 9.2/10 APPROVE_WITH_NITS. **Deferred P8-FU items:**
  - **P8-FU1 ✅ SHIPPED 2026-05-17 v2.137p37** (~45min actual, scope-correct premise): State-form fields render gap closed. **Premise correction:** backlog said "bigint state-form-map id" but `BpStateFormMapDto.id` is the row's Guid PublicId (FE always sees Guid AKs); route uses `:guid` pair, not `:long` — same recurring-path pattern as ARCH-FU / TEST-FU this morning. New `GetStateFormSchemaQuery` handler mirrors `GetIntakeFormSchemaQueryHandler` precedent 1:1: Pattern-A guard pre-DB, same-schema JOIN (`businessprocess.BusinessProcessStateForms sf JOIN BusinessProcessDefinitions bp ON ... WHERE sf.PublicId AND bp.PublicId AND TenantId`), `IFormTemplateQueryClient.GetVersionSchemaByInternalIdAsync` delegation, SystemAdmin bypass. Route `GET /api/v1/business-processes/{bpId:guid}/state-form-maps/{stateFormMapId:guid}/schema` returns reused `BusinessProcessIntakeFormDto`. FE `form-tabs-container.FormTabPanel` now fetches schema via new `useBpStateFormSchema` hook, renders via `renderInteractiveField` + `buildValidationRules` + `buildRequiredIfRules` + `isFieldVisible` (mirrors `new-bp-run-wizard` intake panel), wires `usePrefillContext` keyed on resolved FormTemplate PublicId, submits via `useSubmitBpStateForm`. 3 new files + 3 modified (~210 LOC). Tests BP.App 82→87 (+5: cross-tenant, map-not-found, snapshot-missing, happy, SysAdmin bypass) + Forms 177/177 + Workflow 431/431 + BP.Domain 25/25 + Arch 97/97 PASS. FE tsc clean. Code-review 9.3/10 APPROVE_WITH_NITS, M-1 (FE envelope duck-typing) folded inline → simplified to `(payload as { data?: T }).data ?? payload` per `useBpReferencesForFormTemplate` precedent. **Carry-overs:** L-1 redundant NSubstitute stub (harmless), L-2 NotFound message uniformity intentional (security), L-3 `cleanFormData` signature audit (pre-existing pattern, out of scope), N-2 DTO rename `BusinessProcessFormSnapshotDto` (defer to next BP touch).
  - **P8-FU2 ✅ SHIPPED 2026-05-17 v2.137p38** (~15min actual): Multi-form transition guard. BE `ExecuteBusinessProcessTransitionCommand` now rejects `transitionFormData` with >1 form binding (returns ValidationError before side effect). Guard in handler after `GetTransitionFormTemplateIdsAsync`, prevents silent FE data drop. 1 file modified + 1 test added. BP.Application.Tests 87→88 PASS. Code-review 9.6/10.
  - **P8-FU2.1 ✅ SHIPPED 2026-05-17 v2.137p39** (~20min actual): FE designer-side gate symmetric to P8-FU2 BE guard. `business-process-form-maps-view.tsx` `handleSave()` rejects duplicate `workflowTransitionId` before mutate; surfaces i18n error toast so admins never persist a config that would 422 at runtime. 3 files modified (TSX + vi/en i18n keys). FE typecheck PASS. Code-review 9.4→9.5+ (EN phrasing nit folded inline). **P8-FU2.1.1 ✅ SHIPPED 2026-05-17 v2.137p40** (~10min actual): UX polish — transition picker disables options already chosen in another row (Map-first-wins owner lookup, current row's own selection stays enabled). Save-time toast retained as safety net. 1 file +16/-1 LOC. FE typecheck PASS. Code-review 9.5/10 auto-approve.
  - **P8-FU3 (Low):** 15s allowed-actions polling not implemented. TanStack defaults work; add `refetchInterval: 15_000` on allowed-actions hook if PRD requires explicit config (vs implicit default).

**Post-MVP:** P9 Workflow version migrate UI (0.5-1d), P10 Workflow cache (1-2d)

**Red-team findings:** 17 R-findings, 3 Critical resolved pre-code (R1 FormSubmission immutable → query-latest; R2 prod empty → DROP; R3 migration infra đã ship). Plan ready for P0 implementation.

**Priority rationale:** Corebase-grade feature — các dự án clone AqtCoreFW sẽ có 5-10 modules nghiệp vụ, cần low-code BP để admin config without module code clone. ~~Gap hiện tại: Cases là "golden-path sample" nhưng chưa có mechanism để bundle Workflow + Forms.~~ (OBSOLETE — Cases module removed 2026-05-01, PR #71)

### T3-API-ENVELOPE-FE-RENDER — ✅ CLOSED 2026-05-27 v2.137p120 (whole-section move; all carry-fwds a+c+d shipped, b resolved p119)

### T3-API-ENVELOPE-FE-RENDER (P4, ✅ CLOSED 2026-05-27 v2.137p120 — all carry-fwds done: a+c+d shipped this slice, b resolved p119): Render structured violation metadata in FE workflow graph save/activate error UX

- **SHIPPED (v2.137p118, 2026-05-26):** Orval regen (auto-generated, `ApiError.metadata` now typed) + 2 hand-written functions (extractViolations parser + formatGraphViolations Vietnamese renderer) + toast integration in `handleSave` catch block + 3 i18n keys (Vi+En: saveFailed, unknownGroupCodes, moreViolations). Wire contract `response.errors[0].metadata.violations[*]` fully consumed; renders as multi-line toast (pre-line wrapper so AntD doesn't collapse newlines, 8s duration). Caps list at 5 violations, surfaces UnknownGroupCodes inline. Test coverage: 6 parser/formatter facts + integration via handleSave path. Vitest 891 pass / 14 pre-existing (0 regression). Code-review 9.1/10 APPROVE-WITH-NITS. Closes both normalizeError consumer + handleSave error-path stories.
- **Wire-shape contract locked:** Camel-case outer (rule/stateName/transitionActionName/detail/metadata), PascalCase inner (UnknownGroupCodes) — no global DictionaryKeyPolicy (be intentional if that changes; integration tests I4/I5 + FE parser both assert the shape).
- **SHIPPED (v2.137p120, 2026-05-27) — a+c+d:** (a) BP-Activate `PublicMetadata` projection (`ActivateBusinessProcessCommandHandler`, inline cast arch-rule-scanned) + FE `handleActivate` multi-line violation toast (mirror handleSave; NOT "zero FE work" as filed — handleActivate had own generic onError). (c) Designer-canvas state-node highlight on save-fail (layered error-glow + corner badge + tooltip + a11y `aria-invalid`; map `violation.stateName`→`state.name` verified; lifecycle clear-on-save-attempt + node-edit + undo). (d) `transitionActionName` in violation bullet (4 presence combos). Plan `plans/260527-1018-api-envelope-fe-render-acd/`. Contract caveat: `WorkflowGraphViolationDto` has NO Metadata field (4 props) → BP violations carry no inner dict (tolerable; R1-R4 don't emit ByGroup codes). Tests: BP.App 95/95 + arch rule 6/6 + FE 19/19 (new+changed); 0 new failures (FE JSDOM ~30 + Workflow CS4014 pre-existing). Review 9.3/10 APPROVE-WITH-NITS (1 Low: hardcoded VI aria-label — matches file's existing "Đầu"/"Cuối" convention, deferred to whole-component i18n).
- **Carry-fwd rows (filed):**
  - **(a) ✅ SHIPPED v2.137p120 (2026-05-27):** BP-Activate `PublicMetadata` wire projection + FE `handleActivate` violation toast. (Filed claim "zero FE work" was drift — handleActivate had own generic onError needing ~10 LOC.)
  - **(b) T3-WF-BUILDER-ARCHITECTURE-PUBLICMETADATA-TYPE-RULE (P3) — RESOLVED 2026-05-26 (v2.137p119):** Source-level arch rule `SourceLevelRulesTests.Rule_PublicMetadata_WithMetadata_Value_MustCast_To_IReadOnlyDictionary` + anti-tautology `[Theory]` (5 rows) added to `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/SourceLevelRulesTests.cs`. Scans all `src/**/*.cs` for `WithMetadata("PublicMetadata", …)` call-sites, asserts inline `(IReadOnlyDictionary<string, object?>)` cast (invariant TValue — wrong/missing cast → silent null on wire); enforced solution-wide (no allowlist gate). SharedKernel.Tests 199→205. Review APPROVE (all findings Low/Info). **Audit-at-slice-start verdict (260522-A):** row premise had drift — "V11" = unit test `WorkflowGraphValidatorTests.cs:434` (not an arch rule) covering the ONE existing call-site `WorkflowErrors.cs:156` (already correct); "BP.Domain path unchecked" = path does not exist (BP-Activate `ActivateBusinessProcessCommandHandler.cs:78` is a comment, wire-surfacing deferred to row (a), zero consumers). Rule is therefore a forward-looking guard for the (a) BP-Activate parity path, not a fix for an existing gap. Test-only, 0 production code.
  - **(c) ✅ SHIPPED v2.137p120 (2026-05-27):** Designer-canvas per-state error highlight (ui-ux-designer treatment: layered error-glow + corner badge + hover tooltip + a11y, not color-alone). Lifecycle clear-on-save-attempt + node-edit + undo. NOTE: BP-detail readonly canvas (`bp-workflow-flow-readonly.tsx`) gets NO highlight (out-of-scope follow-up if BP activate-fail UX wants it).
  - **(d) ✅ SHIPPED v2.137p120 (2026-05-27):** `transitionActionName` rendered in violation bullet across 4 presence combos (state+transition → arrow form; transition-only no leading arrow).
- **Linked:** T3-API-ENVELOPE-STRUCTURED-VIOLATIONS (BE v2.137p117, locked by WorkflowByGroupProducerAndSaveValidationTests I4/I5) + Workflow Phase 02 ByGroup (contract source).

---

## 2026-05-26

### T3-API-ENVELOPE-STRUCTURED-VIOLATIONS — BE error-envelope structured metadata (default-deny opt-in)

**Status:** ✅ BE DONE 2026-05-26 (v2.137p117) → `origin/main` `f907e0ca` (feat) + `139b809e` (docs/journal). FE rendering spun off to open row `T3-API-ENVELOPE-FE-RENDER`.
**Backlog ID:** T3-API-ENVELOPE-STRUCTURED-VIOLATIONS (P4, filed 2026-05-25)
**Description:** Surface structured domain-error metadata (e.g. ByGroup `UnknownGroupCodes`) through the API error envelope. **Design pivot (advisor):** original row proposed exclude-reserved passthrough; shipped DEFAULT-DENY OPT-IN instead — `ApiError` gains optional `Metadata`; `ApiResponse.Fail` extracts ONLY the reserved `PublicMetadata` key; SharedKernel knows no domain types; error factories opt in. `WorkflowErrors.GraphValidationFailed` projects violations to the wire (`GraphErrors` stays internal/log). Controller change NOT needed (flows via `ToApiResponse`→`Fail`).
**Files:** `ApiError.cs`, `ApiResponse.cs`, `WorkflowErrors.cs`, `ActivateBusinessProcessCommandHandler.cs` (comment fix), `WorkflowGraphValidatorTests.cs` (V11), `WorkflowByGroupProducerAndSaveValidationTests.cs` (I4/I5 re-enabled + wire assertions).
**Tests:** full cross-assembly 1216/1220 (4 skip), 0 fail; Debug 0E/0W. Code-review APPROVE-WITH-CONCERNS 8.5/10.
**Deferred (→ open rows):** FE renderer + orval regen + BP-Activate wire parity + wire-shape casing contract → `T3-API-ENVELOPE-FE-RENDER`. Runtime dual-meta is a separate concern → `RT2-15-PHASE-05-EQ2`.

### RT2-15-PHASE-04-FU-M1 — Anti-tautology hardening: cross-mode test PrincipalType discriminator proof

**Status:** ✅ DONE 2026-05-26 (v2.137p116) → `origin/main` `8318d538` (shipped within RT2-15 Phase 05 slice A).
**Backlog ID:** RT2-15-PHASE-04-FU-M1 (P4, filed 2026-05-26 from FU code-review M1)
**Description:** C1/C2 cross-mode tests used DISTINCT `PrincipalRefId` per arm → couldn't prove `PrincipalType` is load-bearing in the read-side predicate. NEW `CrossMode_SharedRefId_PrincipalTypeGatesIndependentlyOfRefId` (C3) seeds both candidate-index arms with the SAME RefId, differing only by `PrincipalType`; OrgUnit-only caller sees the OrgUnit task only → proves `PrincipalType` gates the predicate, not RefId divergence. Seed helper refactored into 1-arg passthrough + 3-arg overload.
**Files:** `WorkflowTaskCrossModeClaimIntegrationTests.cs` (+C3 + overload).
**Tests:** WF.Integration 63/67 (4 skip), GREEN. Code-review 9.5/10.

### RT2-15-PHASE-04-FU — Integration test E2E coverage for ByOrgUnit producer + cross-mode interaction

**Status:** DONE 2026-05-26 (v2.137p115) → `origin/main` `188fbd35` (test) + `2c08ec0b` (journal)
**Backlog ID:** RT2-15-PHASE-04-FU (P4, test-only follow-up to Phase 04 `68287c52`)
**Description:** Cross-mode candidate-index pool NON-INTERFERENCE coverage for the ByOrgUnit clause mode shipped in Phase 04. NEW `WorkflowTaskCrossModeClaimIntegrationTests.cs` (2 facts): ONE WorkflowInstance + TWO pool tasks (one OrgUnit-arm candidate index, one Group-arm), ONE inbox call asserting exact set membership → proves the read-side merge predicate (`WorkflowTaskRepository.IsPoolEligibleAsync`/`GetInboxAsync`) keeps the OrgUnit and Group OR-arms disjoint (OrgUnit-only caller sees/claims OU task only, Group task invisible + claim 403; both-arms caller sees/claims both). Stub-mirror of single-mode siblings; real temporal Dapper resolution delegated to `OrgUnitModuleClientTemporalTests` (OU-I1..I5).

**Bundled fix (RED-on-main):** test `I7` in `WorkflowGraphPublishValidationTests.cs` was RED on main since Phase 04 `68287c52` emptied `_notSupportedModes` (ByOrgUnit became supported → activate returns 204, not 422). Phase 04 tester ran a filtered subset and missed the full-assembly failure. Renamed `I7_Activate_WithNotSupportedClauseMode_Returns422...` → `I7_Activate_WithByOrgUnitClause_Returns204_AndDefinitionActivated`, flipped 422→204 + IsActive true — now locks the positive ByOrgUnit activate path (had zero unit-level coverage).

**Files Changed:**
- **NEW** `tests/integration/AqtCoreFW.Workflow.Integration.Tests/Tasks/WorkflowTaskCrossModeClaimIntegrationTests.cs` (2 facts, stub-mirror cross-mode non-interference).
- `tests/integration/AqtCoreFW.Workflow.Integration.Tests/WorkflowGraphPublishValidationTests.cs` — I7 rename + flip (422→204) + 3 stale comments corrected + collapsed duplicate XML-doc summary (review L1).
- `backlogs/backlog.md` + `docs/project-changelog.md` (v2.137p115 entry).

**Test results:** WF.Integration 62/66 (4 skip) / WF.Application 577/577 / Build 0E/0W. Code-review APPROVE-WITH-NITS 9.1/10 (0 Crit/H/M).

**Audit & lessons (per INVARIANT 260522-A slice-start audit):**
1. Row-claim drift: spec said "mirrors Phase 03-FU pattern" — no Phase 03-FU cross-mode test exists; real mirror source was single-mode `WorkflowTaskRoleClaimIntegrationTests`.
2. Row's literal "temporal seed helper" delegated to OU-I1..I5 (avoid duplicate coverage + cross-module DB coupling) — scoped to read-side non-interference.
3. **260525-C reinforcement #2** — filtered tester run ≠ green build; full owning assemblies mandatory on clause-mode slices. Follow-up row `WF-TESTER-FULL-ASSEMBLY-GATE` filed.

**Follow-ups filed (PENDING in backlog):** `RT2-15-PHASE-04-FU-M1` (anti-tautology: PrincipalType discriminator not proven load-bearing) + `WF-TESTER-FULL-ASSEMBLY-GATE` (process).

**Journal:** `docs/journals/journal-260526-1245-rt2-15-phase-04-fu-crossmode-and-i7-stalefix.md`.

---

## 2026-05-18

### BP-WORKFLOW-CANVAS-REVEAL-SPIKE — React-flow v12 ResizeObserver blank-canvas reveal defect resolution via shared guard hook

**Status:** DONE 2026-05-18 (v2.137p66) → `origin/main` `[final commit TBD]`
**Backlog ID:** OPEN SPIKE (v2.137p63-p65, resolved v2.137p66)
**Description:** react-flow v12 read-only canvas node-reveal blank in antd dynamic-container mounts (BP edit form-maps view + Workflow designer). Per-node ResizeObserver callback never fires initial measurements → `nodesInitialized` flag never flips → `fitView` queued but queue never drains → nodes stay `visibility:hidden`. Root invariant isolated after 4 architectural approaches exhausted.

**Solution:** Bounded self-terminating rAF poll loop (500ms budget, ~31 iterations max) calling forced store `updateNodeInternals(Map, {triggerFitView:true})` until `nodesInitialized` flips. Extracted as reusable FE shared hook to prevent regression across both surfaces.

**Files Changed:**
- **NEW** `web/src/features/workflow/components/use-reactflow-reveal-guard.ts` (65 LOC) — custom hook `useReactFlowRevealGuard(nodes, options?: {refitOnRearm?: boolean})`. Option `refitOnRearm` (default `true`) triggers viewport auto-fit; designer sets `false` to preserve user zoom/pan (M1 regression mitigation).
- `web/src/features/business-process/bp-workflow-flow-readonly.tsx` — replaced inert `useUpdateNodeInternals` effect with `useReactFlowRevealGuard(nodes)` call.
- `web/src/features/workflow/components/react-flow-canvas.tsx` — added `useReactFlowRevealGuard(nodes, {refitOnRearm:false})` to designer.
- `web/src/test/features/workflow/react-flow-canvas-persist-and-connect.test.tsx` — added `useStoreApi` mock to @xyflow/react mock + 2 canvas-reveal regression test cases.

**Test results:** FE typecheck clean, eslint 0 errors, affected suite 9/9 PASS.

**Lessons Applied:**
1. Fixing a bug-class must audit ALL sites sharing the pattern, not just the reported one (missed designer surface initially).
2. Verify load-bearing premises with cheap evidence BEFORE building (store-inspect should precede, not follow, 3+ attempts).
3. Surface user-choice-vs-evidence conflicts hard-gated, don't silently pivot (re-architecture premise was user-sanctioned but evidence-disproven).
4. Timing-luck fixes that pass verification are debt; bounded-workaround better than hidden state dependency.

**Journals:** `docs/journals/bp-edit-debug-cascade-session-wrap-260518.md` (session summary covering p63→p66).

---

## 2026-05-13

### AUDIT-HMAC-VAULT-SEEDER-SWEEP — Vault-backed audit HMAC chain key + startup seeder defense sweep

**Status:** DONE 2026-05-13 (P1–P6 complete) → `origin/main` `01377c9d`
**Backlog ID:** AUDIT-HMAC-VAULT-SEEDER-SWEEP (P1, plan `plans/260513-1805-audit-hmac-vault-seeder-sweep/`)
**Commits:**
- P1 `07b5487a` — `feat(audit)` `IAuditHmacKeyResolver` + `VaultAuditHmacKeyResolver` (`aqt/audit/hmac-v1`, base64 32B, 5-min sliding cache, warn-once legacy fallback); `HmacChainService` ctor swap; Api host DI only (AuthServer = Hangfire-client-only).
- P2 `84f89167` — `chore(audit)` `vault-init.sh` seeds new path; `deployment-guide.md` §5 + `vault-key-rotation-runbook.md` updated.
- P3a `7736b12f` — `docs(audit)` Hangfire backlog replay procedure (Plan-A/B/C; Plan-C purge fallback documented).
- P4 `2d91bb9e` — `feat(seeders)` per-unit try/catch on 4 startup loop-seeders (`EmailHashBackfillService`, `TenantBackfillSeeder`, `BtcIdentitySeeder.Users`, `E2ETestDataSeeder` + earlier in-session `OpenIddictDataSeeder`).
- P5 `1d1c6a52` — `test(audit)` `VaultAuditHmacKeyResolverTests` (6 unit cases) + `SecretConfigBypassGuardTests` (3 arch guards).
- P3b smoke `cce13b1f` + journal `01377c9d` — `docs(audit)` ops-only live smoke verification (Job 91 Failed→Succeeded, 497 backlog drain, 8 pre-P1 residuals accepted under Plan-C).
- P6 finalize (this entry) — `docs(audit)` v2.137p14 changelog + dependency-map + code-standards resolver rule + backlog/completed close-out.

**Test results across the sweep:** Build 0E/0W, Architecture 94→**97/97** (+3 new guards), `VaultAuditHmacKeyResolverTests` 6/6, `SecretConfigBypassGuardTests` 3/3, `AqtCoreFW.Infrastructure.Extensions.Tests` 131/1-skip. Identity.Integration unchanged.

**Carry-over follow-ups (filed in backlog.md, all P3+):**
- P3b-FU1 — wire `VAULT_TOKEN=dev-only-token` into `aqt-runapp` / dev launch profile / `docker-compose.override.yml`.
- P3b-FU2 — dev Vault container loses seed on restart; init sidecar or one-line re-seed snippet in deployment-guide §5.
- P4-FU — extend per-unit try/catch to 10+ additional loop-bearing seeders (BtcOrgUnit L2-L4, BtcAssignment, WorkflowTaxonomy, NotificationTemplate, SystemParam — not Vault-pepper consumers, lower priority).
- P5-FU — `HmacChainServiceVaultIntegrationTests` + `OpenIddictDataSeederIsolationTests` + `EmailHashBackfillServiceTests` isolation case (integration infra heavy; arch guards already prevent worst regressions per advisor YAGNI). Also L-1 (`BackfillResult.Skipped` field is currently write-only) and L-2 (cosmetic helper-vs-summary log ordering) reviewer nits.
- FU-HMAC-ROTATION — full rotation playbook + `HmacKeyVersion` per-entry column (deferred per advisor; rotating chain key without per-entry version tagging silently breaks `VerifyAsync` on historical entries).
- FU-AUDIT-HMACKEY-LEGACY-REMOVE — drop the `Audit:HmacKey` legacy config fallback in `VaultAuditHmacKeyResolver.TryLegacyConfigFallback` one release after this.
- FU-AUDIT-REPLAY-OPS — audit-log who/when/count for Hangfire HMAC replay invocations (security/ops trail).

**Journals:** `journal-260513-1825-dev-stack-ew-audit-hmac-plan.md`, `journal-260513-2125-audit-hmac-vault-seeder-p1-p2-p3a-shipped.md`, `journal-260513-2204-audit-hmac-vault-seeder-p4-p5-shipped.md`, `journal-260513-2235-audit-hmac-vault-seeder-p3b-smoke.md`.

---

### RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS — `--migrate-only` / `SKIP_STARTUP_MIGRATIONS` startup gate

**Status:** DONE 2026-05-13 → PR #105 squash → `main` `5e6ae022`
**Backlog ID:** RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS (P3, FU1 of PHASE6-RLS-PROD-PRINCIPAL-VERIFY)
**Reports:** `plans/reports/code-reviewer-260513-0931-rls-principal-skip-startup-migrations.md`, `plans/reports/tester-260513-1045-rls-principal-skip-startup-migrations.md`
**Verdict:** Code-review 7.5/10 REQUEST_CHANGES → addressed (lone High walked back; Lows folded)

**Problem:** Both hosts ran `MigrateAsync()` for every module DbContext on every boot → runtime DB login forced to keep `db_ddladmin` (a `SECURITY POLICY` is only `ALTER`able by a DDL-capable principal) → weakens the RLS least-privilege story (`deployment-guide.md` §4). The Helm `db-migrate-job.yaml` already invoked `dotnet AqtCoreFW.Api.dll --migrate-only` but `Program.cs` ignored the flag → that Job started the full Kestrel host and never exited (latent bug, now fixed).

**Change:**
- New `src/shared/AqtCoreFW.Infrastructure/Configuration/StartupMigrationMode.cs` (sibling of `ProductionSecretValidator`/`RlsPrincipalValidator`) — pure record; resolves the gate from CLI args + `IConfiguration`. `--migrate-only` arg / `RUN_MIGRATIONS_ONLY=true` → run schema migrations then `return` (no Kestrel/Hangfire/recurring jobs/`WarnIfDbOwnerInProduction`). `SKIP_STARTUP_MIGRATIONS=true` → runtime boot skips the `MigrateAsync` block. Default (neither) = migrate-on-boot. `--migrate-only` precedence over `SKIP_STARTUP_MIGRATIONS`. `--migrate-only` token stripped before `WebApplication.CreateBuilder(args)` (bare flag-token breaks `CommandLineConfigurationProvider`). Lenient `ParseBool` (`true|false|1|0`, case/whitespace-tolerant).
- `Program.cs` (Api + AuthServer) — strip arg before `CreateBuilder`; guard the `MigrateAsync` block with `migrationMode.RunMigrations`; `return` after migrations when `ExitAfterMigrations`; AuthServer encryption-upgrader block left **unconditional** (data-plane, `db_datawriter` only — login depends on `NormalizedEmailHash`); Api pool-warming block given its own scope (no captured-`sp` leak). Code comment in AuthServer notes its 2 contexts ⊂ Api migrate set.
- Helm: `db-migrate-job.yaml` +`RUN_MIGRATIONS_ONLY` env + **commented-out** `ConnectionStrings__Default` elevated-login override (`db-migration-connection-string` secret key); `values.yaml` ships `SKIP_STARTUP_MIGRATIONS="false"` (safe default); `values-prod.yaml` ships the `true` opt-in **commented-out** with a 2-step NOTE.
- Docs: `deployment-guide.md` §4 rewritten — ⚠️"not implemented" block removed; trigger table + 2-step steady-state procedure (1. provision elevated migration login → secret key → uncomment Job override; 2. *then* `SKIP_STARTUP_MIGRATIONS=true` + drop `db_ddladmin`) with "don't do step 2 before step 1" warning + non-Helm out-of-band-migration contract. `run-migrations.sh` header note updated.

**Tests:** new `StartupMigrationModeTests` 25/25; `Infrastructure.Extensions.Tests` 131 PASS / 1 pre-existing skip; `Architecture` 94/94; `Identity.Integration` 198/198 (boots `Program.cs` via `WebAppFixture`); build 0E (Api + AuthServer + `AqtCoreFW.Infrastructure` + test project).

**Spawned follow-up (P3):** `RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING` — provision the elevated migration login + add `db-migration-connection-string` secret key + uncomment the Job override + flip the prod `SKIP_STARTUP_MIGRATIONS` flag + drop `db_ddladmin` from the runtime login (completes the "actually drop `db_ddladmin` in prod" trail). `RLS-PRINCIPAL-FAILFAST` (FU2) still open.

---

## 2026-05-12

### P3 Follow-up Bundle — MTS-2, MTS-9-FU1, QD742-AUDIT-403-FU1, PHASE7-FE-CLASSIFY-FU1

**Status:** DONE 2026-05-12 22:48 ICT (uncommitted, branch `main`)
**Bundle scope:** 4 low-risk P3 items from ongoing session sweeps + QD742 audit-403 FU gate.
**Report:** `plans/reports/code-reviewer-260512-2245-p3-followup-bundle.md`
**Verdict:** Code-review APPROVE_WITH_NITS 9.0/10 (all nits folded pre-finalize)

**Items:**

#### 1. MTS-2 — WorkflowTransition Handle Column Length Shrink
- **Change:** `WorkflowTransition.SourceHandle`/`TargetHandle` nvarchar(max) → nvarchar(16).
- **Files:** `WorkflowTransitionConfiguration.cs` (`HasMaxLength(16)` × 2) + migration `20260512153743_AlterWorkflowTransitionHandleColumnLengths` (symmetric Up/Down AlterColumn).
- **Rationale:** Fixed-size anchor codes (≤8 chars); nvarchar(max) bloats row storage.
- **Risk:** SQL Server `ALTER COLUMN` to smaller type errors if oversized values exist — safe here (all values verified ≤8 chars). Pre-ALTER length-probe comment recommended (P3, folded in).
- **Tests:** Architecture 94/94, build 0E.

#### 2. MTS-9-FU1 — Callback Auth Arch Test Namespace Scoping
- **Change:** `CallbackAuthArchTests` narrowed `Contains("Callback")` LINQ filter to namespace `*.Controllers.Signing.*` to prevent future non-webhook `*Callback*Controller` accidentally subject to security guards.
- **Files:** `CallbackAuthArchTests.cs` (extracted shared `GetSigningCallbackControllers()` helper, symmetric `NotBeEmpty` assertions).
- **Impact:** Verified both real webhook controllers (`MysignCallbackAuthController`, `MysignCallbackResultController`) still matched; no regression. Defensive scoping only.
- **Tests:** CallbackAuthArchTests 3/3, Architecture 94/94.

#### 3. QD742-AUDIT-403-FU1 — Immutability Presence Guards
- **Change:** Added 2 mechanical integration facts to `AuditLogImmutabilityTests.cs`:
  - `Trigger_tr_AuditLog_NoMutate_Exists_AndEnabled` — queries `sys.triggers`, asserts `is_disabled=0`.
  - `GlobalDefaultRetentionPolicy_RowExists` — queries audit.AuditRetentionPolicies, asserts global-default seed exists.
- **Rationale:** Guard against future migration-regen loss (Phase-06 migration regen dropped trigger + seed row idempotently). Mechanical smoke test, no runtime change.
- **Files:** `AuditLogImmutabilityTests.cs` (2 new Facts using existing `GetConnectionString()` helper).
- **Tests:** Audit.Integration 30→32/32 PASS.

#### 4. PHASE7-FE-CLASSIFY-FU1 — Documentation Clarification
- **Change:** Clarified in `docs/code-standards.md` that `arch:accept-long T1 <Entity>: <reason>` directives are **documentary-only** (NOT parsed by `SourceCodeScanner`), must NOT convert to `arch:suppress <RuleCode>` form (would introduce dangling rule code).
- **Rationale:** PR-D2 precedent sets `arch:suppress` as parsed form; 22 Phase-07 directives are documentation only. Resolved by-design: doc-only, no code change.
- **Files:** `docs/code-standards.md` (clarification added).
- **Decision:** RESOLVED-BY-DESIGN (no conversion needed).

**Test Results:**
- Architecture 94/94 PASS
- Audit.Integration 32/32 PASS (was 30/32 before FU1)
- Build 0E, no warnings

**Code-review Nits (folded in):**
1. (MTS-2) Add pre-ALTER length-probe comment in migration — accepted, included.
2. (MTS-9-FU1) Optional: add `NotBeEmpty` to POST-only test for symmetry — accepted, included.

---

## 2026-05-10

### QD 742 Phase 04 — §4.4 PII Encryption Waves 1–4+6

**Status:** SHIPPED 2026-05-10
**Commits:** `126462e3` (Wave 1) → `c256e326` (Wave 6 + docs) + `90d44fde` (journal), 6 commits total on main
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/`
**Prereq:** Phase-06 Vault Finish (IKeyResolver, VaultKeyResolver, EncryptedStringConverter v{N}: prefix) — shipped same day.

**Waves shipped:**
- **Wave 1:** `IPepperResolver` abstraction + `VaultPepperResolver` (Vault path `Encryption:EmailPepper-{tenantId:N}`, 5-min cache). TestPepperResolver stub (0xAB×32) in WebAppFixture. 12 unit tests.
- **Wave 2:** `ClassificationRule` entity + `AddClassificationRules` migration + UpdateClassificationRule command + GetClassificationRules/GetClassificationInventory queries + `DataClassificationService` reflection scanner + `DataClassificationController` (3 endpoints) + `AUDIT.CLASSIFICATION.VIEW/MANAGE` PermCodes + `DoNotEncryptAtRestAttribute`.
- **Wave 3:** ApplicationUser 6 PII columns encrypted at rest (`Email`, `PhoneNumber`, `UserName`, `FullName`, `DepartmentCode`, `EmployeeCode`). `EmailHashInterceptor` + `EmailHashedUserStore` + `EmailHashService` (HMAC-SHA-256, Guid.Empty cross-tenant sentinel) + `EmailHashBackfillService` (idempotent, wired Api + AuthServer). `AddEmailHashColumns` migration.
- **Wave 4:** `RtbfRequest.SubjectEmail` + `CitizenNationalId` encrypted at rest, v1: wire format. `AuditDbContext` + `RtbfRequestConfiguration` dual-ctor (design-time / runtime). `PlaintextToEncryptedUpgrader` generic encryptor with `ColumnTarget.PkColumn = "InternalId"` override (RtbfRequests bigint IDENTITY PK). Both hosts wired. `EncryptRtbfPiiColumns` migration.
- **Wave 6:** 5 new Wave-4 arch compliance tests (40/40 PASS). `RtbfRequestEncryptionRoundtripTests` (6 integration tests). `EmailHashBackfillServiceTests` (4 integration tests). TestKeyResolver (0x42×32) + TestPepperResolver stubs in WebAppFixture.

**Test results:** Architecture 40/40 PASS, Identity.Integration 58/58 PASS. Builds Api + AuthServer 0E/0W.
**Deferred:** Wave 5 FE classification UI → QD742-WAVE5-FE (P2). Audit.Integration 10 pre-existing 403 → QD742-AUDIT-403 (P2).

---

### QD 742 Phase 06 — Vault Finish (No Hardcode Credentials)

**Status:** SHIPPED 2026-05-10
**Phase:** 06 of 8 in `plans/260428-1740-cap-4-qd-742-compliance/`
**Scope:** Hoàn thiện Vault integration để production không hardcode credentials. Covers requirement 4.4.a.
**Effort:** 5-6.5d estimated; ~1 session realized (merged S0-S6a cook into finalize gate).
**Deliverables:**
- IKeyResolver + VaultKeyResolver (singleton, 5-min cache, prod-fail-fast)
- VaultHealthCheck + ProductionSecretValidator (both Api + AuthServer hosts per D10.1)
- EncryptedStringConverter v{N}: prefix (dual-decode permanent per D10.2)
- LegacyCipherUpgrader CLR migration (T-SQL cannot do AES-GCM; re-encrypt via EF UpData hook)
- SerilogConfig destructuring filter (mask secret-shaped config values)
- hashicorp/vault dev container in docker-compose.yml (D10.3)
- docs/ops/vault-key-rotation-runbook.md (~250 LOC, 11 sections, 2-person approval, N=3 key retention)
- 9 Phase-06 compliance tests: Architecture 35/35, AuthServer 27/27, Infra 89/89+1 skip, all PASS

**Files Changed:**
- **Production:** Api/Program.cs, AuthServer/Program.cs, EncryptedStringConverter.cs, VaultConfigurationProvider.cs, appsettings.json+.Development.json, SerilogConfiguration.cs, docker-compose.yml (+VaultKeyResolver, +VaultHealthCheck, +ProductionSecretValidator, +IKeyResolver)
- **Docs:** docs/ops/vault-key-rotation-runbook.md (NEW), docs/compliance/qd-742-cap-4/phase-06/README.md (NEW)
- **Tests:** ProductionSecretValidationTests, EncryptedStringConverterVersioningTests, SecretLoggerSanitizationTests (NEW), existing smoke/arch tests updated

**Key Decisions Applied (D10):**
- D10.1: AuthServer host in scope — wired same AddVaultConfiguration + ProductionSecretValidator + Serilog destructure as Api.
- D10.2: One-time migration with permanent dual-decode — existing encrypted columns re-encrypted to `v{N}:` prefix via EF migration UpData hook; read path stays dual-version for safety.
- D10.3: Vault dev container added to docker-compose.yml (hashicorp/vault:latest, root token `dev-only-token`, KV v2 mount).
- D10.4: Config provider order correct; NO reorder (VaultConfigurationProvider last-added = highest priority in IConfiguration chain).
- D10.5: Vault HA + TLS deferred to ops/infra (documented in runbook, not implemented).

**Phase-04 Unblocked:** Phase 06 completion unblocks Phase 04 (Data Classification PII encryption + HMAC pepper) — Vault keys now available for production migration.

**Evidence Dossier:** `docs/compliance/qd-742-cap-4/phase-06/README.md` + child sections (test outputs, runbook, Vault setup guide).

**Pattern Notes:**
- Empirical pre-cook scope lock (D10 amendment) prevented 2-3h of misdirected implementation (config order audit, unnecessary HA topology).
- One-time migration dual-decode approach is stricter than KISS default but cleaner audit trail + safer production transition.

---

## 2026-05-09

### QD742-FU1 — RESOLVED-OBSOLETE (admin sweep already shipped Phase-03)

**Status:** RESOLVED-OBSOLETE 2026-05-09 23:15 ICT (decision-only, no code change)
**Backlog row:** `QD742-FU1 (P2): Wire [RequiresMfa] lên admin endpoints (Phase-03 sweep)` (added 2026-05-01 post Phase-02)
**Decision:** Premise refuted by static + CI-guard verification. Sweep already shipped during v2.133p22 Phase-03 MFA Policy UI ship (2026-05-02). Backlog row drift — author of Phase-02 row didn't update when Phase-03 absorbed it.

**Empirical evidence:**
- 31 controllers carry class-level `[RequiresMfa]`: 9 host Api admin (`Backup`/`Webhooks`/`Jobs`/`EventCatalog`/`DeadLetters`/`ApiKeys`/`Alerting`/`MfaEmergency`/`MfaPolicies`AdminController), 13 Identity Presentation incl non-AdminController-suffix admin (`CredentialPolicies`, `ExternalIdentities`, `JitProviderConfigs`, `AccessReview`, `SodRules`/`AbacRules`/`PolicyRules`AdminController), 3 Audit (`SecurityIncidents`, `Rtbf`, `LoginAudit`), 2 MasterData, 1 Notifications, 2 Organization, 1 SystemParams.
- CI guard `tests/architecture/AqtCoreFW.Architecture.Tests/AdminControllersMfaAttributeTests.cs` PASS 2/2 in 8s — sweeps all `*AdminController` + `*RulesAdminController` across `PresentationAssemblies`; floor=12, allowlist=`AccountMfaController` only (self-service enrolment legitimately exempt).
- Infrastructure (`mfa_at` claim issuance, `RequiresMfaAttribute`, `MfaPolicyProvider`, `MfaRequirementHandler`) wired since Phase-02 v2.133p20 (2026-05-01).

**Out of scope (NOT closed):**
- Method-level `[RequiresMfa]` on user-facing controllers (e.g., `WorkflowTasksController` write actions) — QĐ 742 §1.6.b mandates MFA on privileged/admin ops; non-admin lifecycle out of scope.
- Per-controller stricter `maxAgeHours` (e.g., 1h for emergency MFA disable). Defensive optimization, no current incident.

**Artifacts:** `plans/reports/decision-260509-2315-qd742-fu1-yagni-obsolete.md`
**Time:** ~10min source-verify
**Saved:** ~2-4h speculative cook avoided. **4th YAGNI-obsolete closure this session** (after PHASE7-FE-DTO-CONTRACT, REPORTS-DASHBOARD-ENDPOINT, MFA-EMAIL-OTP-ENDPOINT). ROI ≈ 25-35×. Pattern codified `verify-fe-consumer-before-be-build.md` — now generalized to any "wire X" / "build Y" backlog row: source-verify the consumer/sweep state in <30 min greps before launching cook.

---

### AUTHSERVER-DI-ARCH-FU5 — REJECTED-OBSOLETE (MVC auto-discovery covers premise)

**Status:** RESOLVED-OBSOLETE 2026-05-09 22:45 ICT (decision-only, no code change)
**Backlog row:** AUTHSERVER-DI-ARCH-FU5 (P3, NEW 2026-05-09 21:50, ~30m estimate)
**Decision:** Reject FU5 — premise (static rule asserting every `*.Presentation` project-referenced by host has explicit `AddApplicationPart`) refuted by empirical FU4-test diagnostic.

**Empirical evidence:**
- Added one-line `ITestOutputHelper.WriteLine` diagnostic to existing `Api_AllControllers_AreConstructableViaDi`. Re-ran smoke (~30s warm). Output: **79 controllers enumerated, including all 3 `Integration.Presentation` controllers** (`ContractsController`, `MessageLogsController`, `PartnersController`).
- `Integration.Presentation` referenced in `src/host/AqtCoreFW.Api/AqtCoreFW.Api.csproj:61` but **NOT** in `AddApplicationPart` chain (`Program.cs:110-140` covers 13 of 14 module Presentations; Integration is the missing one).
- ASP.NET Core MVC `DefaultAssemblyPartDiscoveryProvider` walks `DependencyContext.Default.RuntimeLibraries` and adds an `ApplicationPart` for every assembly transitively depending on `Microsoft.AspNetCore.Mvc.Core` → auto-discovery covers the gap.

**Cross-validation:** PHASE7-TIER-D-B4 (commit `b6df1840`) shipped Integration handler fixes verified by 5 originally-failing E2E specs reaching 55/55 PASS. If Integration controllers were not routed, these specs would have 404'd. Auto-discovery hypothesis confirmed by behavioral evidence.

**Outcome:**
- 13 explicit `AddApplicationPart` calls in `Program.cs` are **belt-and-suspenders for trim/single-file/AOT publish** (where `DependencyContext.Default` may be unavailable), not correctness-critical for dev/staging.
- Implementing FU5 would generate immediate false-positive on `Integration.Presentation` working configuration.
- AqtCoreFW is template-only / dev-only (memory `aqtcorefw-template-dev-only.md`); trim/AOT scenario does not apply.
- **AUTHSERVER-DI-ARCH track fully CLOSED:** FU1+FU2+FU3 SHIPPED → ALT REJECTED → FU4 SHIPPED → FU5 REJECTED-OBSOLETE. No open rows remain.

**Artifacts:** `plans/reports/decision-260509-2240-authserver-di-arch-fu5-rejected.md`
**Time:** ~25min (scout + diagnostic + advisor consult + report)
**Saved:** ~30min implementation + ~15min false-positive triage = ~45min wasted work avoided.

**Lesson:** Same pattern as ALT (REJECTED 21:50 ICT same day) — backlog rows posing "could X catch Y?" require empirical premise check before commit. Five-minute diagnostic via existing test (`ITestOutputHelper.WriteLine` + warm smoke run) is cheaper than full implementation cycle. **3 closures in <24h on AUTHSERVER-DI-ARCH track:** ALT (21:50), FU4 (22:30 — the *correct* path), FU5 (22:45). Pattern codified in memory `verify-fe-consumer-before-be-build.md` (now generalizable beyond FE).

---

### AUTHSERVER-DI-ARCH-FU4 — Module Controller DI Coverage via ApplicationPartManager

**Status:** RESOLVED 2026-05-09 22:30 ICT (uncommitted; code-review APPROVE 9.4/10 → 9.6 post P3-1 doc sync)
**Backlog row:** AUTHSERVER-DI-ARCH-FU4 (P3, ~1-2h estimate; UNBLOCKED 21:50 ICT by ALT decision report)
**Decision:** Replaced `HostDiArchGuard.EnumeratePublicControllers(Assembly)` (host-assembly-only) with `EnumerateAllControllers(IServiceProvider)` (canonical ASP.NET Core `ApplicationPartManager` + `ControllerFeatureProvider`). Module Presentation assemblies (Identity/Forms/Workflow/Notifications/Audit/MasterData/SystemParams/Organization/Ai/Files/Reporting/Rules/Collaboration — 14 wired via `AddApplicationPart` in Api host) now directly DI-validated, not only transitively via host-local deps.

**Implementation:**
- `tests/integration/AqtCoreFW.Startup.SmokeTests/HostDiArchGuard.cs`: deleted `EnumeratePublicControllers(Assembly)`; added `EnumerateAllControllers(IServiceProvider)` (uses `partManager.PopulateFeature(new ControllerFeature())` then `Cast<Type>().ToList()`); refactored `TryResolveControllers` signature `(IServiceProvider, Assembly)` → `(IServiceProvider, IReadOnlyList<Type>)` (resolve-only SRP). Dropped unused `using System.Reflection` + `Microsoft.AspNetCore.Mvc`.
- `tests/integration/AqtCoreFW.Startup.SmokeTests/ApiControllerDiResolutionSmokeTests.cs`: switched call site to new method.
- `tests/integration/AqtCoreFW.Startup.SmokeTests/AuthServerStartupValidationTests.cs`: same. AuthServer has no `AddApplicationPart` calls (host-local only) so set is equivalent there; Api now covers ~71+ module controllers vs ~5 prior.
- `docs/code-standards.md:441-477`: synced HostDiArchGuard fixture-usage snippet (P3-1 from code-review).

**Test Results:** Smoke 4/4 PASS (58s warm; 2 controller resolution + 2 auxiliary). Build 0E/0W.

**Code-review Verdict:** APPROVE 9.4/10 (0 P0/P1/P2, 2 P3: doc snippet drift addressed inline + dated-cite monitoring noted). Verified `feature.Controllers.Cast<Type>()` safety (`TypeInfo : Type`), post-Build sequencing, coverage delta (≥ prior set).

**Scope Notes:** Single-change focused; FU5 (wiring guard for "module added to .csproj but forgot AddApplicationPart") explicitly carved out as separate row per ALT decision report.

**Files Touched:**
- Modified test (3): `HostDiArchGuard.cs`, `ApiControllerDiResolutionSmokeTests.cs`, `AuthServerStartupValidationTests.cs`
- Modified docs (1): `docs/code-standards.md`
- Net LOC: ~+15 / -25 = ~-10 (helper got simpler)

**Time:** ~45min (scout + impl + 2 test runs + doc sync + code-review)

---

### AUTHSERVER-DI-ARCH-ALT — Decision: REJECTED (static Architecture rule cannot replace fixture-based FU4)

**Status:** RESOLVED-REJECTED 2026-05-09 21:50 ICT (decision-only, no code change)
**Backlog row:** AUTHSERVER-DI-ARCH-ALT (P3, NEW 2026-05-09)
**Decision:** Reject ALT path. Static IL/source analysis cannot validate DI graph resolution — this is a category error. The two motivating incidents (IMessageBus v2.133p3, IGuidGenerator 2026-05-08) are missing service registrations that only surface when `ServiceProvider` actually resolves. Implementing static DI validation would require reimplementing the .NET DI container (parse every `Add*<I,Impl>()` callsite + replicate framework registrations like `AddIdentity()`'s 50+ services + handle conditional registration). Out of scope.

**Premise refutation:**
- "Wider coverage" — refuted: FU4 walking `MvcOptions.ApplicationPartManager.ApplicationParts` collects identical controller set (~71 module controllers across 14 Presentation assemblies + ~5 host-local).
- "Faster (~seconds vs ~30-40s)" — refuted at the granularity that matters: cold-start fixture cost (~30-40s = Testcontainer SQL boot + EF migrations + DI graph build) is already paid by the existing `Api_AllControllers_AreConstructableViaDi` test. ALT would save only the controller loop (~71 × ~10ms `ActivatorUtilities.CreateInstance` ≈ 700ms) while losing the actual DI-graph guarantee.
- Project hand-rolls Architecture suite (regex source scanner + raw `System.Reflection`); no ArchUnitNET / NetArchTest / NDepend (verified 0 references).

**Outcome:**
- AUTHSERVER-DI-ARCH-FU4 (P3, ~1-2h): UNBLOCKED — proceed with `HostDiArchGuard.EnumeratePublicControllers` extension to walk `ApplicationPartManager.ApplicationParts`.
- AUTHSERVER-DI-ARCH-FU5 (P3, NEW, ~30m): spawned — supplementary lightweight wiring guard ("module added to host .csproj but forgot `AddApplicationPart` callsite"); neither ALT nor FU4 catches this.

**Artifacts:** `plans/reports/decision-260509-2150-authserver-di-arch-alt-rejected.md` (advisor-verified)
**Time:** ~30min (scout + advisor consult + report)
**Lesson:** Backlog rows posing "could X replace Y?" require empirical premise check before commit. ALT premise looked plausible on text but failed first-principles DI-container test. Saved estimated 2-3h of wasted FU implementation.

---

### AUTHSERVER-DI-ARCH-FU1+FU2+FU3 BUNDLE — Program Namespace Wrapping + Fixture Relocation + Helper Extraction

**Status:** RESOLVED 2026-05-09 22:10 ICT (uncommitted, code-review APPROVE 9.0/10)
**Backlog rows:** AUTHSERVER-DI-ARCH-FU1, AUTHSERVER-DI-ARCH-FU2, AUTHSERVER-DI-ARCH-FU3 (P3 cleanups bundled)
**Decision:** Path A (namespace Program classes) chosen by user vs Path B (extern alias) — larger production-code blast radius but long-term cleaner explicit FQN.
**Time:** ~6h total (scout + plan + implement Phases 1-6 + code-review + finalize)

**Implementation Summary (Path A):**
1. **Phase 1-2 (FU1 Api part):** Wrapped `src/host/AqtCoreFW.Api/Program.cs` top-level statements into `namespace AqtCoreFW.Api; public partial class Program { static async Task Main(string[] args) {...} }`. Updated 2 fixtures: `WebAppFixture.cs:25` + `SeederSmokeFixture.cs:42` → `WebApplicationFactory<AqtCoreFW.Api.Program>`. Build + Smoke tests GATE GREEN.
2. **Phase 2 (FU1 AuthServer part):** Wrapped `src/host/AqtCoreFW.AuthServer/Program.cs` into `namespace AqtCoreFW.AuthServer` analogously. Updated `AuthServerHostFixture.cs:30` + `AuthServerStartupValidationTests.cs:46` with FQN. Build + AuthServer.Tests 29/29 GATE GREEN.
3. **Phase 3 (FU2 Relocation):** Added `<ProjectReference>` to SmokeTests csproj for AuthServer host. Moved `AuthServerHostFixture.cs` + `AuthServerStartupValidationTests.cs` (2 files) from `tests/unit/AqtCoreFW.AuthServer.Tests/Startup/` → `tests/integration/AqtCoreFW.Startup.SmokeTests/`. Deleted empty `Startup/` dir. Updated namespaces `AqtCoreFW.AuthServer.Tests.Startup` → `AqtCoreFW.Startup.SmokeTests`.
4. **Phase 4 (FU3 Helper Extract):** Created `tests/integration/AqtCoreFW.Startup.SmokeTests/HostDiArchGuard.cs` static helper (~30 LOC): `EnumeratePublicControllers(Assembly)` + `AssertControllersResolve(IServiceProvider, Assembly)` methods. Refactored `AuthServerStartupValidationTests` to use helper.
5. **Phase 5 (FU1 Api controller test):** Renamed `ApiStartupSeederSmokeTest.cs` → `ApiStartupSmokeTests.cs`. Added `[Fact, Trait("Category","Smoke")] Api_AllControllers_AreConstructableViaDi` using `HostDiArchGuard`.

**Test Results:**
- Smoke tests: 3/3 PASS (1 seeder + 1 Api controllers + 1 AuthServer controllers)
- AuthServer.Tests: 27/27 PASS (was 29/29; 2 relocated facts now in SmokeTests + expect count down to 27)
- Architecture tests: 33/33 PASS (no regressions)
- Build: 0E/0W clean

**Negative Validation (confirms guard works):**
Temporarily removed `AddSharedKernelGuidGenerator()` registration → `Api_AllControllers_AreConstructableViaDi` FAILED with clear error identifying affected controllers (AuthorizationController, ExternalLoginController, MfaChallengeController) + root cause (IGuidGenerator unresolved). Restored and re-validated GREEN.

**Files Touched:**
- **Modified production:** `src/host/AqtCoreFW.Api/Program.cs`, `src/host/AqtCoreFW.AuthServer/Program.cs` (2 files)
- **Deleted production:** none
- **Added test:** `tests/integration/AqtCoreFW.Startup.SmokeTests/HostDiArchGuard.cs` (new helper)
- **Modified test:**
  - `tests/shared/AqtCoreFW.Tests.SharedFixtures/WebAppFixture.cs` (fixture reference)
  - `tests/integration/AqtCoreFW.Startup.SmokeTests/SeederSmokeFixture.cs` (fixture reference)
  - `tests/integration/AqtCoreFW.Startup.SmokeTests/ApiStartupSmokeTests.cs` (renamed + added controller test)
  - `tests/integration/AqtCoreFW.Startup.SmokeTests/AuthServerHostFixture.cs` (relocated from unit)
  - `tests/integration/AqtCoreFW.Startup.SmokeTests/AuthServerStartupValidationTests.cs` (relocated from unit)
  - `tests/unit/AqtCoreFW.AuthServer.Tests/` (Startup/ directory removed)
  - `tests/integration/AqtCoreFW.Startup.SmokeTests/AqtCoreFW.Startup.SmokeTests.csproj` (added AuthServer ProjectReference)

**Code-review Verdict:** APPROVE 9.0/10 (0 P0/P1, 1 P2 M1: module controller coverage documented as FU4 (see backlog FU4 + FU-ALT rows), 2 L-tier (naming clarification, minimal cosmetic)). Negative validation confirmed guard effectiveness.

**Scope Notes:**
- Bundle was deliberate trade: 4-6h implementation (larger blast radius via Path A) vs ~1h Path B (less production cleanup). User selected Path A for long-term code clarity. Anti-creep flag logged in plan.
- HostDiArchGuard extraction (FU3) done in SmokeTests project itself, not in SharedFixtures, because moving to SharedFixtures would create circular dependency (SharedFixtures → Api ProjectReference → circular). Helper remains in SmokeTests (2 consumers: Api + AuthServer); trigger "move to SharedFixtures" when 3rd consumer (Gateway host) arrives, per FU3 spec.

**Follow-up backlog (generated during code-review):**
- **AUTHSERVER-DI-ARCH-FU4 (P3, ~1-2h):** Module controller coverage — extend `HostDiArchGuard.EnumeratePublicControllers` to walk `MvcOptions.ApplicationPartManager.ApplicationParts` for Identity/Forms/Workflow presentation assemblies. Current helper only enumerates host-local controllers.
- **AUTHSERVER-DI-ARCH-ALT (P3):** Alternative: Architecture suite static rule covering ALL controllers (host + module) instead of fixture-based cold-start. Faster + wider (seconds vs 30-40s per run). Decide approach before FU4 implementation.

---

### PHASE7-TIER-D-B4-PAGINATION-ENVELOPE — Misnamed bucket; 3 BE bugs + 2 test drifts fixed; 55/55 PASS

**Status:** RESOLVED 20:05 ICT
**Backlog row:** Was P2 ~1-2h "envelope drift" → reclassified as 3 BE bugs + 2 test drifts after empirical drill.
**Branch:** `main` (uncommitted at write time)
**Time:** ~2h (scout-static → empirical break → 5 fixes → 4 rebuild cycles → verify)

**Empirical break (advisor recommendation):** Bucket name "Pagination envelope drift" was a heuristic guess by prior bucket-classifier. Static scout claimed all 5 specs aligned. Conflict broken by booting stack + running 1 spec → captured actual error: **server returns 500**, not envelope drift. Curl probes against 5 endpoints with admin token revealed 3 distinct BE bugs.

**BE bugs fixed:**
1. **Integration DTOs DateTimeOffset/DateTime mismatch** (`IntegrationDtos.cs`):
   - PartnerDto/ContractDto/MessageLogDto declared `DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt`.
   - Entity base = `DateTime` + DB column = `datetime2(3)` → SQL returns DateTime.
   - Dapper positional-record materialization failed: "A parameterless default constructor or one matching signature... is required" — ctor type signature didn't match.
   - Fix: change DTO to `DateTime CreatedAt, DateTime? UpdatedAt` (mirrors PR #90 SecurityIncidentDto pattern).
2. **RTBF status SQL drift** (`GetRtbfRequestsQueryHandler.cs`):
   - `Status` column converted to NVARCHAR via `HasConversion<string>()` on entity (`RtbfRequestConfiguration.cs:26`).
   - Handler passed `(int)request.Status.Value` AND projected `CASE Status WHEN 0 THEN 'Pending' END` — both treat column as int.
   - SQL error: "Conversion failed when converting the nvarchar value 'Rejected' to data type int".
   - Fix: `p.Add("Status", request.Status.Value.ToString())` + `SELECT Status` (no CASE).
3. **MessageLog NULL projection type** (`ListMessageLogsQueryHandler.cs`, `GetMessageLogQueryHandler.cs`, `UpdateMessageLogStatusCommandHandler.cs`):
   - `NULL AS UpdatedAt` → Dapper infers INT type → "System.Int32 UpdatedAt" in error message vs DTO `DateTime?`.
   - Fix: `CAST(NULL AS datetime2) AS UpdatedAt` (3 handlers).

**Test contract drifts fixed:**
4. `integration-contracts.spec.ts:69` `partnerId` → `partnerPublicId` (T2 retier per Phase-07; BE binding uses `PartnerPublicId`).
5. `integration-message-logs.spec.ts:67` same fix.
6. `workflow-definitions.spec.ts:166` DELETE assertion `[200,204]` → `[200,204,400]` (BE rejects delete on Active per state guard).

**Files changed:**
- `src/modules/integration/AqtCoreFW.Integration.Application/DTOs/IntegrationDtos.cs`
- `src/modules/integration/AqtCoreFW.Integration.Application/Queries/ListMessageLogs/ListMessageLogsQueryHandler.cs`
- `src/modules/integration/AqtCoreFW.Integration.Application/Queries/GetMessageLog/GetMessageLogQueryHandler.cs`
- `src/modules/integration/AqtCoreFW.Integration.Application/Commands/UpdateMessageLogStatus/UpdateMessageLogStatusCommandHandler.cs`
- `src/modules/audit/AqtCoreFW.Audit.Application/Queries/GetRtbfRequests/GetRtbfRequestsQueryHandler.cs`
- `web/e2e/integration-contracts.spec.ts`
- `web/e2e/integration-message-logs.spec.ts`
- `web/e2e/workflow-definitions.spec.ts`

**Empirical verification:**
- Curl probes against 5 endpoints — all 200 OK with proper envelope.
- Single-spec run: 55/55 PASS across the 5 originally-failing specs.

**Cascade impact:**
- Likely unblocks unknown-count of dependent specs that depended on these endpoints (contract drift had 12 specs cascade in beforeAll).
- Resolves **memory feedback `feedback-dapper-no-positional-records.md` 4th occurrence** (Integration DTOs joined SecurityIncidentDto/RtbfRequestDto in needing class-or-typed-record migration).

**Pattern note (memory candidate):** Bucket-classifier scouts that label by spec-name/topic without empirical capture are unreliable. Verify by 1-spec runtime before committing to a fix path. Advisor's "Stop reading files; capture runtime" rule held — saved hours of misdirected static analysis.

**Reopen criteria:** B5 (public-form timeout) still failing after 30s extension — likely a separate FE rendering issue. B6 (workflow-definitions) DELETE handled; remaining 1 cascade in Tier-D residual (acceptable).

**Unresolved:**
- Public-form-{lifecycle,validation,file-upload} timeouts persist past 30s. Needs separate investigation — likely FE rendering bug, not E2E config.
- Should `feedback-dapper-no-positional-records.md` memory be promoted to Architecture rule (DTO-must-be-class)? 4 incidents + Integration module covers 3 DTOs at once. Consider adding to backlog as PHASE7-DAPPER-DTO-ARCH-RULE (P3).

---

### PHASE7-TIER-D-RESIDUAL-CONTRACT — Bucket-Classified + 2 High-Leverage Buckets Drilled

- **Status:** RESOLVED-PARTIAL 2026-05-09 19:30 ICT (parent row closed; 4 child rows split)
- **Scope:** Parent backlog row premised "~60 of 72 parallel residuals". Verifier 17:30 ICT (post-token-revoke fix) baseline = **30 residuals**. Bucket-classify across 6 buckets + drill 2 test-only buckets in this cook.
- **Scout report:** `plans/reports/scout-260509-1917-tier-d-residual-buckets.md` (215 lines). Sampled 6 representative specs across 6 buckets with primary-source evidence (file:line + actual assertion + root cause).
- **Buckets:**
  - **B1** Test asserts wrong, BE correct (×2 specs, account-change-password) — split as PHASE7-TIER-D-B1-CHGPWD-ASSERT P3
  - **B2** DTO id type drift int vs Guid (×1 spec, admin-alerting-rules) — split as PHASE7-TIER-D-B2-ALERTING-ID-TYPE P3
  - **B3** Response shape drift bare id vs object (×2 specs, admin-announcements) — split as PHASE7-TIER-D-B3-ANNOUNCE-SHAPE P3
  - **B4** Pagination envelope drift (×5+ specs, integration/audit) — split as PHASE7-TIER-D-B4-PAGINATION-ENVELOPE P2 (highest leverage; cross-module sweep)
  - **B5** FE form rendering timeout 15s insufficient (×3 specs, public-form-*) — **DRILLED in this cook** ✓
  - **B6** workflow-definitions cascade (×1 root + ~9 dependents, missing `entityType` + state names) — **DRILLED in this cook** ✓
- **Files changed (B5 + B6 drill):**
  - `web/e2e/workflow-definitions.spec.ts` — POST `/workflow/definitions` body added `entityType: 'Generic'`. PUT `/graph` states + transitions: renamed `Review` → `InReview` (4 references) to match `GenericEntityStatusEnumContributor.GetStateNames()` allowed set `[Draft, Submitted, InReview, Approved, Rejected, Cancelled]`. Validator `CreateWorkflowDefinitionCommandValidator.cs:25-32` requires `EntityType` non-empty + registered; `SaveDefinitionGraphCommandValidator.cs:54-110` validates state names against entity's allowed set (case-sensitive `StringComparer.Ordinal`).
  - `web/e2e/public-form-lifecycle.spec.ts` — `timeout: 15_000` → `30_000` (2 occurrences: `toBeVisible` + `waitFor`).
  - `web/e2e/public-form-validation.spec.ts` — `timeout: 15_000` → `30_000` (1 occurrence).
  - `web/e2e/public-form-file-upload.spec.ts` — `timeout: 15_000` → `30_000` (1 occurrence).
  - `backlogs/backlog.md` — parent row marked RESOLVED-PARTIAL + 4 child rows added (B1/B2/B3/B4).
  - `backlogs/completed.md` — this entry.
- **Test state:** Test-only changes; no BE source touched. Suite re-run deferred to next session (current scope was investigation + classification + 2 mechanical fixes).
- **Code-review verdict:** Test-side only, sub-trivial mechanical fixes (constant bumps + JSON body field).
- **Cascade impact:** B6 fix unblocks `workflow-{configs,delegation,dynamic-instances}` dependents (per debugger 14:50 report ~9 specs cascade-skipped on workflow-definitions failure).
- **Reopen criteria:** Run full Tier D parallel; if residuals > 25, drill child rows (B4 first — highest leverage).
- **Pattern note:** This is investigative/classification work + mechanical drill, not BE feature build. YAGNI memory rule (`verify-fe-consumer-before-be-build.md`) doesn't apply — root cause already verified by debugger 14:50 ICT report. Approach mirrors debugger's B1/B2/B3 split pattern from same date (root-cause analysis → split parent into bucket rows).

---

### MFA-EMAIL-OTP-ENDPOINT — Closed as YAGNI Obsolete (No FE Consumer)

- **Status:** RESOLVED-OBSOLETE 2026-05-09 18:55 ICT
- **Scope:** Backlog premise verified against source — no FE consumer exists for email-OTP send. Cleanup-only (delete 2 speculative skipped tests).
- **Source verify (5 min greps):**
  - Grep `web/src` for `mfa/send-otp|sendOtp|email-otp|emailOtp` → only MfaPolicy admin enum-flag refs (`MfaFactor.EmailOtp = 2`, used as **allowed-factor configuration value**, not a send call).
  - FE `web/src/features/account/mfa-enrolment-page.tsx` → only TOTP routes (`/account/mfa/enrol/totp`, `/status`, `/totp`, `/recovery-codes/regenerate`).
  - `src/host/AqtCoreFW.Api/Controllers/Account/AccountMfaController.cs` → 4 routes total (status GET / enrol/totp GET+POST / recovery-codes/regenerate POST). No email-otp endpoint.
  - `src/host/AqtCoreFW.AuthServer/Pages/Account/` → 0 hits for email-otp/send-otp.
  - TC-IDENT-055 + TC-IDENT-056 in `web/e2e/mfa-flow.spec.ts` were speculative placeholders for an unimplemented endpoint.
- **Decision:** Cleanup over build. Email-OTP factor is forward-compat enum value (admins can toggle in MfaPolicy), but no send/verify pipeline wired through controller. Building endpoint without FE consumer = YAGNI violation.
- **Files changed:**
  - `web/e2e/mfa-flow.spec.ts` — deleted TC-IDENT-055 (lines 65-66) + TC-IDENT-056 (lines 32-33). Header comment updated to note removal date + closure context.
  - `backlogs/backlog.md` — MFA-EMAIL-OTP-ENDPOINT row → RESOLVED-OBSOLETE with refute summary.
  - `backlogs/completed.md` — this entry.
- **Test state:** No test changes (only deletions of 2 `test.skip` no-op stubs).
- **Code-review verdict:** N/A (test-deletion + docs cleanup, sub-trivial).
- **Reopen criteria:** Concrete business case for email-OTP fallback (e.g., user lacks TOTP authenticator app) lands. Then design endpoint shape from FE consumer requirement.
- **Pattern note:** **3rd YAGNI-obsolete closure this session** — first PHASE7-FE-DTO-CONTRACT (memory `phase7-fe-dto-contract-closed-260508.md`), then REPORTS-DASHBOARD-ENDPOINT (above), now MFA-EMAIL-OTP-ENDPOINT. Pattern codified in new memory `verify-fe-consumer-before-be-build.md` (rule: BE backlog rows must verify FE consumer existence before scheduling implementation).

---

### REPORTS-DASHBOARD-ENDPOINT — Closed as YAGNI Obsolete (No FE Consumer)

- **Status:** RESOLVED-OBSOLETE 2026-05-09 18:45 ICT
- **Scope:** Backlog premise verified against source — no FE consumer exists for `GET /api/v1/reports/dashboard`. Cleanup-only (delete speculative skipped tests).
- **Source verify (10 min greps):**
  - `web/src/features/reports/` — only definitions/executions/run/modal pages exist; no `reports-dashboard-page.tsx`
  - `report-api.ts` — only definitions/executions/run/download hooks; no `useReportsDashboard`
  - Grep `reports/dashboard|reportsDashboard|getReportsDashboard` in `web/src/` → **0 hits**
  - Backlog row claimed "FE Dashboard route + 4 E2E specs" — 0 FE artifacts. 3 skipped TCs + 1 commented spec line are speculative placeholders.
- **Decision:** Option B (cleanup) over Option A (build). Building an endpoint without a consumer = YAGNI violation; would design a contract for hypothetical future FE that may never match real needs. Same closure pattern as PHASE7-FE-DTO-CONTRACT (memory `phase7-fe-dto-contract-closed-260508.md`).
- **Files changed:**
  - `web/e2e/reports-execution.spec.ts` — deleted lines 23-28 (TC-RPT-001/003/004 + skip comment); updated header line 4 ("dashboard KPIs" claim removed; added closure note pointing at REPORTS-DASHBOARD-ENDPOINT date).
  - `web/e2e/full-route-rendering.spec.ts` — deleted line 41 dead comment (`'/reports/dashboard' removed: BE has no GET endpoint...`).
  - `backlogs/backlog.md` — REPORTS-DASHBOARD-ENDPOINT row → RESOLVED-OBSOLETE with refute summary.
  - `backlogs/completed.md` — this entry.
- **Test state:** No test changes (only test deletions of 3 `test.skip` no-op stubs). Tier D suite count drops by 3 skipped (functional impact: zero — they never executed).
- **Code-review verdict:** N/A (test-deletion + docs cleanup, sub-trivial).
- **Reopen criteria:** A FE feature requesting an aggregate KPI endpoint at `/reports/dashboard` lands. At that point, design endpoint shape from FE consumer requirement, not from the speculative test names.

---

### PHASE7-TIER-D-TOKEN-REVOKE-FU1 — code-standards.md Auth-Helper Section Verify

- **Status:** RESOLVED 2026-05-09 18:20 ICT
- **Scope:** Docs-only verify after PR #97 merge (5dcf7f91). Confirm `docs/code-standards.md` E2E auth-helper/fixture section coverage for new-spec authors.
- **Verify result:** §"E2E Test Fixture Patterns — test-base.ts Auth-Helper 401-Retry" (Phase-07 TIER-D-TOKEN-REVOKE, 2026-05-09) at lines 1552-1603 is comprehensive — Problem Addressed, Usage Pattern (✅ test-base import / ❌ raw @playwright/test), 4 helper methods (forceFresh, invalidateToken, getRoleForToken, tokenToRole), Implementation Details (Proxy intercept location, retries policy, multipart bypass, fallback semantics), and FU2 extension trigger documented.
- **Fix:** 1 nit corrected — line 1596 changed `web/e2e/fixtures/test-base.ts:116 LOC` → `web/e2e/fixtures/test-base.ts:50 (Proxy get trap; file 117 LOC)` for unambiguous file path (`116` was previously total LOC, easily misread as line number; actual Proxy `get` trap is at line 50, file is 117 LOC total).
- **Files changed:** `docs/code-standards.md` (1 line edit), `backlogs/backlog.md` (row → RESOLVED), `backlogs/completed.md` (this entry).
- **Code-review verdict:** N/A (docs-only, sub-1-line edit).

---

### PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE — Auth-helper 401-Retry-with-Refresh Mitigation

- **Status:** RESOLVED 2026-05-09
- **Scope:** E2E test cascade from admin reference-token mid-suite revocation (introspection `active: false` despite wall-clock validity). Single-worker mode amplifies (one cached token across ~10min run); parallel mode ~4min avoids window.
- **Root Analysis:** Identified 4 callsites potentially invalidating cached token: (a) explicit `connect/revoke` in `authserver-introspect-revoke.spec.ts` (verified: revokes fresh token, not cached); (b) negative auth-flow lockout cascades on admin (TC-AUTH-022 swapped to lockout-victim); (c) AuthServer key rotation (none configured in dev); (d) admin SecurityStamp churn (not confirmed). Core issue: cache TTL mismatch (50min hardcoded vs ROPC 15min actual) shipped separately in v2.137-PHASE7-TIER-D-RESIDUAL; token revocation mid-suite remains latent.
- **Fix:** Implemented 401-retry-with-refresh in E2E test infrastructure. NEW `web/e2e/fixtures/test-base.ts` (116 LOC, Proxy-wrapped `request` fixture with 401 → invalidate-cache → refresh → retry). All 108 E2E specs codemodded `@playwright/test` → `./fixtures/test-base`. `auth-fixture.ts` now re-extends `test-base`. NEW `web/e2e/fixtures/auth-helper-invalidation.test.ts` (95 LOC, vitest 3/3 PASS, validates invalidateToken + getRoleForToken + forceFresh behavior).
- **Files changed:**
  - `web/e2e/fixtures/test-base.ts` (NEW, 116 LOC) — Base test fixture with Proxy-wrapped request + 401-retry logic.
  - `web/e2e/fixtures/auth-fixture.ts` — re-extends test-base (minor).
  - `web/e2e/fixtures/auth-helper.ts` — tokenToRole reverse map + invalidateToken + getRoleForToken + forceFresh opt (+47 LOC).
  - 108 spec files — codemod @playwright/test imports (bulk rename).
  - `web/src/test/e2e-fixtures/auth-helper-invalidation.test.ts` (NEW, 95 LOC) — vitest coverage.
  - `docs/code-standards.md` — E2E fixture choice rule (E2E auth-helper section, already updated this session).
- **Test results:** Cascade 87→0 (single-worker smoke), 30 distinct residuals pre-existing contract drift (separate backlog PHASE7-TIER-D-RESIDUAL-CONTRACT). Tier D suite 557/30 (verified token-revoke is NOT the blocker for remaining 30). Vitest auth-helper 3/3 PASS.
- **Code-review verdict:** APPROVE 9.2/10 (0 critical, 2 Important non-blocking addressed: multipart-stream warning comment added to test-base.ts; fixture naming clarity).
- **Branch status:** `main` (all commits pushed; awaiting git-manager squash + PR once journal complete).
- **Report:** `plans/reports/code-review-260509-1730-token-revoke-mitigation.md` + `plans/reports/verifier-260509-1730-tier-d-token-revoke-mitigation.md`

---

### PHASE4B-WF-FU2 — Clone TransactionScope Wrap

- **Status:** RESOLVED 2026-05-09
- **Scope:** `CloneWorkflowDefinitionCommandHandler` two-save sequence (UpdateAsync + AddAsync) not wrapped in transaction. Gap identical to PHASE7-WF-SAVEGRAPH-IDENTITY.
- **Fix:** Wrapped UpdateAsync (clone target definition copy) + AddAsync (new clone entity) in `TransactionScope(Required, ReadCommitted, AsyncFlowOption.Enabled)`. Mirrors proven PHASE7-WF-SAVEGRAPH-IDENTITY pattern.
- **Files changed:**
  - `src/modules/workflow/AqtCoreFW.Workflow.Application/Commands/CloneWorkflowDefinition/CloneWorkflowDefinitionCommandHandler.cs` (+14 LoC) — TransactionScope wrap with using statement + AsyncFlowOption.Enabled.
  - `tests/unit/AqtCoreFW.Workflow.Application.Tests/Commands/CloneWorkflowDefinitionCommandHandlerTests.cs` (+56 LoC) — 2 new unit tests (nominal clone, clone-with-state-copy assertion).
- **Test results:** Workflow.Application 429→431/431 PASS (+2 new); Architecture 26/26 PASS (no regression).
- **Code-review verdict:** APPROVE 9.4/10 (0 P0/P1, all minor/cosmetic addressed pre-merge).
- **Branch status:** `fix/phase4b-wf-fu2-clone-transaction` (uncommitted, ready for commit).
- **Report:** `plans/reports/code-review-260509-0648-phase4b-wf-fu2.md`

---

## 2026-05-08 (continued)

### PHASE7-FE-DTO-CONTRACT + PHASE7-FE-WF-DTO — Premise obsolete, closed without code change

- **Status:** CLOSED 2026-05-08 (no-op — verified empirically obsolete on `main`)
- **Original premise (2026-05-06, ~8-16h est):** 19 contract drift surfaces blocking FE Tier B/C/D — Group A (4 schemas T1↔T2 decision), Group B (14 mapper FK leaks long→Guid), Envelope (2 MFA admin POST endpoints). Plus PHASE7-FE-WF-DTO (`WorkflowDefinitionSummaryDto.CreatedAt` DateTimeOffset→DateTime).
- **Why closed without ship:** Source verify on current `main` shows 100% closed by prior ships. No remaining drift on Application surface that maps to a T2 entity.

| Sub-item | Resolved by | Evidence (current `main`) |
|---|---|---|
| Group A: `MfaPolicyDto` T1→T2 | PR-D3 (`0f0aa2b5`) | `src/modules/identity/AqtCoreFW.Identity.Application/MfaPolicies/MfaPolicyDto.cs` — `Guid Id` from `entity.PublicId` |
| Group A: `AiModelProfileDto`, `AiPromptTemplateDto` | PR-D3 (`0f0aa2b5`) | T2 + admin route `{publicId:guid}` |
| Group A: `AuditRetentionPolicyDto` | PR #76 decision | T1 doc-only skip (no FE consumer) |
| Group B: 14 T2-FK leak surfaces | PR-D2 (`a903fb35`) | `T2FkDriftBacklog = empty` HashSet at `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/SourceLevelRulesTests.cs:87` |
| Group B: `MessageLogDto.PartnerId/ContractId/FormTemplateId` | PR-B (`110a14b5`) | Already Guid |
| Group B: WorkflowDefinition/Instance | Phase-04b WF (`04cd0346`) | Application surface Guid |
| Envelope: MFA admin POST `long`→`Guid` | PR-D3 (`0f0aa2b5`) | `MfaPoliciesAdminController.cs:35,51` `ApiResponse<Guid>` + PUT `{publicId:guid}` |
| FE handwritten adapter request-shape | 2026-05-08 (`8e2f...`) | `partnerId→partnerPublicId` / `contractId→contractPublicId` in `integration-api.ts` |
| PHASE7-FE-WF-DTO: WF DTO shape | Phase-04b WF (PR #74, `04cd0346`) | DTO migrated; FE Tier B/C/D PASS confirms consumption |

- **Remaining `long {Name}Id` hits on Application surface (FormField/AiSession/AiChatMessage/WorkflowTransition/DocumentTemplate):** Intentional T1 design per PR #86 CLASSIFY (22 documentary `arch:accept-long T1` directives). R_T2FK rule does NOT fire (T1 not in T2 list).
- **Validation evidence:** FE Tier B (31/31), Tier C subset (13/14), Tier D (256/83) PASSED 2026-05-08 (PR #89, `60eba78e`) — proves contract layer is FE-functional with current shape.
- **Lessons:** Backlog rows estimated 8-16h became 0-LOC — premise drift over 5 ships across 2 days. Pre-cook source verify (~10min greps) avoids fictitious workflow. Memory feedback `Before recommending from memory: verify the memory is still correct` validated.
- **Branch:** `main` (no code change; backlog + journal only)

---

### AUTHSERVER-DI-ARCH — Architecture Test Guard for Transitive DI Registration

- **Status:** SHIPPED 2026-05-08 (branch `fix/authserver-di-arch-guard`, uncommitted)
- **Scope:** Prevent AuthServer host from missing transitive DI registrations. Pattern recurrence: v2.133p3 (`IMessageBus` missing → `/connect/authorize` runtime crash), 2026-05-08 session (`IGuidGenerator` missing → caught manually during PHASE7-TIER-BC-VALIDATION).
- **Approach:** Two-phase validation: (1) explicit service resolution chain on Program.cs 331-349 (Hangfire JobStorage, IPasswordVerifyTimingDecoy, IdentityDbContext, MessagingDbContext); (2) controller-enumeration — resolve all public ControllerBase types via `ActivatorUtilities.CreateInstance(sp, type)` to catch transitive dependency gaps. ValidateOnBuild=true rejected because AuthServer Program.cs disables it (Identity.Application MediatR handlers registered via assembly scan but AuthServer doesn't host repositories).
- **Files added (no production code change):**
  - NEW `tests/unit/AqtCoreFW.AuthServer.Tests/Startup/AuthServerHostFixture.cs` (~95 LOC) — WebApplicationFactory<Program> subclass with Testcontainers MsSql, ConnectionStrings:Default binding, IHostedService filter.
  - NEW `tests/unit/AqtCoreFW.AuthServer.Tests/Startup/AuthServerStartupValidationTests.cs` (~80 LOC) — 2 tests (explicit GetRequiredService chain, controller enumeration via ActivatorUtilities).
  - MOD `tests/unit/AqtCoreFW.AuthServer.Tests/AqtCoreFW.AuthServer.Tests.csproj` — added `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`.
- **Test results:** AuthServer.Tests 27→29/29 PASS (+2 new); Architecture 26/26 PASS (no regression). Positive: both tests pass. Negative: temporarily removed `AddSharedKernelGuidGenerator()` at Program.cs:177 → test FAILED with clear message identifying 3 affected controllers (Authorization/ExternalLogin/MfaChallenge) + root cause (`Unable to resolve IGuidGenerator while activating AuditService`). Restored and re-validated.
- **Code-review verdict:** PASS (0 P0/P1, 3 P3 cosmetic; M1 doc-tighten applied).
- **Backlog follow-ups:** 3 P3 FUs spawned (API host guard ~2h, SmokeTests relocation ~30m, generic helper extraction ~1h).
- **Branch status:** Ready for commit (`fix/authserver-di-arch-guard`). Depends on zero uncommitted changes in source/test code.

---

## 2026-05-08

### QD742-FU2 — Per-tenant lockout enforcement

- **Status:** SHIPPED 2026-05-08
- **Scope:** Replace hardcoded `MaxFailedAccessAttempts=5` trong AuthServer Program.cs (+ Identity.Infrastructure DI) với per-tenant `CredentialPolicy.MaxFailedAttempts/LockoutMinutes` lookup. Closes Phase-02 QĐ 742 follow-up backlog.
- **Approach:** Custom `TenantAwareUserManager : UserManager<ApplicationUser>` overrides `AccessFailedAsync` reading `ICredentialPolicyRepository.GetDefaultForTenantAsync(user.TenantId)`. Framework `IdentityOptions.Lockout` retained as fallback (no tenant context, no policy, or zero values).
- **Files changed:**
  - NEW `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/TenantAwareUserManager.cs` (~50 LOC)
  - NEW `tests/unit/AqtCoreFW.Identity.Infrastructure.Tests/Authentication/TenantAwareUserManagerTests.cs` (~140 LOC, 4 cases)
  - MOD `src/host/AqtCoreFW.AuthServer/Program.cs` — `.AddUserManager<TenantAwareUserManager>()` + fallback comment
  - MOD `src/modules/identity/AqtCoreFW.Identity.Infrastructure/InfrastructureRegistration.cs` — same DI symmetry
  - MOD `docs/dependency-map.md` — note `TenantAwareUserManager` under ApplicationUser entry
- **Tests:** 7/7 unit (policy maxAttempts=10 honored at #10 not #5; no-tenant fallback; null-policy fallback; correct lockout span; **+3 edge from code-review:** maxAttempts=0 fallback, lockoutMinutes=0 fallback, TenantId=Guid.Empty short-circuit). Identity.Infrastructure 106/106, AuthServer 27/27, Architecture 26/26 PASS — zero regression.
- **Risk:** Backward compat ZERO — tenants without policy → identical behavior (5 attempts / 15min). No migration needed.
- **Follow-ups deferred:** Q1 cache lookup (`IMemoryCache` 5-min TTL keyed by tenantId) — failed logins low-volume; defer until prod metric.

---

### PHASE4A-SEEDER-AUDIT — Seeder Dapper DTO Audit

- **Status:** RESOLVED 2026-05-08
- **Scope:** Post-RT-3/4/5 (PR #75 fixes), sweep all seeder-suite classes for Guid↔Id/PublicId drift via positional-record Dapper materialization incompatibility.
- **Findings:** 1 drift confirmed (vs premise "0 drift-confirmed"), 2 pre-existing fixes from same branch.
- **Fix:** `BtcAssignmentSeeder.UserRow` positional record → sealed class with parameterless ctor + property setters (`init` accessors). Template match to `BtcIdentitySeeder.Users` precedent (PR #75).
- **Files changed:**
  - `src/modules/organization/AqtCoreFW.Organization.Infrastructure/Seeding/BtcAssignmentSeeder.cs:184` — `UserRow` class conversion (10 LOC).
- **Test results:** Architecture 26/26 PASS, Organization unit 128/128 PASS, Smoke 1/1 PASS.
- **Code-review verdict:** APPROVE 9.7/10, 0 critical.
- **Backlog follow-ups:** 3 P3 FUs spawned (static seeder state isolation, ValueGeneratedNever() sweep, architecture rule guard ban `record.*\?` in seeder files).
- **Plan:** `plans/260508-1908-phase-04a-seeder-audit/plan.md` — **status: complete**.

---

## 2026-05-08 (continued)

### AUDIT-INCIDENT-DTO-DAPPER — P1 Bug Fix

- **Status:** SHIPPED 2026-05-08
- **Root cause:** `GET /api/v1/admin/incidents` returns 500 — `SecurityIncidentDto` positional record signature mismatch with Dapper materialization. SQL columns (`int Severity`, `string Status`, `DateTime CreatedAt`) don't coerce into positional params.
- **Fix:** `sealed record` → `sealed class` with parameterless ctor + auto-setters. `CreatedAt: DateTimeOffset → DateTime` to align domain entity.
- **Files changed:**
  - `src/modules/audit/AqtCoreFW.Audit.Application/DTOs/AuditLogDto.cs` — SecurityIncidentDto class conversion.
  - NEW `tests/integration/AqtCoreFW.Audit.Integration.Tests/SecurityIncidentsApiTests.cs` — regression guard, 2 tests PASS.
- **Code-review verdict:** APPROVE 9.3/10.
- **Backlog follow-ups:** AUDIT-DTO-FU1 (P3, XML doc warning on LoginAttemptDto/AuditLogDto) + AUDIT-DTO-FU2 (P3, test assertion UX improvement).
- **Report:** `plans/reports/code-review-260508-1700-audit-incident-dto-dapper.md`

---

### PHASE7-WF-SAVEGRAPH-IDENTITY — P1 Production-Blocker Fix (P1)

- **Status:** SHIPPED 2026-05-08 (main, uncommitted)
- **Root cause:** `PUT /api/v1/workflow/definitions/{id}/graph` returns HTTP 500 `SqlException: Cannot insert explicit value for identity column in table 'WorkflowStates' when IDENTITY_INSERT is set to OFF`. Raw-SQL `INSERT INTO WorkflowStates (Id, …)` with explicit `Id=0` (in-memory) against SQL Server IDENTITY column rejected. Blocks all visual-designer save flows + workflow-dynamic-instances E2E.
- **Fix approach:** 2-phase EF save (AddState/AddTransition via aggregate; SaveChanges to get DB-assigned IDs; name→Id map for transitions; SaveChanges again) wrapped in TransactionScope. Mirrors proven `CreateWorkflowDefinitionCommandHandler` pattern.
- **Files changed (5):**
  - NEW `src/modules/workflow/AqtCoreFW.Workflow.Application/Repositories/NewTransitionSpec.cs` — spec DTOs for name-based transition building.
  - `src/modules/workflow/AqtCoreFW.Workflow.Application/Repositories/IWorkflowDefinitionRepository.cs` — signature change `List<WorkflowTransition>` → `IReadOnlyList<NewTransitionSpec>`.
  - `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Persistence/WorkflowDefinitionRepository.cs` — full rewrite `ReplaceGraphAsync` + TransactionScope + EF two-save + name→Id mapping.
  - `src/modules/workflow/AqtCoreFW.Workflow.Application/Commands/SaveDefinitionGraph/SaveDefinitionGraphCommandHandler.cs` — refactor to pass specs, ValidationError correction (M4 applied).
  - NEW `tests/integration/AqtCoreFW.Workflow.Integration.Tests/SaveDefinitionGraphIntegrationTests.cs` — regression test: 3-state + 2-transition graph save with IDENTITY assertion + FK integrity checks + audit field population.
- **Test results:** Workflow.Application 429/429 PASS, Architecture 26/26 PASS, SaveDefinitionGraphIntegrationTests PASS (HTTP 200, all rows persisted with IDENTITY > 0, FK wiring correct, audit fields populated). Pre-fix proof: stashed test fails with exact IDENTITY_INSERT error on baseline.
- **Code-review verdict:** APPROVE_WITH_CONCERNS 9.0/10 (M1 TryGetValue + M4 ValidationError applied pre-merge).
- **Side-effect closures:** PHASE4B-WF-FU1 (TransactionScope wrap on ReplaceGraphAsync) shipped as part of rewrite.
- **Report:** `plans/reports/code-review-260508-1500-phase7-wf-savegraph-identity.md`

### PHASE7-TIER-C-FU1 — Workflow Spec Fixture Drift Fix (P2)

- **Status:** SHIPPED 2026-05-08
- **Root cause:** Post-Cases-removal (2026-05-01), only `GenericEntityStatusEnumContributor` remains in `IEntityStatusEnumRegistry`; specs sent stale `entityType` (defaulted to "Case") + invalid state name `Review` (not in Generic's allowed set `[Draft, Submitted, InReview, Approved, Rejected, Cancelled]`) + wrong field name `workflowDefinitionId` (should be `definitionId`) + string `entityId` instead of Guid.
- **Files:**
  - `web/e2e/workflow-dynamic-instances.spec.ts` — added `entityType: 'Generic'` to POST /definitions body; renamed graph state `Review` → `InReview` (4 sites: state def, 2 transitions From/To, comments).
  - `web/e2e/workflow-instances-tasks.spec.ts` — renamed `workflowDefinitionId` → `definitionId`; `entityId: crypto.randomUUID()`.
- **Test results:** `tsc --noEmit` clean. No production code changed. Fixture unblocks 13 cascaded tests (2 spec files × ~6–7 tests each).
- **Code-review verdict:** APPROVE 9.7/10
- **Deferred:** Runtime Playwright verification (needs full stack boot via `/aqt-runapp`) → spawned PHASE7-TIER-C-FU1-VERIFY backlog item.

---

### BE-LOGIN-USER-ENUMERATION — OWASP A04 User-Enumeration Fix (P2)

- **Status:** RESOLVED 2026-05-08
- **Root cause:** `AuthorizationController.cs:273` user-not-found path returned "Email hoặc mật khẩu không đúng." vs `:318/323` user-found wrong-password returned "Sai tên đăng nhập hoặc mật khẩu" — different messages enabled OWASP A04 user enumeration attack vector.
- **Fix:** L273 message unified to canonical "Sai tên đăng nhập hoặc mật khẩu." (matches existing wrong-password branch).
- **Files changed:**
  - `src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs:273` — message string updated.
  - `web/e2e/authserver-login-page.spec.ts` — TC-AUTH-021 + TC-AUTH-022 assertions tightened from regex-alternation to single canonical message.
- **Test results:** AuthServer 0E/0W, Identity unit 225/225 PASS, Architecture 26/26 PASS.
- **Code-review verdict:** APPROVE 9.5/10, 0 critical.
- **Backlog follow-ups:** 1 P3 residual spawned (BE-LOGIN-TIMING-SIDE-CHANNEL — dummy password-hash call to eliminate work-factor timing leak).
- **Plan:** `plans/260508-1958-be-login-user-enumeration/plan.md` — **status: complete**.

---

### PHASE7-FE-DTO-CONTRACT-READ-SIDE — FE Adapter Request-Shape Sync (P2)

- **Status:** SHIPPED (branch `fix/phase7-fe-message-log-request-shape`, awaiting commit)
- **Premise correction:** Backlog claimed `MessageLogDto.ContractId : long?` + `ListMessageLogsQuery.ContractId : long?` symmetry needed. Source verification 2026-05-08 found BE already 100% Guid-symmetrical (`MessageLogDto.Id/PartnerId/ContractId` all Guid, `ListMessageLogsQuery.PartnerPublicId/ContractPublicId` Guid?, `MessageLogsController` `[FromQuery] partnerPublicId/contractPublicId` Guid?). Real drift surfaced in **handwritten FE adapter** `web/src/features/integration/integration-api.ts` + 2 form pages — request-side field names (`partnerId`/`contractId`) silently dropped by ASP.NET model binder which expects `partnerPublicId`/`contractPublicId`. Filter dropdowns ineffective + Create POST would 422.
- **Files:**
  - `web/src/features/integration/integration-api.ts` — `CreateMessageLogDto`/`CreateContractDto` field renames + `useMessageLogs`/`useContracts` query-param signatures + `messageLogList`/`contractList` query-key signatures.
  - `web/src/features/integration/message-log-list-page.tsx` — `Form.Item name="partnerId|contractId"` → `partnerPublicId|contractPublicId` in LogMessage modal.
  - `web/src/features/integration/contract-list-page.tsx` — `Form.Item name="partnerId"` + `mutateAsync({ partnerId })` → `partnerPublicId` in CreateContract modal.
- **Read-side response shape unchanged:** `MessageLogDto.partnerId`/`contractId` + `ContractDto.partnerId` JSON fields match BE camelCase output → drawer/table dataIndex untouched.
- **Test results:** `tsc --noEmit` exit 0. No vitest exists for integration feature (manual smoke deferred to user runtime).
- **Bonus scope:** Found Contract feature had identical drift (`CreateContractBody.PartnerPublicId` Guid vs FE `partnerId`), bundled in same fix.

---

### PHASE4D-* Trio — Backlog Hygiene (closing stale lines from 2026-05-04 phase shipped same-day)

- **PHASE4D-EXEC (P1) — RESOLVED 2026-05-04:** Phase-04d EF config base-class migration shipped same-day as filed via 3 commits (`b7068c81` initial 72 configs + `29aaf284` + `8bb3af1f` follow-up 3 misses). Plan `plans/260504-2140-phase-04d-ef-config-base-migration/plan.md` `status: complete`, `actual_effort: ~3h`. Phase-06/6C apply step subsequently shipped 2026-05-05 (`a809addf` + chain) — 19/19 migrations applied, SchemaUniversalsTests 4/4 GREEN. Verified 2026-05-08: **86 configs** inherit `EntityTypeConfigurationBase` / `HotTableConfigurationBase` across all module Infrastructure projects (vs plan estimate 72; Phase-04a/04b residual configs absorbed). Sole `HotTableConfigurationBase` consumer = `WorkflowTaskConfiguration` matches plan B5 expectation. Backlog line was stale-orphaned for ~4 days because Phase-06/6C journal closed it implicitly.
- **PHASE4D-CORRECT-1 — DROPPED 2026-05-04** per `plans/260504-2140-phase-04d-ef-config-base-migration/AMENDMENT-260504-2207-soft-delete-redundancy.md`. Premise ("soft-delete query filter missing on ~75 entities = correctness bug") refuted by `ModuleDbContext.ApplyGlobalFilters` walking all `ISoftDeletable` entities + calling `entityType.SetQueryFilter` after configs apply. Filter enforced regardless of config base class — zero correctness gap.
- **PHASE4D-ARCH-1 (P1) — RESOLVED 2026-05-04:** Shipped as `tests/architecture/AqtCoreFW.Architecture.Tests/PublicMessageIdAkCoverageTests.cs` (B10 of Phase-04d plan). 2 facts: PublicId path = base-class inheritance; MessageId path = base OR manual AK. Suite GREEN. Scope-shifted vs original (IsDeleted HasQueryFilter coverage → PublicId/MessageId AK coverage) per amendment.

---

## 2026-05-07

### PHASE7-FE-DTO-CONTRACT-CLASSIFY — NotificationTemplate T2 Retier + ACCEPT-LONG Annotations (P1)

- **Status:** SHIPPED v2.137 (feat/phase-07-fe-dto-contract-classify, awaiting git-manager finalize)
- **Plan:** `plans/260507-2115-phase-07-fe-dto-contract-classify/`
- **Phase-01:** NotificationTemplate T1→T2 retier — `IPublicIdentified` + `Guid PublicId` AK. `Create()` factory → `IGuidGenerator gen`. Migration `20260507144339_AddNotificationTemplatePublicId`. `SendNotificationCommand.TemplateId` `long?→Guid?`; handler resolves via `GetInternalIdByPublicIdAsync`. Admin routes `{publicId:guid}`. Create returns `Guid`. Integration test MfaAtHeader fix.
- **Phase-02:** 22 `// arch:accept-long T1 <Entity>: <reason>` directives on WorkflowTransition (7), WorkflowState (6), FormField (9) FE-facing long surfaces.
- **Phase-03:** CSV row 76 sync (T1→T2). Docs: dependency-map, changelog, roadmap, backlog.
- **Test results:** Build 0E/0W; Architecture 26/26; Notifications unit 26/26; Notifications integration 7/7; FE tsc 0 errors.

---

### WebAppFixture Seeder Gate — False-Green Pattern Fix (P1)

- **Status:** SHIPPED (working tree, ready to commit)
- **Plan:** `plans/260507-1739-webappfixture-seeder-gate/`
- **Journal:** `docs/journals/journal-260507-1921-webappfixture-seeder-gate.md`
- **Files:**
  - NEW `src/shared/AqtCoreFW.SharedKernel/Application/Hosting/IIntegrationTestSeeder.cs` (marker interface)
  - NEW `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/SeederMarkerContractTests.cs` (2 architecture facts)
  - NEW test project `tests/integration/AqtCoreFW.Startup.SmokeTests/` (csproj + fixture + collection + test)
  - MODIFIED `src/AqtCoreFW.slnx` (registered new test project)
  - MODIFIED `tests/shared/AqtCoreFW.Tests.SharedFixtures/WebAppFixture.cs` (seeder predicate filter + comment M1)
  - MODIFIED 9 seeder source files (1-line marker append + using statement)
- **Test delta:** Architecture suite 30/30 → 32/32 PASS (+SeederMarkerContractTests); Smoke 1/1 PASS in 15s; Identity.Integration 54/54 stable; Org/MasterData pre-existing failures confirmed unchanged
- **Code-review verdict:** APPROVE_WITH_MINOR 8.5/10
  - M1 predicate-lambda requirement comment (done at WebAppFixture.cs:79-83)
  - M2 env-var-bleed fix (seeder enable flags moved to ConfigureAppConfiguration only, eliminates parallel-test assembly bleed risk)
- **Verification:** Build 0E/0W; contract test 2/2 GREEN; smoke test 1/1 GREEN; Identity integration validates seeders run alongside existing tests
- **Root cause solved:** 3× pattern (Phase-04e, Phase-04b-follow-up, Phase-04a-RT) where WebAppFixture stripped all IHostedService → seeders never ran in tests → seeder bugs slipped to prod startup. Now marker-gated at test-infrastructure level.
- **Backlog deferred:** 5 follow-ups (PHASE1-SEED-M1/M2/m1/m2/m3)

### PHASE4C-ARCH — Tier-rule architecture tests (P1)

- **Status:** SHIPPED, awaiting commit (review pending from code-reviewer)
- **Plan:** `plans/260507-1652-phase-04c-arch-tier-rules/`
- **Journal:** `docs/journals/journal-260507-1700-phase-04c-arch-tier-rules.md`
- **Files:** 1 new test file (`tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/TierRepoArchitectureRulesTests.cs` ~250 LOC) + 4 plan/research markdown files + 1 journal + dependency-map.md header bump
- **Test delta:** Architecture suite 27/27 → 30/30 PASS
- **Rules added:**
  - `R_INTERNAL_CTRL` — controllers banned từ gọi repo `*InternalAsync` (forward-looking guard for Phase-4b sibling work)
  - `R_T2_REPO_SIG` — T2 repos ban single-overload `GetByIdAsync(long)` (entity-aware filter via CSV; T1 immune by design; ASP.NET Identity Guid-PK satellites exempt)
  - `CoverageFact_T2_Entities_Have_Discoverable_Repository_File` — every T2 entity has discoverable repo OR is on documented exception list (3 maintenance lists)
- **Verification:** Build 0E/0W; negative-test probe injections FIRED both rules; T1 immunity confirmed (14 existing T1 declarations stayed silent); clean revert verified.
- **Backlog claim correction:** original PHASE4C-ARCH claimed 4 missing rules — re-grep showed R1 (Guid.NewGuid ban) + R_T2FK (DTO long-Id leak ban) already exist in `SourceLevelRulesTests.cs`. Real gap: only 2 rules + 1 coverage fact.
- **Decisions deferred:** R_T2FK extension to controllers/ModuleClients (YAGNI); permanent `[Theory]` neg-tests (one-shot manual sufficient); suppress directive on R_T2_REPO_SIG (KISS, ~1h refactor when first edge case arises).
- **Phase-08 finalize gate UNBLOCKED.**

---

## 2026-05-05

### Phase-04a Seeder Regressions Fixes (Branch: fix/phase-04a-rt-seeder-regressions)
- **Status:** 3 P0 fixes APPROVED, pending commit + merge to main
- **Code-review ratings:** All 9/10 APPROVE

### PHASE4A-RT-3: BtcIdentitySeeder Cross-Module FK Type
- **Files:** `src/seeders/BtcIdentitySeeder.Users.cs:131,356`
- **Fix:** `SELECT PublicId AS Id` (OrgUnits.PublicId Guid is cross-module FK target, not internal long Id)
- **Fix:** Positional record → class for Dapper null-handling compatibility
- **Verification:** API host pass-2: 1139 OrgUnits + 10000 users seeded clean

### PHASE4A-RT-4: TenantBackfillSeeder Guid vs Bigint Clash
- **Files:** `src/seeders/TenantBackfillSeeder.cs:74,119-134`
- **Fix:** `WHERE t.PublicId = a.TenantId` (Tenants.Id is bigint IDENTITY DB-assigned, not Guid PublicId input)
- **Fix:** INSERT column list: dropped `Id`, added `PublicId`
- **Verification:** API host pass-2: "Backfilled 2 tenants" logged

### PHASE4A-RT-5: FormFieldConfiguration DB IDENTITY Assignment
- **Files:** `src/modules/forms/Forms.Infrastructure/Persistence/FormFieldConfiguration.cs` + migration `20260505030102_InitialCreateFoundationV1`
- **Fix:** Removed `ValueGeneratedNever()`, restored DB IDENTITY convention (default Entity behavior)
- **Fix:** Migration Designer + Snapshot updated to match FormTemplate sibling pattern
- **Verification:** E2ETestDataSeeder boot: 4 fields auto-IDENTITY assigned, no duplicate-key tracking error

---

## 2026-04-11

### RBAC Menus Bug Fixes (T2-01)
- **Commit:** `310757b7`
- F4 (Critical): Build-then-prune tree — children no longer orphaned
- F5: Fallback perm codes aligned to PermCodes.* constants
- F6: No flash of over-privileged menu during loading
- F8: RolePermissions seeder idempotency fixed
- F10: Empty group nodes auto-pruned

### Forms Permission Gating (T2-02)
- **Commit:** `875d08b9`
- FORMS.SUBMIT.APPROVE + REJECT permissions (105→107)
- 9 endpoints gated with correct policies
- Self-approval/rejection guard in 4 handlers (SoD)
- ExportPdf gated with EXPORT policy

### Forms FE Red-Team Fixes (10 findings)
- **Commit:** `ff112ccc` — 25 files, +804/-74
- F1: Auth submit modal consent fix (PDPL)
- F2: RequiredIf runtime evaluation
- F3: File upload data loss fix (hidden Form.Item)
- F4: DOMPurify XSS sanitization for RichText
- F5: File upload per-field size/extension validation
- F6: RichText ErrorBoundary fallback
- F7: ~75 i18n keys (en.json + vi.json)
- F8: View columns editable after creation (BE + FE)
- F9: Signature canvas resize preservation
- F10: Table field double-render elimination
- **Tests:** 38 FE vitest tests

### Server-side RichText HTML Sanitization
- **Commit:** `a44eff0e`
- HtmlSanitizer (Ganss.Xss) in FormDataPreprocessor
- Allowlist mirrors FE DOMPurify config
- **Tests:** 10 BE xunit tests

### BE Consent Enforcement — Verified
- Both SubmitFormCommandHandler and SubmitPublicFormCommandHandler enforce `consentGiven` when `requiresConsent && hasPiiFields`
- No code change needed — already correct

---

## 2026-04-10

### Local Dev Automation + Reverse Proxy Support
- **Commit:** `1c7ec7e3`
- 4 scripts (1_run-local-dev, 2_start-tunnels, 3_setup-tunnel-urls, 4_expose-demo)
- ForwardedHeaders middleware (Cloudflare Tunnel, nginx, Docker)
- CORS hardening (X-Correlation-Id, X-Tenant-Id)
- API baseURL fix (Vite env injection)

### Project Progress Report
- HTML dashboard + DOCX with per-module readiness (75% overall, 17 modules)

---

## 2026-04-09

### RBAC Roles & Permissions CRUD
- **Commit:** `f29e77a1` + 14 commits
- Role CRUD APIs (POST/PUT/DELETE), permission assignment with SoD pre-check
- FE admin interface (create/edit modal, permission matrix)
- Permission-based controller auth (105 PermCodes, ~40 controllers)

### Chat UI Redesign (Telegram-style)
- ConversationDto enriched (LastMessagePreview, UnreadCount, SenderName)
- OUTER APPLY query optimization

### RBAC FE i18n migration (partial)
- 40 keys migrated, 3 TSX files remaining

---

## 2026-05-09 (moved from backlog)

### PHASE4B-WF-FU2 — Clone TransactionScope Wrap

- **Status:** RESOLVED 2026-05-09
- **Scope:** `CloneWorkflowDefinitionCommandHandler` two-save sequence (UpdateAsync + AddAsync) wrapped in TransactionScope.
- **Files:** CloneWorkflowDefinitionCommandHandler.cs (+14 LoC), CloneWorkflowDefinitionCommandHandlerTests.cs (+56 LoC, 2 new unit tests).
- **Tests:** Workflow.Application 429→431/431 PASS (+2 new), Architecture 26/26 PASS.
- **Code-review:** APPROVE 9.4/10 (0 P0/P1 unresolved).
- **Report:** `plans/reports/code-review-260509-0648-phase4b-wf-fu2.md`

### PHASE7-TIER-D-CASCADE — Cascade Fix Re-Verify

- **Status:** RESOLVED 2026-05-09
- **Scope:** Source fix shipped 2026-05-09 (TC-AUTH-022 admin→lockout-victim swap; account-change-password admin→viewer swap).
- **Re-verify:** 2026-05-09 16:00 ICT: `npx playwright test --last-failed --workers=1` → **64 PASS / 19 FAIL of 83** retried.
- **Result:** Cascade-resolved 64/83 = 77% (admin lockout was dominant root cause).
- **Residual:** 19 non-cascade failures classified into PHASE7-TIER-D-RESIDUAL.
- **Code-review:** 8.8/10 APPROVE_WITH_MINOR.

---

## 2026-05-08 (moved from backlog)

### AUDIT-INCIDENT-DTO-DAPPER — P1 Bug Fix

- **Status:** RESOLVED 2026-05-08
- **Root cause:** `GET /api/v1/admin/incidents` returns 500 — `SecurityIncidentDto` positional record mismatch.
- **Fix:** Record → class with parameterless ctor + auto-setters. `CreatedAt: DateTimeOffset → DateTime`.
- **Files changed:**
  - `src/modules/audit/AqtCoreFW.Audit.Application/DTOs/AuditLogDto.cs`
  - NEW `tests/integration/AqtCoreFW.Audit.Integration.Tests/SecurityIncidentsApiTests.cs` (2 PASS).
- **Code-review:** APPROVE 9.3/10.
- **Report:** `plans/reports/code-review-260508-1700-audit-incident-dto-dapper.md`

### PHASE4B-WF-FU1 — ReplaceGraphAsync TransactionScope Wrap

- **Status:** RESOLVED 2026-05-08
- **Scope:** `WorkflowDefinitionRepository.ReplaceGraphAsync` bundled in PHASE7-WF-SAVEGRAPH-IDENTITY rewrite.
- **Fix:** States-save → capture IDENTITY → transitions-save wrapped in TransactionScope.
- **Files:** `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Persistence/WorkflowDefinitionRepository.cs:135`
- **Code-review:** 9.0/10.

### PHASE4A-SEEDER-AUDIT — Dapper DTO Audit

- **Status:** RESOLVED 2026-05-08
- **Scope:** Sweep all seeder-suite classes for Guid↔Id/PublicId drift via positional-record Dapper materialization.
- **Fix:** `BtcAssignmentSeeder.UserRow` positional record → sealed class.
- **Files:** `src/modules/organization/AqtCoreFW.Organization.Infrastructure/Seeding/BtcAssignmentSeeder.cs:184`
- **Tests:** Architecture 26/26 PASS, Organization unit 128/128 PASS, Smoke 1/1 PASS.
- **Code-review:** APPROVE 9.7/10.

---

## 2026-05-05 (moved from backlog)

### PHASE4A-RT-5 — FormFieldConfiguration DB IDENTITY Assignment

- **Status:** RESOLVED 2026-05-05
- **Scope:** Removed `ValueGeneratedNever()`, restored DB IDENTITY convention.
- **Files:** `src/modules/forms/Forms.Infrastructure/Persistence/FormFieldConfiguration.cs` + migration `20260505030102_InitialCreateFoundationV1`
- **Verification:** E2ETestDataSeeder boot: 4 fields auto-IDENTITY assigned, no duplicate-key error.
- **Code-review:** APPROVE 9/10.

### PHASE4A-RT-4 — TenantBackfillSeeder Guid vs Bigint Clash

- **Status:** RESOLVED 2026-05-05
- **Scope:** `WHERE t.PublicId = a.TenantId` (Tenants.Id is bigint IDENTITY, not Guid PublicId).
- **Files:** `src/seeders/TenantBackfillSeeder.cs:74,119-134` — INSERT cols dropped `Id`, added `PublicId`.
- **Verification:** API host: "Backfilled 2 tenants" logged.
- **Code-review:** APPROVE 9/10.

### PHASE4A-RT-3 — BtcIdentitySeeder Cross-Module FK Type

- **Status:** RESOLVED 2026-05-05
- **Scope:** `Users.cs:131,356` SQL `SELECT PublicId AS Id` (OrgUnits.PublicId Guid cross-module target).
- **Fix:** Positional record → class for Dapper null-handling.
- **Verification:** API host: 1139 OrgUnits + 10000 users seeded clean.
- **Code-review:** APPROVE 9/10.

---

## 2026-05-04 (moved from backlog)

### PHASE4D-EXEC — EF Config Base-Class Migration

- **Status:** RESOLVED 2026-05-04
- **Scope:** Phase-04d EF config base-class migration shipped via 3 commits (`b7068c81` + `29aaf284` + `8bb3af1f`).
- **Results:** **86 configs** inherit `EntityTypeConfigurationBase`/`HotTableConfigurationBase`.
- **Verification:** Phase-06/6C apply step shipped 2026-05-05.
- **Plan:** `plans/260504-2140-phase-04d-ef-config-base-migration/plan.md` (status: complete, ~3h).

### PHASE4D-CORRECT-1 — Soft-Delete Filter Redundancy

- **Status:** DROPPED 2026-05-04
- **Premise:** Refuted — `ModuleDbContext.ApplyGlobalFilters` enforces soft-delete filter regardless of config base class.
- **Amendment:** AMENDMENT-260504-2207-soft-delete-redundancy
- **Verdict:** Zero correctness gap.

### PHASE4D-ARCH-1 — PublicMessageId AK Coverage

- **Status:** RESOLVED 2026-05-04
- **Scope:** PublicId/MessageId AK coverage test — `tests/architecture/AqtCoreFW.Architecture.Tests/PublicMessageIdAkCoverageTests.cs` (B10).
- **Facts:** PublicId path = base inheritance; MessageId path = base OR manual AK.
- **Tests:** Suite GREEN.
- **Code-review:** APPROVE 9/10.

---

## 2026-05-27 (moved from backlog — optimization pass: 52 closed sections)

> Archived from `backlog.md` during the 2026-05-27 README-compliance optimization (header history condensed, backlog 1304→635 lines). All sections below were closed/shipped before this date; each retains its original close-note + resolving commit.

### ~~P6-DEFER-2 (4C-1, T3 P2, ~1h): WebAppFactory EF model-cache contamination fix (test fixture)~~ ✅ CLOSED-WONTFIX-YAGNI 2026-05-22
- **Origin:** 4-C M1 production fix landed via `WorkflowTaskRepository.cs:137` `.IgnoreQueryFilters().AsNoTracking()` defense-in-depth. Test fixture across classes sharing `DbContextOptions<T>` still has the model-cache contamination class.
- **Scope:** Add `IModelCacheKeyFactory` discriminator OR consistent mock behavior across integration test fixtures using shared options.
- **Resolution:** WONTFIX per YAGNI. Scout confirms (1) `ApiFactory` does NOT substitute `ITenantContext` — runs production `HttpContextTenantContext` (grep `RemoveAll<ITenantContext>|AddScoped<ITenantContext|Substitute.For<ITenantContext` against `src/tests/AqtCoreFW.Tests.Integration/` returns 0 hits); (2) unit-test classes that mock `ITenantContext` each instantiate their own `WorkflowDbContext` with `UseInMemoryDatabase(Guid.NewGuid())` → fresh model cache per class, no cross-contamination; (3) production defense shipped at `WorkflowTaskRepository.cs:135` + `WorkflowInstancesCreateFirstTaskTests.cs:124-185` already applies the canonical `.IgnoreQueryFilters().AsNoTracking()` pattern; (4) `4-C-A IgnoreQueryFilters Re-Query Pattern` is documented in `docs/code-standards.md` as a permanent INVARIANT. Integration suite stable at 71/71. The "contamination class" is theoretical given current fixture behavior — no failing repro to anchor an `IModelCacheKeyFactory` rework. Re-open if a future test fixture substitutes `ITenantContext` AND shares `DbContextOptions<T>` across classes.

### ~~P6-DEFER-3 (4C-4, T3 P3, ~30min): F7 multi-state race deterministic test~~ ✅ CLOSED 2026-05-22
- **Origin:** 4-B carry-fwd; 4-C re-query+switch Status discriminator + `WinnerFirstInterceptor` already cover the production code path.
- **Scope:** Add deterministic race test for concurrent state-change race on non-Claim handler.
- **Resolution:** Shipped — `WorkflowTaskRowVersionRegressionSweepTests.F7r_ConcurrentCancel_InterceptorForcesWinnerFirst_LoserThrowsConcurrencyException`. Reuses `WinnerFirstInterceptor` from `ClaimGuardExceptionDiscriminationTests` (same assembly). Two contexts race on same task's Cancel mutation; interceptor gates loser's SavingChanges until winner commits → loser's UPDATE WHERE RowVersion=stale → DbUpdateConcurrencyException deterministically. Closes 4-B H1 advisory for state-change handlers. WF.Infra 80/80.

### ~~P6-DEFER-5 (5b-L1, T4 P4, ~15min): Dead `IsFailed` branches cleanup~~ ✅ CLOSED-AUDIT-CLEAN 2026-05-22
- **Origin:** 5b code-review L1.
- **Scope:** Audit `WorkflowInstanceResult` callers for unreachable `IsFailed` branches now that contract is stable.
- **Resolution:** Audit complete. `WorkflowInstanceResult` exposes only `IsSuccess` (no `IsFailed` property). All 3 callers (Forms.SubmitForm, Forms.SubmitPublic, BP.StartBusinessProcessRun) use canonical `!wfResult.IsSuccess` check. The `saveResult.IsFailed` branch in `InProcessWorkflowModuleClient.cs:122-132` handles legitimate DB-failure path (not dead). No code change needed.

### ~~P6-DEFER-6 (5b-L3, T4 P4, ~15min): Title null-fallback hardening~~ ✅ CLOSED 2026-05-22
- **Origin:** 5b code-review L3.
- **Scope:** Defensive null-check on `initialState.DisplayNameVi` in instance-creation task title.
- **Resolution:** Shipped — both call sites now fall back to `initialState.Name` when `DisplayNameVi` is null/whitespace: `CreateWorkflowInstanceCommandHandler.cs:111-115` + `InProcessWorkflowModuleClient.cs:113-118`. Mirrors existing `ParallelBranchService.cs:66` pattern.

### ~~P6-DEFER-7 (5c-L-NEW-1, T3 P3, ~15min Testcontainers): DB-roundtrip canonical persistence proof~~ ✅ CLOSED 2026-05-22
- **Origin:** 5c code-review carry-fwd.
- **Scope:** Add Testcontainers fact asserting canonical-at-write PermCodes round-trip (write → read → assert normalized form).
- **Resolution:** Shipped — `WorkflowGraphPublishValidationTests.I8_Activate_WithMixedCasePermissionCode_Returns204_AndPersistsCanonicalUppercase`. Seeds clause with lowercase `"workflow.task.process"`; activation succeeds (204 NoContent); 4-C-A read-back asserts persisted `RefsJson == "[\"WORKFLOW.TASK.PROCESS\"]"` (canonical uppercase) AND `IsActive=true`. Closes the bytes-on-disk evidence gap that I5/I6/I7 left implicit. WorkflowGraphPublishValidation 4/4.

### ~~P6-DEFER-8 (5c-M3, T4 P4, ~15min): `ValidateDefinitionGraphAsync` NotFound vs 422 error mapping~~ ✅ CLOSED-WONTFIX-COSMETIC 2026-05-22
- **Origin:** 5c code-review M3.
- **Scope:** Discriminate NotFound (definition does not exist) from ValidationFailed (graph invalid) — currently both return 422.
- **Resolution:** WONTFIX per YAGNI. Current sole caller `ActivateBusinessProcessCommandHandler.cs:67-78` does not branch on NotFound vs ValidationFailed (both surface as 422 with `GraphErrors` metadata). Adding `IsNotFound` discriminator field would be dead code. Re-open when a caller needs the discriminator.

### ~~P6-DEFER-9 (5c-M4, T4 P4, ~10min): `IWorkflowGraphValidator` DI lifetime cosmetic~~ ✅ CLOSED 2026-05-22
- **Origin:** 5c code-review M4.
- **Scope:** Review DI lifetime (Scoped vs Singleton) for `IWorkflowGraphValidator`.
- **Resolution:** Shipped — `WorkflowApplicationModule.cs:29` flipped Scoped → Singleton. Validator is stateless: only `static readonly` HashSets for `PermCodes` + NotSupported clause modes, zero ctor deps, all methods stateless. WF.App 533/533 + WF.Infra 79/79 GREEN.

### ~~P6-DEFER-10 (5c-L-NEW-2, T4 P5, ~10min): Phase-doc carry-fwd housekeeping~~ ✅ CLOSED 2026-05-22
- **Origin:** 5c code-review L-NEW-2.
- **Scope:** Tidy carry-fwd notes in `phase-05c-design-time-validation.md`.
- **Resolution:** Shipped — added "Post-Closure Carry-Forward Status" subsection to phase-05c doc summarizing M3/M4/L-NEW-1/L-NEW-2/L-NEW-3/L-NEW-4 final status.

### ~~P6-DEFER-11 (5c-L-NEW-4, T4 P5, observational): Tracking SELECT round-trip perf~~ ✅ CLOSED-OBSERVATIONAL 2026-05-22
- **Origin:** 5c code-review L-NEW-4.
- **Scope:** Profile 2 extra tracking SELECT round-trips on cold ToggleActive path.
- **Resolution:** Observational close — code-review memory confirms "negligible per code-review" (cold admin path); no production signal warranting optimization. Reopen on telemetry evidence.

### ~~FE-FU 4-A-A (carry-fwd from PR #114): role-permission matrix audit for `POST /api/v1/workflow/instances`~~ ✅ CLOSED-AUDIT-CLEAN 2026-05-22
- **Origin:** Phase 4-A-A invariant closure (PR #114 v2.137p92) introduced `WORKFLOW.INSTANCE.MANAGE` policy gate on `POST /api/v1/workflow/instances`. Carry-fwd flagged: FE consumers in non-admin contexts may need role-permission matrix updates.
- **Scope:** Audit FE callers + BE role-grant seeder for drift.
- **Resolution:** Audit clean — report `plans/reports/audit-260522-1530-fe-instance-manage-callers.md`. Zero FE consumers of `usePostApiV1WorkflowInstances` (orval-generated hook unused); FE PermCode registry (`workflow-permissions.ts`) does not track `INSTANCE.*` codes; BE seeder grants `WORKFLOW.INSTANCE.MANAGE` to SystemAdmin + Admin (`E2ETestDataSeeder.RolePermissions.cs:36-74`); GovOfficer correctly excluded (no `INSTANCE.View` either — not a regression). No hotfix needed.

---

## P7 Cleanup Batch Summary (2026-05-22 v2.137p94, post-PHR-V2)

Closed in single bundle (initial):
- **DEFER-5** audit-clean (no dead `IsFailed` branches found)
- **DEFER-6** title null-fallback shipped (2 spots, defensive fallback to `initialState.Name`)
- **DEFER-8** WONTFIX-cosmetic per YAGNI (no caller discriminates NotFound vs ValidationFailed)
- **DEFER-9** Singleton flip shipped (`IWorkflowGraphValidator`)
- **DEFER-10** phase-05c doc tidy shipped
- **DEFER-11** observational close (negligible perf)
- **FE-FU 4-A-A** audit clean (zero FE drift)

## P7 Cleanup Batch 2 — TDD slice (2026-05-22 v2.137p95)

Test-first additions covering remaining feasible cleanup items:
- **DEFER-3** deterministic Cancel race test shipped (`F7r_ConcurrentCancel_InterceptorForcesWinnerFirst`) — mirrors F7 Claim pattern on state-change handler. WF.Infra 80/80.
- **DEFER-7** canonical-uppercase DB-roundtrip proof shipped (`I8_Activate_WithMixedCasePermissionCode`) — seeds lowercase, asserts uppercase persisted. WorkflowGraphPublishValidation 4/4.

**Remaining P7 carry-fwd (3 items):**
- **P6-DEFER-1** (P3 strategic, ~1d): `WorkflowLinkPendingEvent` — needs product-owner sign-off (out-of-session)
- **P6-DEFER-2** (T3 P2, ~1h): WebAppFactory EF model-cache fix — test-infra change deferred (fatigue floor)
- **P6-DEFER-4** (T3 P2): I3/I4 BP HTTP integration tests — blocked by missing BP fixture (separate scope)

Build 0E/0W. Tests: WF.App 533/533, WF.Infra 80/80 (+1 F7r), WorkflowGraphPublishValidation 4/4 (+1 I8).

---

## ~~Phase-01 Cases→Workflow Tech Debt & Follow-ups~~ (OBSOLETE — Cases module removed 2026-05-01, PR #71)

- [x] **M1 (medium):** Comment fix in `CaseProcessDefinitionSeeder.cs:175` — `Activate()` doesn't validate initial-state count (misleading documentation) — **Done 2026-04-17** | Fixed seeder comment to clarify validation scope
- [x] **M2 (medium):** Add startup health check asserting `CaseProcess v1` exists post-seed — **Done 2026-04-17** | CaseProcessDefinitionHealthCheck (55 LOC), 3 tests, registered with Degraded policy; tag "ready"
- [ ] **L4 (low):** Verify `(DefinitionKey, Version, TenantId)` DB unique constraint before multi-replica rollout (Phase-05 concern)
- [ ] **Runbook:** Deploy Phase-00 MasterData seeder in existing prod envs to pick up new `ReturnedForRevision` row

---

## Proposed Directions (2026-04-12 Audit)

> Pick one direction to focus on. Items within each direction are ordered by priority.

### Direction A: Quick Wins — UX Polish (3-5d) — **COMPLETE** (A1+A2+A3, 2026-04-13)
Small enhancements that improve product quality before demo/deploy.
- [x] A1: Signature canvas responsive layout — 0.5d — **Done 2026-04-13** | 5 tests | `plans/260413-1457-direction-a-quick-wins/phase-01-signature-responsive.md`
- [x] A2: Views column drag-reorder (Forms) — 1d — **Done 2026-04-13** | 4 tests (3 base + 1 disabled) | `plans/260413-1457-direction-a-quick-wins/phase-02-views-drag-reorder.md`
- [x] A3: Workflow Instance Dashboard virtualization (TanStack Virtual v3.13.23) — 1.5d — **Done 2026-04-13** | 12 tests | `plans/260413-1457-direction-a-quick-wins/phase-03-workflow-virtualization.md`
- [x] A4: Elasticsearch facet aggregation — **REMOVED 2026-04-21** (Search module deleted; was feature of removed module. Contextual search now in per-module list pages)

### Direction B: New Feature — KPI Module (20d) → T5-01
Largest business-value feature in backlog. Presale doc available.
- See T5-01 below

### Direction C: Workflow Enhancements ✅ (17d total) → T5-08→13
Six improvements for Workflow engine: delegation, analytics, versioning, webhooks, audit export, batch.
- **COMPLETE** (2026-04-13) — all 6 items shipped
- See T5-08 through T5-13 below

### Direction D: SDK Package (2d) → T5-06
Package SharedKernel as NuGet for downstream projects.
- See T5-06 below

---

### ~~T3-BP-FORMMAP-BACKFILL (P3, NEW 2026-05-17 21:25 ICT, ~1h): BP form-map back-fill needs FormTemplatePublicId in BE detail Dto~~ ✅ CLOSED-AUDIT-CLEAN 2026-05-23

- **Discovered:** 2026-05-17 via v2.137p57 debug. `business-process-form-maps-view.tsx:175-194` back-fill maps GET's bigint `formTemplateId` → picker Guid `publicId` via `Number(f.id) === bigintId` — this has been broken since BP MVP P3 ship because the picker `FormTemplateDto.Id` IS the PublicId Guid (not the internal bigint), so `Number(<guid>)` is always NaN → lookup always returns undefined → existing form template selections appear empty on edit-view open. Pre-p55 the code was wrong-typed; post-p55 the types are honest but the back-fill is still impossible without an internal-id↔public-id mapping.
- **Fix (BE contract change):** Add `FormTemplatePublicId (Guid)` to `BusinessProcessStateFormDto` + `BusinessProcessTransitionFormDto` in `src/modules/businessprocess/.../DTOs/BusinessProcessDtos.cs`. Update `GetBusinessProcessByIdQueryHandler` JOIN to project the public id alongside the bigint. Then drop the broken FE back-fill block.
- **Workaround until fixed:** Users opening BP detail edit see empty form-template Select on existing rows and must re-select. New rows added in-session work normally.
- **Effort:** ~1h (BE 30min + FE cleanup 15min + arch test refresh 15min).
- **Resolution (2026-05-23 audit):** Already SHIPPED 2026-05-18 as **BUG2 fix** in the `v2.137p63-p66 BP-EDIT DEBUG CASCADE` (header rows §). Empirical re-grep at slice-start (memory invariant 260522-A):
  - **BE DTO** `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/DTOs/BusinessProcessDtos.cs:59,74` — `FormTemplatePublicId { get; init; }` present on both `BusinessProcessStateFormDto` + `BusinessProcessTransitionFormDto` with BUG2-fix xmldoc.
  - **BE handler** `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Queries/GetBusinessProcessById/GetBusinessProcessByIdQueryHandler.cs:75-91` — resolves via batched `IFormTemplateQueryClient.ResolveTemplatePublicIdsAsync(formTemplateIds, tenantId, ct)` (no N+1) then `with { FormTemplatePublicId = pid }` projection on both lists; mirrors `IWorkflowModuleClient.TryGetDefinitionPublicIdAsync` pattern below.
  - **FE consumer** `web/src/features/business-process/business-process-form-maps-view.tsx:75-92` — `makeStateRow`/`makeTransitionRow` bind directly via `source?.formTemplatePublicId ?? undefined`; broken `Number(f.id) === bigintId` block deleted (replaced by stub comment lines 181-184 documenting the BUG2 history).
- **Pattern:** 3rd row this week (FE-FU 4-A-A clean PR #114 / P6-DEFER-2 WONTFIX-YAGNI / this row) closed as stale via point-in-time backlog snapshot drift — re-affirms memory `phase-04a-a-invariant-closure-260522` 260522-A invariant: re-grep at slice-start before opening scope.
- **Status:** ✅ **CLOSED-AUDIT-CLEAN 2026-05-23** (zero code change this session; row was stale since 2026-05-18).

### ✅ T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION (P0 CRITICAL, NEW 2026-05-24 21:00 ICT, DONE 2026-05-25): Role-only read-side extension (claim/inbox predicate merge)

- **Discovered:** 2026-05-24 RT2-15 Phase 03 (B1 partial) ship — empirical slice-start scout caught read-side gap.
- **Status:** ✅ **DONE 2026-05-25 v2.137p110** (pending git-manager commit). Phase 01: `IIdentityModuleClient.GetUserRoleNamesAsync` Dapper + 4 unit + **5 real-DB integration tests** (advisor-driven; closes SQL-direct-only PQ-1 A semantic). Phase 02: repo merge `(Permission ∪ Role)`, claim/inbox wiring, 6 anti-tautology integration facts. Role-only; Group/OrgUnit deferred to RT2-15 phases 02/04 when product Qs ratify.
- **Close-note:** PQ-1 A direct-only semantic locked by `IdentityModuleClientRoleNamesTests.GetUserRoleNamesAsync_UserWithRoleViaGroupOnly_ExcludesRoleName` (real-DB) + workflow I1/I2 anti-tautology integration facts. Closes 260523-D fail-CLOSED-with-reader + closes 260524-G privilege-escalation trap empirically.
- **Shipped:** 2 files extended + 5 test files extended + 1 new integration test file. Tests: Identity 146/146 (+4 unit, +5 integration), Workflow.App 564/564 (+2), Workflow.Infra 84/84, Workflow.Integration 47/50 (3 pre-existing HEAD baseline fails tracked separately). Arch 105/105. Build 0E/0W.
- **Plan:** `plans/260524-2122-readside-extension-role-only/` (plan.md + phase-01 + phase-02 — all complete).
- **Unblocks:** `RT2-15-PHASE-03-FU` (flip BLOCKED → PENDING); phases 02/04 of RT2-15 when product Qs clear.
- **Linked:** [[260523-D]] feeder-without-reader inverse INVARIANT origin; [[phase-savegraph-assignment-260523]] RT2-15 context.

### ✅ T3-WF-RT2-15-TEST-DEBT-260525 (T3 / P2, NEW 2026-05-25, CLOSED 2026-05-25 v2.137p111, ~1h25min actual): Fix 3 pre-existing Workflow integration test failures from RT2-15 Phase 01 + 03 B1 ships

- **Status:** ✅ CLOSED v2.137p111 (2026-05-25). 47/50 Workflow.Integration PASS / 0 FAIL / 3 SKIP. Net +3 passing from baseline.
- **Discovered:** While shipping T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION Phase 01+02 (2026-05-25); verified via `git stash` baseline run that these 3 failures existed at HEAD `ca16d466` (not introduced by current slice).
- **Resolution (3 tests):**
  - **(a) WorkflowGraphPublishValidationTests.I7_Activate_WithNotSupportedClauseMode_*** — FIXED. Backlog row mis-stated mode (said ByRole, actual seed was `PreviousHandler` mode=2 which is now supported). Swapped seed to `ByGroup` (mode 5, still in `_notSupportedModes`); helper renamed `SeedDefinitionWithPreviousHandlerClauseAsync` → `SeedDefinitionWithByGroupClauseAsync`. R4 422 assertion shape preserved.
  - **(b) WorkflowDefinitionSaveGraphWithAssignmentTests.I_SaveAct_A4_PreviousHandlerClause_*** — FIXED. Inline clause `mode = 2 / refsJson = null` → `mode = 5 / refsJson = """["GROUP_TIEP_NHAN"]"""`. Test renamed to `I_SaveAct_A4_ByGroupClause_Returns422_NoRowsPersisted`.
  - **(c) WorkflowInstanceConcurrencyTests.SimultaneousRace_*** — SKIPPED + filed standalone T3-WF-CONCURRENCY-FLAKE (P3) below. User gate ratified Option α (skip + file) over Option β (deterministic WinnerFirstInterceptor reuse ~30-60min) to keep cycle within fatigue-floor.
- **Doc fold:** `src/modules/workflow/AqtCoreFW.Workflow.Domain/Enums/AssignmentClauseMode.cs:3-10` xmldoc updated (PreviousHandler + ByRole moved Deferred → RT2-15-shipped sections).

### ✅ T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP (P0 CRITICAL, NEW 2026-05-25, DONE 2026-05-25 v2.137p113, ~1h actual): Read-side extension for ByGroup (mirror ByRole shipped v2.137p110)

- **Status:** ✅ **DONE v2.137p113** (single-SHA per 260523-D). NEW `IIdentityModuleClient.GetUserGroupCodesAsync` Dapper direct-membership lookup + 4 unit + 5 real-DB integration facts. Workflow repo `IsPoolEligibleAsync` + `GetInboxAsync` extended with `groupCodes` arm; 4 anti-tautology Workflow.Integration facts (claim/inbox member-vs-non-member) in new `WorkflowTaskGroupClaimIntegrationTests.cs`. Closes 260524-F gate-chain for ByGroup.
- **Tests:** WF.App 574/574 (+2) / Identity.Infra 154/154 (+4) / Identity.Integration 211/212 PASS (+5 — 1 pre-existing `SignatureConfigApiTests` failure confirmed at clean HEAD `d96ffe42`, NOT introduced) / WF.Integration 56/60 (+4 PASS, 4 SKIP unchanged) / Arch 105/105 / Build 0E/0W.
- **Cycle:** ~1h actual vs 3-5h estimate (260522-O pattern-mirror 3-5× discount honored). Plan `plans/260525-1640-readside-extension-group/`.
- **Code-review:** APPROVE-WITH-NITS 9.3/10. 3 Low + 1 Nit all twin-inherited (carry-fwd to optional `WF-CANDIDATEINDEX-CANONICALIZATION-HARDENING` follow-up).
- **Unblocks:** Phase 04 ByOrgUnit (mirror pattern), production ByGroup seeding (gate-chain CLOSED).

### ~~T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP-OLD~~ (P0 CRITICAL, NEW 2026-05-25, ~3-5h): Read-side extension for ByGroup (mirror ByRole shipped v2.137p110)

- **Discovered:** 2026-05-25 RT2-15 Phase 02 ByGroup slice completion. Phase 02 ships predicate-pool producer (Option A ratified) + Q2 Save-validation hop, but workflow claim/inbox read-side still filters `PrincipalType=Permission + Role` only (per T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION ByRole v2.137p110).
- **Gap:** ByGroup-pooled tasks unclaimable + inbox-invisible until this row ships.
- **Scope:** Extend `WorkflowTaskRepository.IsPoolEligibleAsync` (Permission + Role arms at lines 119-128) to add ByGroup arm. Extend `GetInboxAsync` (lines 208-215) to merge ByGroup candidates via existing `IIdentityModuleClient.ResolveActiveUsersInGroupsAsync` (already wired at Phase 02 v2.137p112 for forward-compat but unused; read-side activation unlocks it).
- **Implementation:** Single batched `IIdentityModuleClient.ResolveActiveUsersInGroupsAsync` call per query covering all ByGroup predicates in `WorkflowTaskCandidateIndex` rows. Union merge into existing Permission + Role result sets. Integration test validates end-to-end: save + activate ByGroup clause, create instance, assert task appears in `GetInboxAsync` response.
- **Effort:** ~3-5h (pattern established by ByRole read-side; ByGroup follows same shape — 2 queries modified, module-client call added, 2-3 integration tests).
- **Status:** PENDING. PRODUCTION TENANTS MUST NOT seed ByGroup assignments until this row closes (gate: WF.Definition docs + seeder guards).
- **Gate-chain (INVARIANT 260524-F):** Phase 02 ByGroup producer (SHIPPED v2.137p112) gates on this row to unblock production use. Phase 04 ByOrgUnit will mirror same pattern. Phase 05 cross-mode validator includes all 3 (Permission+Role+Group+OrgUnit).
- **Linked:** [[phase-02-by-group]] ship notes; [[phase-savegraph-assignment-260523]] 260523-D feeder-without-reader INVARIANT; [[rt2-15-scaffold-and-q-fold-260523]] RT2-15 roadmap.

> **RT2-15-PHASE-04-FU** — DONE 2026-05-26 (v2.137p115) `188fbd35`+`2c08ec0b` → archived to `completed.md`. Flagged carry-fwd: `BtcOrphanPurgerTests` column-drift pre-existing (separate row, not blocking).

> **RT2-15-PHASE-04-FU-M1** — DONE 2026-05-26 (v2.137p116) `8318d538` → archived to `completed.md`. C3 `CrossMode_SharedRefId_PrincipalTypeGatesIndependentlyOfRefId` shipped within Phase 05 slice A.

### ✅ T3-05: FE smoke-phase5 test failures (4 tests FIXED)
- **Scope:** `smoke-phase5-pages.test.tsx` — ChatPage `useInfiniteQuery` mock fix, JobMonitorPage assertion + i18n keys
- **Discovered:** 2026-04-13 (unrelated to RBAC work, pre-existing on main)
- **Impact:** FE suite 625/631 (all smoke tests green); blocks clean test reports — ✅ **RESOLVED**
- **Effort:** 1h
- **Completed:** 2026-04-13 | **Plan:** `plans/260413-1144-t3-05-fe-smoke-phase5-fixes/`
- **Files:** smoke-phase5-pages.test.tsx, en.json, vi.json | **Tests:** 29/29 pass (was 28/32) | **Code review:** APPROVED

---

### T4-04-B: Adjacent validator gaps (follow-up to T4-04) ✅
- **Effort:** 0.25d (completed 2026-04-14)
- **Status:** ✅ COMPLETE
- **Context:** Discovered during T4-04 Phase-02 code review — two gaps outside original scan inventory.
- **Scope (patched):**
  - `web/src/features/integration/partner-list-page.tsx:123` `name` field → `max:200` + `validateFirst` + `// Mirrors BE CreatePartnerCommandValidator.cs` comment
  - `web/src/features/templates/template-list-page.tsx:150` `code` field → `max:100` + `validateFirst` + `// Mirrors BE CreateDocumentTemplateCommandValidator.cs` comment
- **Reuses:** `common.validation.maxLength` i18n key (from T4-04)
- **TypeScript:** clean | **Playwright:** still DEFERRED (BE port 5002 offline — inherits from T4-04)
- **Discovered:** 2026-04-14 (T4-04 Phase-02 code review) | **Completed:** 2026-04-14

---

### T5-08: Workflow delegation/proxy ✅
- **Effort:** 3d (delivered on schedule)
- **Direction:** C
- **Completed:** 2026-04-12 (commit `073529f7`)

### T5-09: Workflow dashboard analytics ✅
- **Effort:** 3d (delivered on schedule)
- **Direction:** C
- **Completed:** 2026-04-12 (commit `fc829071`)

### T5-10: Workflow version migration ✅
- **Effort:** 7d (delivered in 2 sessions: 4d + 3d, elevated from 5d due to approval workflow + concurrency guards)
- **Direction:** C
- **Completed:** 2026-04-13 (5 work chunks: A+B+C committed, D+E pending)

### T5-11: Workflow webhook on transition ✅
- **Effort:** 2d (delivered in 1d + 2h)
- **Direction:** C
- **Completed:** 2026-04-12

### T5-12: Workflow audit export ✅
- **Effort:** 2d (delivered on schedule)
- **Direction:** C
- **Completed:** 2026-04-12

### T5-13: Workflow batch transition ✅
- **Effort:** 2d (delivered on schedule)
- **Direction:** C — subtotal T5-08→13: 17d
- **Completed:** 2026-04-12

### T5-14: ~~Signature PKI real implementation (TSA/OCSP/CRL)~~ — OBSOLETE 2026-05-01
- **Status:** OBSOLETE — Signature module REMOVED 2026-05-01 (PR #69, commit b855ddc6). All in-process PKI services (Rfc3161TimestampService, OcspCertificateValidator, CrlRefreshJob, StubHsmAdapter) deleted with module. TSA/OCSP/CRL delegated to external VN CA provider (handled by QĐ 742 phase-05b external adapter). See `docs/journals/journal-260501-1007-signature-module-removal.md`.

### T5-17: Virtual table full ARIA grid keyboard navigation ✅
- **Effort:** 0.5d estimate → actual ~0.5d (on schedule)
- **Status:** ✅ COMPLETE — 2026-04-14
- **Scope:** Full keyboard nav (ARIA grid pattern) for `instance-monitoring-dashboard.tsx` virtual table; previously partial (checkbox tabIndex only)
- **Delivered:**
  - `use-workflow-grid-keyboard-nav.ts` (80 LOC) — 11 keyboard bindings, roving tabIndex, virtualizer scroll sync
  - `workflow-grid-row.tsx` (156 LOC) — extracted row renderer (LOC budget)
  - `workflow-grid-constants.ts` (5 LOC) — `GRID_COLS`/`ROW_HEIGHT`/`COL_COUNT`
  - `instance-monitoring-dashboard.tsx` (258 → 237 LOC) — full ARIA grid semantics (role=grid/row/gridcell/columnheader, aria-rowcount/colcount/rowindex/colindex/selected/multiselectable)
  - 2 i18n keys en+vi: `workflow.instance.gridAriaLabel`, `rowAriaLabel`
  - 97/97 workflow tests green (35 hook + 32 dashboard + others)
  - Code review: 8/10 APPROVED_WITH_MINOR — all findings addressed
- **UX decision locked:** Option A — Tab-into-grid lands on cell (0,0), skip sticky header select-all (WAI-ARIA spec compliant, regression-tested TC-FE-WF-DASH-011)
- **Deferred:** Playwright E2E (BE port 5002 offline — same blocker as T4-04/T5-19)
- **Discovered:** 2026-04-13 (Direction A A3 session) | **Completed:** 2026-04-14
- **Plan:** `plans/260414-1135-t5-17-virtual-grid-keyboard-nav/`

### ✅ T5-20: Workflow designer — persist drag positions + draw-to-connect
- **Effort:** 1d estimate → actual ~1d (completed 2026-04-15)
- **Status:** ✅ COMPLETE + Follow-ups (T5-20-FU1 + T5-20-FU2) ✅ COMPLETE 2026-04-15
- **Scope:** R7 + R11 from plan `260413-2250-workflow-designer-sync-layout`:
  - **R7 — Persist drag positions:** `positionMap` via `useRef<Map>` in parent; commits on drag-end via `onPositionCommit` callback; cleared on undo/auto-layout.
  - **R11 — Wire draw-to-connect:** `onCreateTransition` callback — draw-edge opens `TransitionEditorModal` pre-filled with from/to; modal save → `crypto.randomUUID()` temp ID → pushed to `transitions` state.
- **Delivered (T5-20 + FU1 + FU2):**
  - `react-flow-canvas.tsx` (198/200→152 LOC) — new props `positionMap`, `onPositionCommit`, `onCreateTransition`; updated `handleNodesChange`, `handleConnect`; helpers extracted to `react-flow-canvas-helpers.ts` (61 LOC)
  - `definition-visual-designer-tab.tsx` (193/200→148 LOC) — `positionMapRef`, `handlePositionCommit`, `transitionModalOpen`, `transitionPreFill`, `handleCreateTransition`, `handleTransitionSave`; new `<TransitionEditorModal />` instance; handlers extracted to `use-visual-designer-handlers.ts` (102 LOC)
  - `transition-editor-modal.tsx` (FU1) — added `initialFields?: { fromStateId?: string; toStateId?: string }` prop; removed unsafe cast at `definition-visual-designer-tab.tsx:385`
  - `workflow-graph-input-utils.ts` (52 LOC) — extracted helpers
  - `react-flow-canvas-helpers.ts` (FU2, 61 LOC) — extracted builder fns; re-export for 6 T5-20 tests
  - `use-visual-designer-handlers.ts` (FU2, 102 LOC) — extracted handlers; re-export `ReactFlowCanvasProps` for test compat
  - `react-flow-canvas-persist-and-connect.test.tsx` — 6 new tests (3 R7 + 3 R11); 730/730 total pass
- **Code review:** T5-20: 8/10 DONE_WITH_CONCERNS (M1 + M2 → FU1/FU2, now resolved) | FU1+FU2: approved
- **Branch:** `fix/t5-20-followups` (pending commit)
- **Playwright E2E:** deferred — port 5002 offline (same blocker as T4-04/T5-17)
- **Plan:** `plans/260415-0838-t5-20-workflow-designer-persist-and-connect/`
- **Discovered:** 2026-04-13 red-team round 2 | **Completed (T5-20):** 2026-04-15 | **Completed (FU1+FU2):** 2026-04-15

### ✅ T5-20-FU1: Refactor TransitionEditorModal initialFields prop (M1 from T5-20 code review)
- **Effort:** 0.5d (completed 2026-04-15)
- **Status:** ✅ COMPLETE
- **Scope:** Refactor `TransitionEditorModal` to accept `initialFields?: { fromStateId?: string; toStateId?: string }` prop; remove unsafe `as WorkflowTransitionDto | null` cast in `definition-visual-designer-tab.tsx:385`
- **Delivered:**
  - `transition-editor-modal.tsx` — added `initialFields?: { fromStateId?: string; toStateId?: string }` prop; pre-fill form when provided
  - `definition-visual-designer-tab.tsx` — removed cast at line 385; pass `initialFields` to modal directly (backward compat: prop `transition` still optional; caller `definition-transitions-tab.tsx` unchanged)
  - Tests: 730/730 pass (no regression)
  - Code review: approved (M1 concern resolved)
- **Priority:** Low — tech debt / type safety; no user-facing impact
- **Discovered:** 2026-04-15 (T5-20 code review M1) | **Completed:** 2026-04-15
- **Branch:** fix/t5-20-followups (pending commit)

### ✅ T5-20-FU2: LOC ceiling — extract before next touch (N1 from T5-20 code review)
- **Effort:** 0.25d (completed 2026-04-15)
- **Status:** ✅ COMPLETE
- **Scope:** `react-flow-canvas.tsx` (198/200→152) and `definition-visual-designer-tab.tsx` (193/200→148) exceeded LOC ceiling; extracted helpers before next touch
- **Delivered:**
  - `react-flow-canvas.tsx` — 198→152 LOC (46-line reduction); extracted `buildNodes`, `buildEdges`, re-export pattern to `react-flow-canvas-helpers.ts` (61 LOC); 6 T5-20 tests reuse exports
  - `definition-visual-designer-tab.tsx` — 193→148 LOC (45-line reduction); extracted handlers + side-effects into `use-visual-designer-handlers.ts` (102 LOC); re-export `buildNodes`, `buildEdges`, `ReactFlowCanvasProps` for test compatibility
  - Tests: 730/730 pass (zero regression; 6 T5-20 tests unaffected)
  - Code review: approved (N1 concern resolved)
- **Priority:** Low — hygiene; gate satisfied for next feature touching these files
- **Discovered:** 2026-04-15 (T5-20 code review N1) | **Completed:** 2026-04-15
- **Branch:** fix/t5-20-followups (pending commit)

### T5-21: tsc strict check for test files (`src/test/**`) — config landed (split) ✅
- **Effort:** 0.25d → actual ~0.3d for config-only landing (2026-04-14)
- **Status:** ✅ COMPLETE (family fully closed 2026-04-14)
- **Landed:** `web/tsconfig.test.json` (extends app, include `src/test`), `package.json typecheck:test` script. All 62 errors resolved across T5-21-B + T5-21-C; CI gated on both prod + test typecheck.
- **Plan:** `plans/260414-1359-t5-21-tsc-test-strict/`

### T5-21-B: Fix 29 prod typecheck errors + unblock CI build ✅
- **Effort:** 0.5-1d (delivered on schedule)
- **Priority:** CRITICAL — resolved
- **Status:** ✅ COMPLETE — 2026-04-14
- **Commit:** `f2db41e5` — 23 files, -44/+29 lines
- **Delivered:** All 7 batches (A→G) fixed; `ci.yml:252` flipped to real gate (`-p tsconfig.app.json`); `npm run build` + `npx tsc -b` exit 0; Vitest no regression (1 pre-existing `LAYOUT.siderWidth` failure unchanged)
- **Note:** BE `ASPDEPR005` fix (`c80bb22f`) pre-dated this work — T5-21-D cancelled as redundant. 29 FE errors were real tech debt independent of BE.
- **Follow-up (TBD):** `package.json typecheck` script + `abac-rules-page.tsx` dead `Form.Item` removal (code-reviewer major finding)
- **Prerequisite:** T5-21 config landed ✅
- **Discovered:** 2026-04-14 (T5-21 baseline) | **Completed:** 2026-04-14
- **Plan:** `plans/260414-1437-t5-21-b-prod-tsc-fix/`

### T5-21-C: Fix 44 test typecheck errors + wire CI test typecheck ✅
- **Effort:** 0.25-0.5d → delivered on schedule (2026-04-14)
- **Priority:** HIGH (after T5-21-B)
- **Status:** ✅ COMPLETE — 2026-04-14
- **Commits (branch fix/t5-21-c-test-tsc-fix, not yet on main):**
  - `bb555c1e` — resolve 44→0 test TSC errors + wire CI typecheck:test gate across 19 test files + ci.yml
  - `d29a55e6` — M1 follow-up: type notification-bell `items: []` as `NotificationDto[]`
- **Delivered:** 7 error clusters fixed (unused imports, tenantId prop removal, FormTemplateDto.version, mock signature casts, never-typed mocks, AxiosResponse shape, org-units narrowing); `npx tsc --noEmit -p tsconfig.test.json` exits 0; CI `Type check (tests)` step wired after prod typecheck in ci.yml
- **Code review:** 9.6/10, 0 critical, auto-approved
- **Prerequisite:** T5-21-B ✅ | **Discovered:** 2026-04-14 (T5-21 baseline)
- **Plan:** `plans/260414-1545-t5-21-c-test-tsc-fix/`

### T5-22: Pre-existing FE test failures (8 tests, 4 files) ✅ COMPLETE
- **Effort:** 0.5d estimate → actual ~0.5d (completed 2026-04-14)
- **Status:** ✅ COMPLETE
- **Actual count:** 4 failures (not 8 per original entry) — `notification-list-page.test.tsx` self-healed by T5-21-C mock type fixes
- **Context:** T5-17 tester run revealed 8 failures unrelated to T5-17 scope (confirmed via git diff). Files:
  - `app-smoke.test.tsx` — LAYOUT siderWidth assertion drift (`220` → `256`, Nyasha One redesign `22df356d`)
  - `api-client.test.ts` — baseURL test picked up dev `.env.local` `http://localhost:5001/api/v1`; fixed via `vi.hoisted` env stub
  - `smoke-remaining-pages.test.tsx` (2 tests) — missing `vi.mock('@/features/roles/roles-api')` → "No data" table; root cause was roles-api mock absent (not i18n — original diagnosis incorrect)
  - `notification-list-page.test.tsx` — NOT failing at T5-22 baseline; already resolved by T5-21-C
- **Delivered:** 3 test files modified, 0 production code changes; suite 65 files / 724 tests / 0 fail
- **Code review:** 9.7/10, 0 critical, auto-approved
- **Branch:** `fix/t5-22-vitest-regressions`
- **Discovered:** 2026-04-14 (T5-17 validation) | **Completed:** 2026-04-14
- **Plan:** `plans/260414-1744-t5-22-vitest-regressions/`

### T5-18: Batch transition confirm dialog for >20 selections ✅
- **Effort:** 0.5d estimate → actual ~0.3d (completed 2026-04-14)
- **Status:** ✅ COMPLETE
- **Scope:** Count confirmation guard in `WorkflowBatchActionToolbar` when selectedIds.length > 20 — prevents accidental mass transitions
- **Delivered:**
  - `workflow-batch-action-toolbar.tsx`: `CONFIRM_THRESHOLD=20` + `handleActionClick` intercepts với `Modal.confirm` (title+content+okText+cancelText); fallback straight-open khi count ≤ 20; hard cap MAX_BATCH=50 vẫn giữ (button disabled)
  - i18n: 3 keys mới vi/en — `workflow.batch.confirmTitle`, `confirmContent` (với `{{count}}`), `confirmOk`
  - TDD: new `workflow-batch-action-toolbar.test.tsx` với 4 tests (count≤20 skip confirm, count>20 spawn confirm + content assertion, onOk opens modal, onCancel keeps closed). Pattern `vi.spyOn(Modal, 'confirm')` + capture callback — tái sử dụng cho future imperative dialog tests
- **Tests:** 4/4 new pass (workflow suite 42/42 green, zero regression)
- **Discovered:** 2026-04-13 (Direction A A3 session) | **Completed:** 2026-04-14

### T5-19: Orval codegen cho ApiResponse error envelope ✅
- **Effort:** 1-1.5d estimate → actual ~0.75d (completed 2026-04-14)
- **Status:** ✅ COMPLETE
- **Context:** 2026-04-13 debug — FE `BackendApiError` khai `field?` nhưng BE `ApiResponse.cs` gửi `property`. Contract drift dẫn đến FE nuốt message 422.
- **Finding (Phase-01 audit):** BE OpenAPI đã emit đầy đủ `ApiError` (5 fields: code, message, detailVi, traceId, property) + `ApiMeta` + `ApiResponseOf*`. Generated types đã sẵn trong `web/src/api/generated/aqtCoreFWApiV1.schemas.ts`. Root cause không phải "chưa generate" mà là "viết tay song song không import generated".
- **Delivered:**
  - Phase-01/02 (audit + orval.config verify): BE schema correct, zero config change needed
  - Phase-03 (FE migration): `api-error.ts` swap → import `GeneratedApiError`, rename FE export `ApiError` → `NormalizedApiError`; delete `BackendApiError` + `BackendEnvelope` (dead code); update `query-config.ts` + `index.ts` rename; cleanup dead `ApiError` + `ApiResponse<T>` trong `shared/types/api.ts` (bonus — 0 importers, carried old `field?` drift)
  - Phase-04 compile-time portion: new `api-error-contract.test.ts` — TypeScript assertion `ConsumedApiErrorFields extends keyof GeneratedApiError` fails typecheck nếu BE rename/remove field consumed
  - `traceId` field (previously silent omission) nay available on generated type
- **Tests:** 11/11 green (10 normalization + 1 contract)
- **Deferred:** Phase-04 runtime OpenAPI snapshot (cần BE port 5001 online — same infra blocker as T4-04 Playwright)
- **Benefit realized:** Ngừa lớp bug "FE type sai shape" — next contract regression catches at compile time
- **Discovered:** 2026-04-13 | **Completed:** 2026-04-14

### ✅ T4-06: Delete MasterData Monolithic Project — COMPLETE (2026-04-16)
- **Effort:** 2h actual (vs 2-2.5h estimate)
- **Status:** ✅ COMPLETE
- **Scope:** Deleted `src/modules/masterdata/AqtCoreFW.MasterData/` (41 files, -2226 lines); migrated 11 unit tests to layered namespaces (Domain/Application/Infrastructure split); corrected `docs/dependency-map.md` §MasterData (was "service-based, no CQRS")
- **Commits** (branch `chore/t4-06-delete-masterdata-monolithic`):
  - `1331ad8c` — test(masterdata): migrate unit tests from monolithic to layered namespaces
  - `ca1d4b3e` — chore(masterdata): delete monolithic project
  - phase-03 pending commit
- **Red-team:** 2 rounds (Round 1: 12 findings resolved; Round 2: 12 more resolved incl. RT-R2-01 critical count error)
- **Verification:** 56/56 unit tests pass Debug + Release; FQN diff 0 lines; Release builds green 0 warnings; 6 namespace greps clean
- **Plan:** `plans/260416-1036-t4-06-delete-masterdata-monolithic/`
- ~~**Unblocks:** Phase 00 of Cases→Workflow refactor (`260415-1737-cases-workflow-refactor`)~~ (OBSOLETE — Cases module removed 2026-05-01, PR #71)

### ✅ T4-05: Fix IMemoryCache cross-pod consistency (per-module strategy) — COMPLETE (Phase 01 v2.120 · Phase 02-04 + FU2-FU5 v2.121-v2.123)
- **Effort:** ~7.5h total (all phases shipped)
- **Priority:** Medium — tech debt, latent multi-pod consistency bug
- **Status:** ✅ **DONE** — backlog parent header was stale (claimed Phase 02-04 🔵 BACKLOGGED); git proves all shipped 2026-04-20.
  - **Phase 01 ✅ shipped 2026-04-19 (v2.120, direct push to `main`; commits `b8e8f5a3` MailKit CVE + `ca4a7954` feature + `4f31fbb6` docs/journal)** — ABAC Two-Tier cache migration + cross-pod `abac-invalidate` pub/sub + B.1 tenant filter. Integration 103/103 pass.
  - **Phase 02 ✅ `6a04f8ff`** — deleted dead `DictionaryPublishedCacheInvalidationConsumer` + `DictionaryPublishedEvent` + publisher call (no-op: no cache producer ever set `masterdata:dict:*`).
  - **Phase 03 ✅ `347ef36a`** — `RulesEngineCacheSubscriber : BackgroundService` + Redis channel `rules:cache-invalidate` (compiled LINQ delegates not JSON-serializable → keep `IMemoryCache`, add pub/sub fan-out only). Caller wired.
  - **Phase 04 ✅ `14eec5af`** — 3 integration tests prove 2-pod round-trip + graceful Redis-down degradation; `docs/dependency-map.md` §5b/5c (Hosted Services + Redis Pub/Sub Channels registry). v2.121 finalize `17cd8b4d`.
  - **FU2 ✅ `d979b81d`** ABAC write-path defence-in-depth (9 tests); **FU3 ✅ `a089e714`** UpdateRule invalidate compiled engine cache; **FU4 ✅ `4a1b6e5f`** DeleteAbacRule audit log; **FU5/5.1/5.2 ✅ `f7b5604b`/`87af3d8e`/`67083645`** Rules tenant-isolation hardening (v2.123).
  - Plan: `plans/260420-0859-t4-05-phase-02-03-04-fu2/` (executed). Journal: `docs/journals/journal-260420-0859-t4-05-completion.md`.
- **T4-05-FU (AbacCacheRefreshService periodic safety net) — ✅ COMPLETED 2026-05-13** (R6 closure; commit pending).
- **Context:** Production Helm `replicaCount: 2` + HPA `maxReplicas: 10`. Modules using `IMemoryCache` directly create stale window between pods.
- **Scout findings (2026-04-16):** Original assumption "migrate all 3 to ICacheService" was WRONG. Per-module strategy required:

#### Phase 01: Identity/AbacAuthorizationHandler — REAL migration (~3h)
- **Type:** Two-Tier migration
- **File:** `src/modules/identity/AqtCoreFW.Identity.Application/Authorization/AbacAuthorizationHandler.cs`
- **Data:** `List<AttributeRule>` — likely JSON-serializable (verify navigation properties)
- **Changes:** `IMemoryCache` → `ICacheService`; `GetOrCreateAsync` → manual Get/Set pattern; update existing `IAbacCacheInvalidator` to use `RemoveAsync`/`RemoveByPrefixAsync` instead of `Compact(1.0)`
- **Risk:** Security decision divergence between pods (5min stale window currently)

#### Phase 02: MasterData/DictionaryPublishedCacheInvalidationConsumer — DEAD CODE, delete (~30min)
- **Type:** Cleanup, NOT migration
- **Discovery:** Invalidation consumer removes key `masterdata:dict:{code}` — BUT no query handler SETS this key. `GetDictionariesQueryHandler` uses Dapper directly, zero cache. Consumer comment "matches keys set by dictionary query handlers" is FALSE.
- **Decision:** Delete `DictionaryPublishedCacheInvalidationConsumer.cs` + `DictionaryPublishedEvent.cs` (YAGNI — no performance problem requiring dictionary cache). Optional future: add producer cache if Dapper reads become bottleneck.

#### Phase 03: Rules/RulesEngineEvaluator — CAN'T migrate, use pub/sub (~3h)
- **Type:** IMemoryCache + Redis pub/sub (T4 tier pattern)
- **File:** `src/modules/rules/AqtCoreFW.Rules.Infrastructure/Engine/RulesEngineEvaluator.cs`
- **Blocker:** `RulesEngine.RulesEngine` stores compiled delegates + LINQ expressions — **NOT JSON-serializable**. Can't write to Redis via `ICacheService` (Two-Tier uses `JsonSerializer.Serialize`). IMemoryCache is the RIGHT choice here.
- **Solution:** Keep `IMemoryCache` for engine storage; add Redis pub/sub channel `rules:cache-invalidate` for cross-pod L1 eviction. Reuse pattern from `SystemParamService`/`FeatureFlagSubscriber` (`ff-invalidate` precedent).
- **Changes:** Inject `IConnectionMultiplexer`; `InvalidateCache(ruleSetId)` publishes to channel; new `RulesEngineCacheSubscriber : BackgroundService` listens + clears local IMemoryCache.

#### Phase 04: Tests + dep-map update (~1h)

- **Excluded:** `Collaboration/PresenceCacheService` — best-effort by design (SignalR compensates).
- **Excluded:** `Identity/AbacCacheInvalidator` — will be updated as part of Phase 01.
- **Unresolved questions (surface to user at execution time):**
  1. ABAC `AttributeRule` entity — does it have navigation properties? If yes → wrap in DTO before serialize.
  2. MasterData — keep `DictionaryPublishedEvent` publisher in `PublishDictionaryCommandHandler`? (Event fired but no consumer = YAGNI candidate.)
  3. Rules — channel name convention `rules:cache-invalidate` vs `rules-invalidate`? (Alignment with existing `ff-invalidate`.)
- **Discovered:** 2026-04-16 (cache architecture review during Cases→Workflow refactor planning) | **Scout revised:** 2026-04-16

#### T4-05-FU: AbacCacheRefreshService periodic safety net (~1h) ✅
- **Type:** Follow-up to T4-05 Phase 01 — belt-and-suspenders for subscriber-fail residual risk
- **Status:** ✅ **COMPLETED 2026-05-13 (R6)** — `AbacCacheRefreshService : BackgroundService` shipped in `Identity.Infrastructure/Authorization/`. 15-min interval sweep via `ICacheService.RemoveByPrefixAsync("sys:abac:")` + `RemoveByPrefixAsync("t:*:abac:")` (Redis KEYS glob matches mid-pattern `*`). Mirrors `FeatureFlagRefreshService` precedent (KISS — no initial sweep, hardcoded interval per YAGNI). Wired alongside `AbacCacheSubscriber` in `InfrastructureRegistration.cs`. 2 unit tests pass (eviction order + exception propagation to background loop). Build 0E, Identity.Infrastructure.Tests pass.
- **Priority:** Low (R2 retry in Phase 01 closes 95% of subscriber-fail risk; this closed residual 5%)
- **Context:** In rare tail case, all 5 R2 retry attempts fail (~5s → 60s backoff, total ~2min), subscriber exits loop, and natural L1 10-min expiry becomes only recovery path. The periodic refresh service closes this residual window.
- **Discovered:** 2026-04-19 during T4-05 Phase 01 red-team walkthrough (Q-R3) | **Completed:** 2026-05-13

#### T4-05-FU2: ABAC cross-tenant write protection (command-handler validations) (~2h) ✅
- **Type:** Follow-up to T4-05 Phase 01 — defence-in-depth cho write-path (read-path đã fix qua B.1 inline repo filter).
- **Status:** ✅ **COMPLETED 2026-04-20** — `UpdateAbacRuleCommandHandler` injected ITenantContext + guard validates rule.TenantId == currentTenantId before mutate (log warning on mismatch). `AttributeRuleRepository` AddAsync/UpdateAsync guarded with 20 LOC validation. 9 unit tests (5 critical path + 4 positive controls). Session: Phase 04 v2.121 (commit pending, finalize → v2.122).
- **Priority:** Medium-High (security — cross-tenant write leak vẫn possible qua Create/Update command handlers).
- **Discovered:** 2026-04-19 during T4-05 Phase 01 red-team walkthrough

#### T4-05-FU3: UpdateRule invalidate compiled engine cache (~1h) ✅
- **Type:** Follow-up to T4-05 Phase 03 — close stale-cache gap for rule mutations (only ActivateRuleVersion wired in v2.121).
- **Status:** ✅ **COMPLETED 2026-04-20** — `UpdateRuleCommandHandler` injected `IRuleEvaluator`, calls `InvalidateCache(ruleSet.Id)` after `repository.UpdateAsync()`. Matches Phase 03 ActivateRuleVersion pattern. Execution order: load → mutate → persist to DB → evict L1+L2 cache + Redis pub/sub. Next eval rebuilds from DB. 6 tests (happy path cache invalidate + success/persist + RuleSet-null no-invalidate × 2). File: `UpdateRuleCommandHandlerTests.cs`. Session: v2.122 (commit pending).
- **Priority:** Medium (prevents stale-cache edge cases like disabled rules still in engine cache).
- **Discovered:** 2026-04-20 code-review M1 finding — `IRuleEvaluator.InvalidateCache` interface existed but had 0 callers besides ActivateRuleVersion (dead-pipe class).

#### T4-05-FU4: DeleteAbacRule audit log + defence-in-depth (~30min) ✅
- **Type:** Follow-up to T4-05 FU2 — symmetry + forensic audit trail.
- **Status:** ✅ **COMPLETED 2026-04-20** — `DeleteAbacRuleCommandHandler` injected `ITenantContext` + `ILogger`, emits `LogWarning` with caller `currentTenantId` when `rule is null` (cross-tenant attempt or already-deleted). Silent no-op preserved (`Result.Ok` for null) — idempotent delete, no tenant leak via response. `AttributeRuleRepository.GetByIdAsync` (B.1 filter) ensures tenant isolation on read before delete. 5 tests (happy path success/invalidate + rule-null idempotent/no-invalidate/log-warning). File: `DeleteAbacRuleCommandHandlerTests.cs`. Session: v2.122. Code-review note: M1 minor — log includes RuleId for identification but not currentTenantId (forensic parity with FU2); acceptable for v2.122, optional to add tenantId in log template.
- **Priority:** Medium (audit hygiene + defence-in-depth pattern consistency).
- **Discovered:** 2026-04-20 code-reviewer recommendation for FU2 symmetry.

#### T4-05-FU5: RuleSetRepository tenant filter + write-path defence-in-depth (~1h) ✅
- **Type:** Security hardening — Rules module parity with Identity module B.1 pattern.
- **Status:** ✅ **COMPLETED 2026-04-20 v2.123** — Repository inline filter + write guards + 3 handler defence-in-depth guards + 12 new tests (5 repo + 3 handler + 4 integration). Code-review 9.3/10 APPROVED_WITH_MINOR. M1/M2 deferred to follow-up backlog items.
- **Priority:** Medium-High (production multi-tenant — cross-tenant isolation P0 security critical).
- **Context:** `RuleSetRepository.GetByIdAsync` has NO tenant filter. Attacker guessing foreign RuleSetId could call `UpdateRule` against it. Identity module has B.1 tenant filter pattern (`AttributeRuleRepository.GetByIdAsync` filters `TenantId == currentTenant || TenantId == null`). Rules module lacks equivalent.
- **Scope (SHIPPED):**
  - Added inline filter in `RuleSetRepository.GetByIdAsync` with SystemAdmin bypass (read-path only).
  - Strict tenant validation in `AddAsync`/`UpdateAsync` (no bypass — prevents accidental mis-tagging).
  - Belt+suspenders tenant guards in 3 command handlers (CreateRuleSet, UpdateRule, ActivateRuleVersion).
  - 12 comprehensive tests: 5 repo unit (add/update/get cross-tenant), 3 handler unit (request/load validation), 4 integration (cross-tenant scenarios + SystemAdmin bypass).
- **Effort:** ~1h (follows established B.1 pattern from Identity module).
- **Discovered:** 2026-04-20 code-reviewer P1 flag (pre-existing issue, not regression).

#### ✅ **COMPLETED 2026-04-20 v2.125** — T4-05-FU5.1: Rules query handlers + module client tenant cross-check
- **Type:** Defence-in-depth for Rules read-path + cross-module client throw variant.
- **Status:** ✅ COMPLETED 2026-04-20 | 8 query/command handlers + module client with Pattern A/B guards. 17 new tests (16 scope + 1 M2 null-context fix post-review). Code-review 9.3/10 APPROVED_WITH_MINOR. T4-05 family complete.
- **Priority:** Medium-High — JWT-sourced TenantId trusted at API boundary, server-side re-validation provides defence-in-depth parity with write-path.
- **Context:** Before FU5.1, Rules query handlers read `request.TenantId` (from JWT) without cross-check against `ITenantContext.TenantId`. Creates asymmetry: write-path guarded (FU5), read-path unguarded. SystemAdmin read-bypass only, non-SystemAdmin rejected.
- **Delivered:**
  - **Query handlers (6 surfaces):** `GetRuleSetQueryHandler`, `ListRuleSetsQueryHandler`, `EvaluateRuleQueryHandler`, `SimulateRuleQueryHandler`, `ExplainRuleQueryHandler`, `GetDecisionTableQueryHandler` — all inject `ITenantContext`, validate `request.TenantId == tenantContext.TenantId || tenantContext.IsSystemAdmin`, reject with `ValidationError` (HTTP 400) on mismatch.
  - **Command handler (1 surface):** `RunTestCaseCommandHandler` (post-fetch variant) — loads parent RuleSet via FU5-hardened repo (inline filter is safety net). Null parent → NotFoundError.
  - **Module client (1 surface):** `InProcessRuleEngineModuleClient` (throw variant) — enforces tenant pre-call, throws `InvalidOperationException` on cross-tenant (internal invariant).
  - **Tests (17):** 8 test files with 2 tests per handler/client surface (happy path + cross-tenant). M2 fix (post-review): added null-context test to GetRuleSetQueryHandlerTests (R3 red-team commitment).
  - **Code-review findings:** M1 (RunTestCaseCommandHandler guard branch unreachable for non-SystemAdmin — documented, defence-in-depth preserved), M2 (null-context test missing — FIXED), M3 (NSubstitute generic fragility — low priority, tests validate logic correctly).
- **Files:** 8 test files (new), 7 src files (modified). Existing test file +1 test (null-context M2 fix).
- **Effort:** ~1h actual (follows established FU5/FU5.2 patterns).
- **Regression:** 0 failures across Rules.Application (46/46), Rules.Infrastructure (12/12), TenantIsolation (11/11), Rules.Integration (6/6).
- **Discovered:** 2026-04-20 code-reviewer M1 on FU5 delta (M1 escalated from FU5, M2 backlog commitment, M3 test robustness observation).

#### ✅ **COMPLETED 2026-04-20 v2.124** — T4-05-FU5.2: DecisionTableRepository tenant guards
- **Type:** Pattern parity — ported FU5 pattern to DecisionTable write-path.
- **Status:** ✅ COMPLETED 2026-04-20 | Repository inline filter + strict `AddAsync`/`UpdateAsync` guards (no admin bypass) + `IDecisionTableRepository.UpdateAsync` signature added (future-proof) + `CreateDecisionTableCommandHandler` Pattern A guard. 7 new unit tests (5 repo + 2 handler). Code-review 9.5/10 APPROVED — smaller blast radius vs FU5 baseline (9.3/10).
- **Priority:** Medium-High — DecisionTable IS `ITenantScoped`, write-path now guarded.
- **Context:** `DecisionTableRepository` lacked inline tenant filter + write guards. Same pattern Rules `RuleSet` had before FU5. Prod multi-tenant → P0 security.
- **Delivered:** 
  - Inline filter in `GetByIdAsync`: `IsSystemAdmin || TenantId == current` (read-path bypass, write-path strict).
  - Strict tenant validation in `AddAsync`/`UpdateAsync` (no admin bypass, throw InvalidOperationException with LogWarning).
  - `UpdateAsync` method added to interface + implementation (future-proof pattern parity with RuleSet).
  - Pattern A pre-factory guard in `CreateDecisionTableCommandHandler` (TenantId validation + LogWarning on mismatch).
  - 7 new unit tests: DecisionTableRepositoryTests.cs (5) + CreateDecisionTableCommandHandlerTests.cs (2). All green, 0 regression (44 Rules regression suite pass).
- **Files:** DecisionTableRepository.cs, IDecisionTableRepository.cs, CreateDecisionTableCommandHandler.cs, DecisionTableRepositoryTests.cs (new), CreateDecisionTableCommandHandlerTests.cs (new).
- **Effort:** ~45min (smaller — single file, no integration infra, matches FU5 scope tightly).
- **Discovered:** 2026-04-20 code-reviewer M2 on FU5 delta.

---

## Post-Cases-Removal Follow-ups (2026-05-01, PR #71)

### ~~WFE-AUTH-FAIL — WorkflowEndpointsTests regression (403 Forbidden post-RBAC v2.133p6)~~ ✅ DONE 2026-05-23
- **Failing tests**: `ListDefinitions_Authenticated_Returns200`, `ListDefinitions_ResponseWrappedInApiEnvelope` in `tests/integration/AqtCoreFW.Workflow.Integration.Tests/WorkflowEndpointsTests.cs`.
- **Root cause hypothesis**: Tests use role `"GovOfficer"` (pre-RBAC v2.133p6) which carried workflow permission claims. Post-refactor, JWT claims removed; perms via `IEffectivePermissionService` DB lookup. TestSystemAdminPermissionBypassHandler only bypasses `"SystemAdmin"` role.
- **Discovery**: COVR-1 finalize gate (2026-05-01).
- **Last touched**: commit `102988ed` (pre-v2.133p6 RBAC refactor).
- **Priority**: P2 (blocks Workflow integration suite all-green). **Effort**: ~1-2h.
- **Suggested fix**: (a) switch to `"SystemAdmin"` + DB seed, or (b) extend TestSystemAdminPermissionBypassHandler, or (c) seed Identity rows in WebAppFixture.
- **Resolution (2026-05-23)**: Audit-then-patch. **Audit found row partially stale** — the 2 originally-named tests (`ListDefinitions_Authenticated_Returns200`, `ListDefinitions_ResponseWrappedInApiEnvelope`) had been silently fixed via path (a) "SystemAdmin" sometime between 2026-05-01 and now (lines 36 + 58 of test file use SystemAdmin with xmldoc explaining TestSystemAdminPermissionBypassHandler bypass). But **2 SIBLING tests in same file were missed by the silent fix**: `ListAssignments_Authenticated_Returns200` (line 70 used "GovOfficer") + `ListAssignments_CrossTenant_Isolation` (lines 84-85 used default no-role). Both hit `GET /api/v1/workflow/assignments` which was elevated from naked `[Authorize]` to `[Authorize(Policy = PermCodes.Workflow.Assign.Manage)]` by the 4-A-A INVARIANT closure 2026-05-22 (memory `phase-04a-a-invariant-closure-260522`; controller xmldoc `WorkflowAssignmentsController.cs:22-23`). 4-A-A's "5 pre-existing integration test files folded" missed this 6th sibling file. **Patch (~10min)**: 2-line role-arg addition mirroring lines 36 + 58 precedent — `"SystemAdmin"` injected on all 3 `CreateAuthenticatedClient` call sites + xmldoc comments documenting `TestSystemAdminPermissionBypassHandler` bypass mechanics + why SystemAdmin preserves tenant isolation (`ResolveTenantId()` reads JWT `tenant_id` claim independently of role; `TestSystemAdminPermissionBypassHandler` bypasses ONLY `PermissionRequirement`, not tenant context). **Test gate**: `WorkflowEndpointsTests` 5/5 GREEN (was 3/5 + 2 FAIL pre-patch); commit `<pending>`.
- **Carry-fwd META-L (260523-S candidate)**: Memory invariant **260522-C reinforced** — "Phase N+1 fail-CLOSED behaviors leak into Phase N test seeds; INVARIANT sweep ALL integration tests by route URL pattern" — 4-A-A's 5-file sweep was scoped to `WorkflowFineResolverGateTests` neighbours; missed cross-assembly siblings (here `Workflow.Integration.Tests/WorkflowEndpointsTests.cs`). Practical strengthening: when a controller gains a Policy gate, grep `CreateAuthenticatedClient` (all assemblies) for callers hitting that controller's routes — narrower than full route-pattern sweep but catches the missed-test-class case. Audit-clean pattern week strengthens to **4th instance** (FE-FU 4-A-A clean / P6-DEFER-2 WONTFIX / T3-BP-FORMMAP-BACKFILL stale / WFE-AUTH-FAIL partial-stale-partial-real); invariant **260522-A re-grep-at-slice-start** also reinforced (the row claimed only 2 failures; reality was 2 fixed + 2 still-failing-siblings).

---

## Completed (2026-04-11 & 2026-05-01)

- [x] **COVR-1:** Workflow execute-transition + concurrency coverage restoration — 2026-05-01
  - Restored 2 deleted Workflow integration test files on generic 2-state fixture (no Cases dependency).
  - **Files added**: InlineWorkflowDefinitionFactory.cs, ExecuteTransitionContractTests.cs, WorkflowInstanceConcurrencyTests.cs.
  - **Test results**: 3/3 new tests PASS. Build 0E/0W. Code-review 9.7/10 APPROVE (0 P0/P1).
  - **Test-only change** — no production code, entities, modules, or FE↔BE contracts modified.
  - Plan: `plans/260501-1712-covr-1-workflow-execute-transition-coverage/plan.md`.
- [x] **T2-01:** E2E P1 test cases — 2026-04-11
  - 718 TC-IDs implemented across 131 spec files, 950 test() calls
  - Verified: 766 pass / 25 fail / 46 skip / 50 DNR (4.4min suite)
  - 25 failures tracked in T3-03 for follow-up
  - Plan: `plans/260406-1740-e2e-spec-implementation/plan.md` (8/8 phases ✅)
- [x] **T4-02:** Forms FE i18n migration — 2026-04-11
  - 15 keys added, 5 files migrated, 30 TDD tests | `72644649`
- [x] **T4-01:** RBAC FE i18n migration — 2026-04-11
  - 50 strings migrated, 56 TDD tests | `72644649`
- [x] **T1-02:** Full Module Audit (3 phases) — 2026-04-11
  - Integration tests (10 modules), shared fixtures, dead code cleanup
- [x] **T1-01:** Parallel Branch UI + Security Fixes — `8a1ca024..84ae8477`
- [x] Forms Permission Gating — `875d08b9`
- [x] RBAC Menus bug fixes — `310757b7`
- [x] Forms FE red-team (10 fixes) — `ff112ccc`
- [x] Server-side RichText sanitization — `a44eff0e`

---

### T3-BP-FORMMAP-REPRO (P2, NEW 2026-05-23 12:00 ICT, ~1-2h): Production fix — `BusinessProcessRepository.UpdateAsync` same-triple replace divergence

- **Severity:** P2 (latent production bug; same-triple replace path silently fails with 500 GOV_SYS_000 under real EF Core 10 + SqlServer).
- **Discovered:** 2026-05-23 ICT 11:30 via TDD spike for [[T3-BP-FORMMAP-REPLACE-TEST]] (plan `260523-1111-bp-formmap-replace-test/`). 4 diagnostic experiments + ChangeTracker state dump empirically proved the 2026-05-17 v2.137p62 fix at `BusinessProcessRepository.cs:87-118` is a NO-OP for the bug class it was meant to address.
- **Empirical evidence (verified against real SqlServer testcontainer):**
  - `bp.ClearTransitionForms()` invokes `_transitionForms.Clear()` on private List backing exposed as `IReadOnlyList<T>.AsReadOnly()`. EF Core 10 DOES detect severance for Required relationships with `OnDelete(DeleteBehavior.Cascade)` (per `BusinessProcessDefinitionConfiguration.cs:52-55`) → auto-marks severed children `state=Deleted` (NOT Unchanged as the 2026-05-17 fix's code comment at lines 87-95 claims).
  - Walk filter `e.State != EntityState.Deleted` at line 113-114 then EXCLUDES the already-marked orphans → walk iterates 0 entries → no-op.
  - At SaveChanges, EF batches: INSERT new tuple + DELETE old orphans. EF Core 10 evaluates UNIQUE-INDEX constraint on INSERT BEFORE issuing DELETE for the colliding row → `Cannot insert duplicate key value (BPId, TrId, TplId)` thrown.
  - 2-batch workaround (separate SaveChanges for DELETE then INSERT) ALSO fails because EF Core 10 **resurrects** post-Cascade-saved entries as `state=Unchanged` in ChangeTracker (DB count goes 2→0 but tracker count stays 2 with reverted state) → next DetectChanges re-severs them → infinite resurrection loop.
- **Failure mode in production:**
  - `ReplaceFormMapsCommandHandler` (`src/modules/businessprocess/.../ReplaceFormMaps/ReplaceFormMapsCommandHandler.cs:128-168`) calls `bp.ClearStateForms()` + `bp.ClearTransitionForms()` then re-adds all form-map triples from the request DTO.
  - If user submits replace with at least one IDENTICAL `(WorkflowTransitionId, FormTemplateId)` tuple as previously persisted → unique-key collision → 500 GOV_SYS_000.
  - **Why not reported since 2026-05-17:** (a) most edits change something (template version bump, isRequired toggle, displayOrder reorder), (b) 412 ETag check catches rapid double-submits, (c) FE form-map editor `business-process-form-maps-view.tsx` may pre-filter "no-op" saves before submitting.
- **Candidate production fix paths (require validation):**
  - **(A) `_db.ChangeTracker.Clear()` between Cascade-save and Add** — forces EF to forget resurrected entries. Pattern: `ClearStateForms+ClearTransitionForms → bp.SaveChanges() → ChangeTracker.Clear() → re-load bp via GetByPublicIdAsync → AddStateForm/AddTransitionForm → repo.UpdateAsync`. **Risk:** breaks single-aggregate Unit-of-Work invariant; needs ETag re-check.
  - **(B) Two-pass on BP repo** — Pass 1: ClearStateForms + ClearTransitionForms + SaveChanges (commits DELETEs only). Pass 2: AddStateForm/AddTransitionForm + SaveChanges (commits INSERTs). **Risk:** non-atomic — partial-failure leaves BP with 0 form-maps; needs explicit transaction wrapping both passes.
  - **(C) Switch BP child relationships from `Cascade` to `ClientCascade`** — EF Core 10 doesn't auto-mark severed children as Deleted under ClientCascade; walk filter then catches them as Unchanged orphans → marks Deleted → single SaveChanges issues DELETE-then-INSERT correctly. **Risk:** loses DB-level cascade semantics on direct BP-row delete (orphans linger until app-level cleanup).
  - **(D) Replace `_transitionForms.AsReadOnly()` with raw `IReadOnlyList<T>` cast** — EF Core may handle severance differently when collection nav doesn't go through `AsReadOnly()` wrapper. **Risk:** breaks encapsulation invariant per [[forms-readonly-collection-pattern]].
- **Path A or C recommended.** B is too risky (non-atomic). D breaks abstraction.
- **Acceptance criteria:**
  - Same-triple replace via `POST /api/v1/businessprocess/{id}/form-maps` succeeds 204 No Content (not 500).
  - DB row count stable at N (delete-then-insert, not duplicate-insert).
  - Integration test `T3-BP-FORMMAP-REPLACE-TEST` becomes implementable (F1+F2 GREEN against fixed SUT).
- **Effort:** ~1-2h (45min path-selection scout + 30min impl + 30min integration test resurrection + 15min code-review).
- **Blockers:** ratify path A/B/C/D selection with product/architecture owner.
- **Status:** ✅ **CLOSED 2026-05-24** — Path C ratified + implemented (cascade→ClientCascade semantics switch + EF migration + F1/F2 integration tests GREEN).

### T3-BP-FORMMAP-REPLACE-TEST (P3, NEW 2026-05-17 22:30 ICT, ~30min): Integration test for BP form-map orphan reconcile

- **Discovered:** 2026-05-17 v2.137p62. EF Core's snapshot change detection does not flag children removed via `_list.Clear()` on a private List<T> backing field exposed as `IReadOnlyList<T>.AsReadOnly()`. ReplaceFormMaps handler called `bp.ClearStateForms()` + `bp.ClearTransitionForms()` then re-added — second replace with the same (transitionId, formTemplateId) triple hit `UX_BusinessProcessTransitionForms_Bp_Transition_Form` unique-index violation (SQL 2601 → 500 GOV_SYS_000). Fix in `BusinessProcessRepository.UpdateAsync` walks `ChangeTracker.Entries<TChild>()` and marks orphans `Deleted` before SaveChanges. Shipped without automated coverage.
- **Test scope:** Integration (real BP DbContext + SQL Server testcontainer). Steps: (1) Seed BP with 1 state-form + 1 transition-form binding. (2) Call ReplaceFormMaps with the SAME (state, form) + (transition, form) triples. (3) Assert: SaveChanges succeeds, table row count stays at 1+1 (delete-then-insert, not duplicate-insert).
- **Why deferred:** No BP module integration test infra exists yet (only Application unit tests). Setting up testcontainer + DbContext seed needs ~30min plus follow-on tests for other replace-style aggregates. Treat as part of a broader BP integration-test foundation phase.
- **Effort:** ~30min (test alone) or ~1.5h (test + DbContext seed infra).
- **Status:** ✅ **CLOSED 2026-05-24** — BP.Integration.Tests project created (Phase 01 bootstrap) + F1/F2 both GREEN (Phase 02 tests passing) + full regression sweep clean (Phase 03). Tests now verify production fix (Path C: cascade→ClientCascade) handles same-triple replace correctly. BP.App 94/94 + Arch 105/105 + build 0E/0W.

### T4-04: FE↔BE validator regex sync audit
- **Effort:** 1d (completed 2026-04-14)
- **Status:** ✅ COMPLETE
- **Context:** 2026-04-13 debug — `FormTemplates.code` thiếu regex FE → BE 422 với message bị nuốt. Scan toàn codebase phát hiện 7 gap; red-team review in Phase-02 expanded to 12.
- ~~Originally 7 gaps, expanded to 12 after red-team review (+5 admin gaps). All patched.~~
- **Scope (historical):** Audit tất cả `Form.Item` FE có BE validator tương ứng. Gaps patched (xếp theo severity):
  - **User-facing (nghiêm trọng):** *(OBSOLETE — Cases module removed 2026-05-01, PR #71; files below deleted)*
    - ~~`web/src/features/cases/case-create-form.tsx:65`~~ — deleted with Cases module
    - ~~`web/src/features/cases/case-create-form.tsx:57`~~ — deleted with Cases module
  - **Admin CRUD:**
    - `web/src/features/master-data/master-data-page.tsx:278` — Dictionary `code` → BE `^[A-Z0-9_]+$` + `MaxLength(100)` (CreateDictionaryCommandValidator.cs:9-10)
    - `web/src/features/notifications/notification-templates-admin-page.tsx:205` — `templateKey` → BE `^[a-z0-9_]+$` (CreateNotificationTemplateCommandValidator.cs:13)
    - `web/src/features/integration/partner-list-page.tsx:126` — Partner `code` → BE `MaxLength(50)` (CreatePartnerCommandValidator.cs:11)
    - `web/src/features/templates/template-list-page.tsx:141` — DocumentTemplate `code` → BE `MaxLength(100)` (CreateDocumentTemplateCommandValidator.cs:10)
    - `web/src/features/forms/form-templates-page.tsx:211,218` — `name`, `nameVi` → BE `MaxLength(200)` (CreateFormTemplateCommandValidator.cs:14,18)
    - +5 additional admin gaps patched in Phase-02 (cases form Phase-01 + 5 admin pages Phase-02)
- **Shared i18n keys added:** `common.validation.maxLength` (with `{{max}}` interpolation), `pattern.upperAlphaNum`, `pattern.lowerAlphaNum`; page-specific: `forms.codePatternError`
- **All patched Form.Items:** `validateFirst` + `// Mirrors BE <Validator>.cs` comment
- **Playwright gate:** deferred (backend offline at 127.0.0.1:5002); TypeScript typecheck clean as substitute
- **Đã khớp (bỏ qua):** `roles/role-form-modal.tsx:88`, `forms/form-templates-page.tsx:194` (code — đã fix session trước)
- **Root-cause ngừa tái diễn:** chờ T5-19 (Orval codegen)
- **Discovered:** 2026-04-13 | **Completed:** 2026-04-14

### T5-16: Workflow dashboard — server-side cursor pagination
- **Effort:** 1-2d estimate → Phase 1 actual ~0.5d
- **Scope:** Backend `/api/v1/workflow/instances` cursor-based pagination; FE infinite-load for very large tenants (>1K instances)
- **Discovered:** 2026-04-13 (Direction A A3 session)
- **Phase 1 (BE):** ✅ COMPLETE — 2026-04-15
  - `GET /api/v1/workflow/instances` — cursor-paginated with `StartedAt DESC, Id DESC` keyset
  - `GET /api/v1/workflow/instances/summary` — KPI counts (active, completedToday, slaBreachRate, total)
  - `CursorCodec` — base64 encode/decode, 400 on malformed (`GOV_WORKFLOW_040`)
  - Keyset index migration (raw SQL, schema `workflow`)
  - 16 new unit tests green (9 CursorCodec + 7 handler)
  - All 6 verification gates passed; `ToQueryString()` proves `[Id] <` keyset predicate [RT-01]
  - Plan: `plans/260415-1138-t5-16-workflow-cursor-pagination/`
- **Phase 2 (FE):** ✅ COMPLETE — 2026-04-15
  - `use-workflow-instances-infinite.ts` — `useWorkflowInstancesInfinite` + `useWorkflowInstancesSummary` hooks (72 LOC)
  - `admin-workflow-instances-page.tsx` — route page wires both hooks + filter state
  - `instance-monitoring-dashboard.tsx` — Load-more button, summary KPIs, select-all tooltip [RT-08]
  - 7 new vitest tests (5 infinite + 2 summary); all 116 workflow tests green
- **Phase 3 (Tests + Docs):** ✅ COMPLETE — 2026-04-15 (trimmed scope)
  - `WorkflowInstancesListEndpointTests.cs` — 8 integration tests: 401/403/200/400 for list; 401/403/200 for summary; rate-limit smoke
  - `WorkflowInstancesKeysetCorrectnessTests.cs` — 2 active tests: 120-row walk (3 pages × 40, no dupes, no gaps, StartedAt DESC) + tenant isolation (60A+60B, no bleed); perf gates `[Fact(Skip)]` (Testcontainers hardware)
  - `20260411105147_DropRowVersionFromWorkflowInstance.cs` — migration made idempotent (pre-existing bug: 20260329 already dropped column on production DBs)
  - `SqlServerCollectionLocal.cs` — re-declares `[CollectionDefinition]` locally to fix xUnit v2 assembly-scoping bug (same latent bug in 16 other integration projects, out of scope)
  - `docs/dependency-map.md` — Section 4 workflow-instances row + Section 8 FE hooks (already present from prior entry)
  - 15/15 integration tests pass (2 perf gates skipped); all pre-existing endpoint tests unaffected
  - Deferred: Playwright E2E spec, rate-limit quota exhaustion — see T5-16-FOLLOWUP below
- **Status:** ✅ COMPLETE — T5-16 fully delivered 2026-04-15

### T3-WF-CLAUSE-MODES-RT2-15 (P2-product, NEW 2026-05-23 19:24 ICT, ~4.5-5.5d): Wire 4 deferred `AssignmentClauseMode` producers

- **Discovered:** 2026-05-23 19:00 ICT via user UI probe — Workflow Designer transition-assignment dropdown shows only 3 MVP modes (`SpecificUser`, `Initiator`, `ByPermission`); 4 modes (`PreviousHandler=2`, `ByRole=4`, `ByGroup=5`, `ByOrgUnit=6`) defined in `AssignmentClauseMode.cs:11-20` but fail-CLOSED in `WorkflowGraphValidator._notSupportedModes` (lines 23-30) per RT2-15 deferral.
- **Scope:** Lift R4 NotSupported guard + ship producer + FE picker + integration test, **one merge unit per mode** to honor INVARIANT **260523-D** (fail-CLOSED ↔ producer same-slice pairing).
- **Empirical evidence (scout 2026-05-23 19:30):**
  - `PreviousHandler` — trivial: `WorkflowTask.CompletedBy: Guid?` set at line 176; producer `repo.GetLastCompletedTask(instanceId).CompletedBy`.
  - `ByGroup` — `Identity.Domain.Entities.UserGroup` + `UserGroupMembership` (flat membership, no nesting).
  - `ByRole` — `Identity.Domain.Entities.ApplicationRole` (ASP.NET Identity base) + `GroupRoleAssignment` for group-inherited roles.
  - `ByOrgUnit` — `Organization.Domain.Entities.OrgUnit` adjacency-list (`ParentId`, `Level`, `SuccessorId`).
- **Status:** ✅ **DONE 2026-05-26 — arc CLOSED, all 5 phases shipped (p108→p116).** Phase 05 cross-mode validator sweep SHIPPED v2.137p116 (`8318d538`); plan `260523-1924-rt2-15-clause-modes-deferred/plan.md` = COMPLETE. Remaining work split into separate FU rows (RT2-15-PHASE-01-FU / 03-FU / 05-EQ2). Phase-by-phase: Phase 01 PreviousHandler SHIPPED 2026-05-24 v2.137p108 (`7dc3784b`). Phase 02 ByGroup SHIPPED 2026-05-25 v2.137p112 with Q2 Save-validation hop + C1 cross-tenant fix (~3h actual). Phase 03 ByRole SHIPPED 2026-05-25 v2.137p110 with read-side extension (claim/inbox predicate merge role-only). **Phase 04 ByOrgUnit SHIPPED 2026-05-26 v2.137p114** (~3h actual; producer + reader 4th OR-arm bundled per 260523-D; temporal Q4 + Code key Q3 ratified; pattern-mirror discount 3rd instance).
- **Plan scaffold:** `plans/260523-1924-rt2-15-clause-modes-deferred/plan.md` (5 phases; phases 01-04 COMPLETE 2026-05-26).
- **Product Q (PQ-1 RATIFIED):** ByRole — direct `AspNetUserRoles` only (SHIPPED Phase 03 v2.137p110).
- **Product Q (PQ-2 RATIFIED):** ByGroup — active groups + active users (SHIPPED Phase 02 v2.137p112 with Save-validation hop via `IIdentityModuleClient.ResolveActiveUserGroupsByCodesAsync`).
- **Product Q (PQ-3 RATIFIED):** ByOrgUnit — flat temporal-active assignments (SHIPPED Phase 04 v2.137p114).
- **Carry-fwd:** Phase 05 cross-mode validator sweep + E-Q2 dual-meta error format (~1-2h). Candidate follow-up rows: RT2-15-PHASE-03-FU + RT2-15-PHASE-04-FU (integration E2E coverage) + optional descendant-tree-select + `RoleInOrg` filter. Product policy review flagged for M3 (workflow-admin sees all tenant group codes).
- **Linked:** [[phase-savegraph-assignment-260523]] 260523-D INVARIANT origin + [[phase-5c-design-time-graph-validation-merged-260521]] R4 NotSupported origin + [[rt2-15-scaffold-and-q-fold-260523]] plan origins.


## Backlog-optimize archival 2026-08-04 (CI-DISABLED section — fully resolved, pulled from active backlog)

> Section moved verbatim from `backlog.md`: CI-DISABLED-SINCE-APRIL + all sub-blockers #1–#8 closed/GREEN 2026-07-31. Content unedited. A one-line pointer remains in the active backlog.

### CI-DISABLED-SINCE-APRIL `[done]` — T2 🟠 (discovered 2026-06-10; chain-head for any CI gate) — ✅ **RESOLVED + GREEN 2026-07-31 (sub-blockers #1–#8 all closed); tag swept `[blocks-v1.0]` → `[done]` 2026-07-31**
- **✅ CI RE-ENABLED 2026-07-31.** `gh workflow enable` + repo-level `actions/permissions {enabled:true}` (workflow-enable alone was insufficient — repo Actions was OFF, runs queued 0-job forever) + `workflow_dispatch` trigger added. **First real runs since 2026-04-13.** Filter-by-path/CS8425/NU1902 all VERIFIED on the real ubuntu runner: `Build (warn-as-error)` = success on both runs; `smoke-tests`/`quality-gates`/`frontend` = success. Run 1 unit-tests hit the 10m job timeout mid-test (not a code fail) → raised `timeout-minutes` 10→25 (`bd9c49dae`). Run 2 (`30606206217`, 9m55s, under timeout) surfaced **2 real pre-existing Linux-only test failures** (below) — the true value of re-enabling.
- **🔴 NEW sub-blocker #8 — 2 platform-dependent (Windows-authored) unit tests fail on the Linux CI runner, discovered 2026-07-31 on the first real Linux run.** Both shipped AFTER CI went dark (never ran on Linux); NOT touched by the re-enable slice (git-confirmed). (a) `AqtCoreFW.Infrastructure.Extensions.Tests…DrillTempFileCleanupTests.TryDelete_LockedFile_LogsWarning_NoThrow` (`:75`) — asserts a `FileShare.None` lock makes `File.Delete` throw → warning logged; the test's OWN comments say "on Windows" (`:80,:88`). On Linux POSIX-unlink deletes an open file, so no throw, no warning → NSubstitute `ReceivedCallsException`. (b) `AqtCoreFW.AuthServer.Tests.SpaClientRedirectUriResolverTests.RelativeUri_Throws_NamingTheOffendingValue` — expects `InvalidOperationException` for a relative URI; none thrown on Linux (likely a `Uri`/path parsing difference). **✅ RESOLVED 2026-07-31 (plan `260731-1334-ci-linux-only-test-failures`, user-approved):** (a) was a **real product bug** — `SpaClientRedirectUris.Resolve` (`:110`) now also requires the parsed URI's scheme be http/https, not just `UriKind.Absolute`; this rejects `/callback` (→ `file:///callback` on Linux) on BOTH platforms and closes the silent-file://-passthrough hole in Linux prod. Added `NonHttpScheme_Throws` test pinning `file:///callback` (OS-independent). (b) test-only — `TryDelete_UndeletableFile...` now OS-conditional: Windows `FileShare.None` lock, Linux `chmod 0500` (`File.SetUnixFileMode`) read-only parent → `UnauthorizedAccessException` (in the catch filter), both asserting the same swallow-and-log invariant so Linux keeps coverage. CA1416 (SetUnixFileMode unsupported-on-Windows under `/warnaserror`) hit + fixed via `!OperatingSystem.IsWindows()` guard. Local (Windows): AuthServer 10/10, DrillCleanup 4/4, build `/warnaserror` 0-error. Code-review DONE 0-blocking (confirmed no config regression: enumerated both seeder call sites; `ubuntu-latest` non-root `runner` makes the chmod trigger valid). **✅ FINAL ORACLE PASSED — CI run `30611141000` (commit `70308079c`) = overall SUCCESS 2026-07-31:** unit-tests + quality-gates + frontend + smoke-tests all green on the real ubuntu runner (integration/e2e/docker `if:false` as designed). The Linux file:// rejection and EACCES-swallow branches behaved exactly as reasoned. **CI-DISABLED-SINCE-APRIL is now fully re-enabled and GREEN — all sub-blockers #1–#8 resolved.**
- **🔴 Discovery (empirical):** the GitHub Actions `CI` workflow is **`state: disabled_manually`** since **2026-04-13** (`gh api .../actions/workflows`). NO CI run has executed on any push since then (~2 months) — every "pushed ahead=0" ship in that window had ZERO CI verification. The `CI-FE-NPM-LOCKFILE-STALE` premise "frontend job red since 06-09" was therefore moot (the job never ran). Was disabled because the last April runs were red (unit-tests at `Build (warn-as-error)`, frontend at vitest).
- **Active jobs on push** (others `if:false` per issue #21): `unit-tests` (.NET build /warnaserror + non-Integration tests + coverage-warn), `quality-gates` (all steps non-blocking → effectively always-green), `frontend` (pnpm now, green local). `integration-tests` already `if:false` (Testcontainers, 04-18). `release-approval` has an `environment` manual gate.
- **Progress 2026-06-10 (scout-first + fix):** verified `dotnet build src/AqtCoreFW.slnx -c Release /warnaserror` = **0W/0E** at HEAD (April warn-as-error failure self-healed). Fixed the two Workflow stale-test families from today's clone arc → `WORKFLOW-CLONE-STALE-UNIT-TESTS` below. Full non-Integration suite now: **all Workflow green** (App 640/640, Domain 145/145); only the 4 rows below remain red.
- **🔴 Blocking re-enable — 4 failures, ALL INVESTIGATED 2026-06-10 (NOT homogeneous stale-tests — 3 of 4 are flake/structural, only 1 deterministic):**
  1. **`Audit.Infrastructure…AuditCategoryBackfillSqlMirrorTests.Migration_Sql_File_Mentions_Every_Mirror_Key`** — DETERMINISTIC. Test (`:131`) reads a hard-pinned migration `20260501004056_AddAuditCategoryAndSeverity_M1.cs` that was **SQUASHED** (audit migrations now start `20260505041956_InitialCreateFoundationV1`); grep confirms **NO current audit migration contains the SQL CASE backfill** (`WHEN '…'`). The mirror-invariant (migration SQL CASE == C# `AuditCategoryMapper`) no longer exists in the schema → the file-reading test is **obsolete**. Fix = design call: delete `Migration_Sql_File_Mentions_Every_Mirror_Key` (and reconsider sibling `Mapper_And_SqlCase_Agree` which still passes off hardcoded dicts) since categorization is now runtime-only. NOT mechanical — needs confirmation the SQL backfill is intentionally gone. **✅ SELF-RESOLVED (audit 2026-07-30):** row was STALE — someone already did exactly this between 06-10 and now. `AuditCategoryBackfillSqlMirrorTests.cs` no longer contains `Migration_Sql_File_Mentions_Every_Mirror_Key` (grep = 0) nor the pinned migration `20260501004056`; the file now holds ONE pure in-memory test `Mirror_Matches_AuditCategoryMapper_For_Every_AuditAction_Constant` (reflection over `AuditAction` constants vs `AuditCategoryMapper.Map`, no file read). **Verified PASS 1/1** (`dotnet test --filter ~AuditCategoryBackfillSqlMirrorTests`). Sub-blocker #1 is CLOSED — 3 of 4 remain.
  2. **`Reporting.Infrastructure…ExecuteReportJobTests.ExecuteAsync_NonSelectSql_MarksExecutionFailed`** — **FLAKY (contention), NOT stale.** Passes in isolation (1/1); fails full-suite with `ErrorMessage = "dangerous keyword 'PATTERN_TIMEOUT'"` instead of "must start with SELECT". The SQL validator uses a Regex with a timeout; under full-suite CPU contention the regex times out and returns the `PATTERN_TIMEOUT` pseudo-keyword before reaching the SELECT-prefix check. The test assertion is CORRECT — do NOT change it. Same class as `WFE-FULLSUITE-CONTENTION-TIMEOUT` (frontend). Real robustness concern: the validator's regex-timeout fallback masks the real validation result under load. **✅ ROOT-FIXED 2026-07-30** (plan `260730-1353-sql-validator-regex-timeout`): `SqlValidationHelper`'s three regexes used a **100ms** `RegexMatchTimeoutException` window that false-fired under CPU contention (and prod load) → raised to a shared `RegexTimeout = 1s` constant, fail-closed catch blocks unchanged. The blocklist pattern is a flat alternation (NOT ReDoS-prone), so 1s is a safe belt (a genuine ReDoS hangs sec-min, still trips). Fixes the PROD robustness gap, not just the test flake. Code-review 0-crit (5/5 verified, defense intact). Verify limit: per-project green (Infra 26/26, App 27/27) proves no-regression + root-removed-by-logic, NOT a reproduced full-slnx contention run (accepted — pure safety-margin widening).
  3. **`Reporting.Application…CreateReportDefinitionCommandHandlerTests.Handle_ValidCommand_CreatesDefinition`** — **FLAKY (isolation/shared-state), NOT stale.** Passes in isolation (1/1); fails only in full-suite. Root cause un-diagnosed (shared static state / xUnit parallel collision). Needs a focused flake hunt, not an assertion edit. **⚠️ NOT confirmed closed by the #2 fix (2026-07-30):** #3's row diagnosis is *shared-state/parallel-collision*, a DIFFERENT root than #2's regex-timeout. If `CreateReportDefinition` validation calls `SqlValidationHelper`, the #2 timeout fix may incidentally help — but do NOT assume it. **✅ RESOLVED 2026-07-31 — the row's shared-state hypothesis was WRONG; #3 shares #2's exact root.** Source-proven: `CreateReportDefinitionCommandHandler.cs:31` calls `SqlValidationHelper.FindDangerousKeyword`, whose `catch (RegexMatchTimeoutException)` returns the fail-closed `"PATTERN_TIMEOUT"` sentinel (non-null ⇒ handler rejects a VALID command ⇒ `Handle_ValidCommand` fails). Under contention the old 100ms regex timed out → false reject. The #2 fix (100ms→1s shared `RegexTimeout`) closes #3 for free. **Verified under REAL full-slnx contention 2026-07-31:** `Reporting.Application.Tests` 27/27 + `Reporting.Infrastructure.Tests` 26/26 green while ~40 assemblies saturated CPU (durations 45s — genuine contention). The NSubstitute repo/guid stubs are instance-level (per-test ctor), never static → the "shared-state" guess had no basis.
  4. **`TenantIsolation…RlsPolicyPresenceTests.RlsPolicyCount_Equals_Expected`** — **STRUCTURAL.** This is an INTEGRATION test (`tests/integration/`, needs a live SQL Server via `RlsPolicyPresenceFixture`) but its FQN `AqtCoreFW.TenantIsolation.Tests…` does NOT contain "Integration", so the unit-job filter `--filter "FullyQualifiedName!~Integration"` does NOT exclude it → it runs in the no-DB unit-tests job. In CI (no DB) it fails at fixture connection; locally it fails on a stale expected count (39 hardcoded `ExpectedPolicies` vs actual DB). Fix = exclude integration projects from the unit-job scope (don't run `src/AqtCoreFW.slnx` integration assemblies in unit job, or tag them so the filter catches them) — AND separately reconcile the 39-count if it's genuinely drifted. **⚠️ SCOPE WIDENED (audit 2026-07-30) — this is NOT one leaking test, it is a fragile FQN-substring filter scheme leaking ≥3 whole projects:** empirically, `tests/integration/` projects whose FQN lacks the "Integration" token AND that carry no matching `Category` trait are invisible to BOTH filters — they LEAK into the no-DB unit job (`--filter "FullyQualifiedName!~Integration"`, ci.yml:54) AND are NOT included by the integration job (`FullyQualifiedName~Integration|Category=Resilience|Category=Installation`, ci.yml:208). Confirmed leaking with NO Category trait: **`TenantIsolation.Tests`, `Concurrency.Tests`** (both use live-SQL fixtures). **`Security.Tests`** has `Category=Security` but the integration job does NOT include that category → it also leaks into unit + is skipped by integration. (Resilience/Installation are correctly caught by their Category; SmokeTests runs its own `Category=Smoke` job.) **Proper fix = filter integration OUT of the unit job by PROJECT PATH, not FQN substring** (run `tests/unit` + `tests/architecture` in the unit job; `tests/integration` in the integration job) — eliminates the leak class permanently. Fragile-FQN-patch (excluding the 3 names) leaves the trap armed for the next project. **DEFERRED: user-owned CI-architecture decision (filter-by-path vs unify the trait scheme); ci.yml NOT edited 2026-07-30 — editing CI blind while Actions is off gives no real-run verification (user was away at the decision point). The count-reconcile (39 vs actual) needs a live DB, separately gated.**
  5. **`CS8425` warn-as-error build break in ~8 unit test fixtures — NEW, discovered 2026-07-31 during the #4 filter-by-path verify.** `tests/Directory.Build.props:7` sets `TreatWarningsAsErrors=true` at `Configuration=Release`, so any `CS8425` (async-iterator with a `CancellationToken` param lacking `[EnumeratorCancellation]`) is a **build error** in the unit job. The `FakeReadDbConnection.QueryUnbufferedAsync<T>` fakes (added by `801e21e67` "stream submissions CSV export" on **2026-07-29**, AFTER CI went dark 2026-04-13) omit the attribute → 7 errors across `Forms.Application.Tests` (3), `Identity.Application.Tests` (2 — `ListCredentialPolicies`, `ListExternalIdentities`), plus siblings in `Files.Application.Tests`/`Workflow.Application.Tests`/`Infrastructure.Extensions.Tests`. **A/B-convicted pre-existing, NOT a filter-by-path regression:** building `Forms.Application.Tests` standalone `-c Release -p:TreatWarningsAsErrors=true` = 3 errors / exit 1; the projects are already in `src/AqtCoreFW.slnx` so the current `ci.yml:48` `/warnaserror` build would break identically the moment CI re-enables. **Never ran through CI** (shipped after Actions off). Fix = mechanical: decorate the fake iterators' `CancellationToken` param with `[EnumeratorCancellation]` (~8 files, test-only, low-risk). Gates the unit job going green on re-enable — independent of #4's filter mechanism. **✅ FIXED 2026-07-31 in the #4 filter-by-path slice** (user-approved): 7 fakes across `Files.Application.Tests`/`Forms.Application.Tests`(3)/`Identity.Application.Tests`(2)/`Workflow.Application.Tests` decorated with `[System.Runtime.CompilerServices.EnumeratorCancellation]`. Code-review CONFIRMED behavior-neutral (canonical fix, matches prod `DapperReadDbConnection.cs:163`; empty-iterator fakes unaffected). `dotnet build ci/unit.slnx -p:TreatWarningsAsErrors=true` = 0 CS8425 errors after fix.
  6. **`NU1902` NuGetAudit warn-as-error break — AngleSharp 0.17.1 known moderate-severity vuln, NEW discovered 2026-07-31 during the #4 verify.** `ci.yml`'s `dotnet build … /warnaserror` (MSBuild flag) treats the `NU1902` NuGet-audit warning as an error; AngleSharp 0.17.1 (transitive via Forms/Files product code) trips it. **A/B-convicted PRE-EXISTING, independent of the filter-by-path slice:** the ORIGINAL `dotnet build src/AqtCoreFW.slnx --no-restore -c Release /warnaserror` (ci.yml:48 unchanged) = **32–64 NU1902 errors / exit 1** — CI would break identically on re-enable regardless of test-scoping. Never ran through CI (Actions off since 2026-04-13). **Subtle trap noted:** `/warnaserror` (flag) elevates NU-warnings, but `-p:TreatWarningsAsErrors=true` (property) does NOT → an early verify that used the property showed a false-green "0 errors". Options (user-owned, 2026-07-31): (a) bump AngleSharp, (b) suppress, (c) accept. **✅ RESOLVED 2026-07-31 via (a) — user chose bump.** Root: `HtmlSanitizer 9.0.892` (only direct consumer = `FormDataPreprocessor.cs` RichText XSS-sanitize) pulled `AngleSharp.Css 0.17.0 → AngleSharp 0.17.1`. Bumped `HtmlSanitizer 9.0.892 → 9.1.973` in `src/Directory.Packages.props` → pulls `AngleSharp 1.6.0` (≥1.5.0 fixes CVE-2026-54570 mXSS). Verified: `dotnet build ci/unit.slnx /warnaserror` (after FULL restore) = **0 errors, NU1902 gone**; `FormDataPreprocessorRichTextTests` 10/10 green (sanitize behavior unchanged); Forms.Application product build 0W/0E. **Verify trap noted:** a per-project restore left the test project on stale `9.0.892` → transient MSB3277 + NU1902 that vanished on full `dotnet restore ci/unit.slnx` — do NOT diagnose a version conflict off a partial restore.
- **Revised scope (corrected from "~1-2h stale-tests"):** the real re-enable blockers are (a) a .NET full-suite **contention mitigation** (parallelism cap, mirrors the vitest `maxWorkers:2` fix — covers #2 + likely #3), (b) an **integration-test scoping fix** for the unit job (#4 — mechanism now filter-by-path, user-decided 2026-07-31), (c) ~~an **obsolete-test removal** design call (#1)~~ **✅ #1 CLOSED, self-resolved (audit 2026-07-30)**, (d) the **#5 CS8425 warn-as-error break** (✅ fixed 2026-07-31), (e) ~~the **#6 NU1902 AngleSharp-vuln warn-as-error break**~~ **✅ #6 RESOLVED 2026-07-31 (HtmlSanitizer bump)**, (f) ~~**#2 + #3 contention flakes**~~ **✅ #2 + #3 RESOLVED 2026-07-31** (both share the `SqlValidationHelper` regex-timeout root, fixed by the 1s bump; verified 27/27 + 26/26 under real full-slnx contention), (g) ~~the **#7 FormulaSandbox wall-clock flake**~~ **✅ #7 RESOLVED 2026-07-31**. **✅ ALL known re-enable blockers cleared — `gh workflow enable CI` is now the sole remaining step (user decision).** First real run on the low-core runner will be the true oracle (Actions is off by user choice — enabling = user decision, not auto).
  7. **`Sandbox_UnderConcurrentLoad_StaysResponsiveAndCorrect` wall-clock flake — NEW, discovered 2026-07-31 during the full-slnx contention run for #2/#3.** `tests/unit/AqtCoreFW.Forms.Infrastructure.Tests/Services/FormulaSandboxTests.cs:149` asserted `sw.Elapsed < 10s` for 80 concurrent Formula evals. Passes in isolation (41/41) but false-fired once under ~40-assembly CPU saturation — a starved-but-healthy `SemaphoreSlim(N=cores)` pool can drain slower than 10s without hanging. Same wall-clock-under-contention class as #2/#3. **✅ RESOLVED 2026-07-31:** ceiling raised 10s→30s (a real hang/deadlock/starved-pool drags far longer, so 30s still convicts it + fails loud on early-release). Test-only, correctness asserts (count + canary shed-to-null) unchanged. Verified 8/8 green.
- **✅ Filter-by-path SHIPPED 2026-07-31 (plan `260731-0849-ci-filter-by-project-path`, commit TBD):** user chose **filter-by-project-path** over unify-trait. Mechanism resolved empirically — **`.slnf` solution-filter REJECTED** (`MSB5025` against the `.slnx` parent, SDK 10.0.302); shipped hand-authored **subset `.slnx`**: `ci/unit.slnx` = 41 projects (39 `tests/unit` + `Tests.Contracts` + `Architecture.Tests`), `ci/integration.slnx` = 23 (`tests/integration` minus SmokeTests). `ci.yml` unit+integration jobs rewired to target these, `--filter "FullyQualifiedName…"` deleted (grep=0). **Code-review caught a HIGH:** the first `ci/unit.slnx` draft dropped `AqtCoreFW.Tests.Contracts` (47 no-DB tests the old `!~Integration` filter ran, IS in slnx) → added. Verified: `dotnet build ci/unit.slnx /warnaserror`-clean of CS8425; `--list-tests` precise grep = 0 leak (`Concurrency`/`Security`/`TenantIsolation`), 0 SmokeTests in integration; smoke + quality-gates jobs untouched (still build full slnx, correct). **Known follow-up (code-review MEDIUM, deferred):** no arch-test guards `ci/*.slnx` membership == directory contents → a new test project is silently omitted (the Contracts miss proves the risk is real). Verify-limit: Actions off → local `dotnet build`+`--list-tests` only, no real CI run; `gh workflow enable` still gated on #2/#3/#6.
- **Audit re-verify 2026-07-30 (empirical, INVARIANT 260522-A):** workflow STILL `disabled_manually` since 2026-04-13 (re-confirmed `gh api`). #1 self-resolved (above). #2 `ExecuteReportJobTests…NonSelectSql` re-verified PASS 1/1 in isolation → still flaky-under-contention, NOT stale. #4 `RlsPolicyPresenceTests` re-confirmed STRUCTURAL — namespace `AqtCoreFW.TenantIsolation.Tests` has no "Integration" token, and the unit job runs `dotnet test src/AqtCoreFW.slnx --filter "FullyQualifiedName!~Integration"` (whole solution, ci.yml:52-54) so the integration test leaks into the no-DB unit job. #3 not re-run this pass. **Next fix = #4 (deterministic-structural, clearest fix) OR #2/#3 contention-cap; #4 touches ci.yml + needs DB for the 39-count reconcile.**
- **Decision (user 2026-06-10):** investigated + characterized accurately; deferred fixing (3 of 4 are flake/structural needing judgment, not mechanical; fatigue high after a long session). This session fixed only the Workflow clone-arc stale tests + the pnpm-CI lockfile. Re-enable remains a separate, multi-slice effort.
