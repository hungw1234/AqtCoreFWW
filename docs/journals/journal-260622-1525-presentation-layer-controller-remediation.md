# Journal — Presentation-Layer & Host-Controller Clean-Architecture Remediation

**Date:** 2026-06-22 · **Branch:** main · **Commits:** `21d19811`·`dc71fe69`·`4f106c63`·`e130d08b`·`fb053d6c`·`c72fa579`·`f5c59f6f` (pushed origin + bitbucket)

## Session goal

Execute the 6-phase `/cook` plan `260622-0935-presentation-layer-controller-remediation`: restore the Clean-Architecture dependency rule on every HTTP surface (thin controllers, `*.Presentation` → Application+SharedKernel only, host `AqtCoreFW.Api` = pure composition root) and — the real prize — close a confirmed **audit-pipeline bypass** on the signing-completion endpoints. Make the invariant machine-checkable (ADR-011). Pacing: commit P1, then human checkpoint per phase. This wrap-up session then live-smoke-verified and pushed.

## What worked

- **Root-cause framing beat file-placement framing.** The two offending controllers (`MysignCallbackResultController`, `FileUploadHandlerController`) lived in the host only because the host is the one assembly referencing `Identity.Infrastructure` (where `IdentityDbContext` lives). They injected the DbContext directly and skipped `ISender.Send` entirely → zero `AuditBehavior`/`ValidationBehavior` on the highest-risk writes (QĐ 742 §1.6.b signing callbacks + VGCA doc-flow upload). Fixing the **dependency direction** (persistence behind an audited command/handler) dissolved the placement problem AND closed the ADR-004 gap in one move.
- **Port + host-adapter for the cross-module file access.** Download/upload needed Files storage from inside Identity. Binding `Identity.Infrastructure` → `Files.Domain` directly would have broken `ModuleIsolationTests`. Correct shape: `ISigningDocFlowBlobStore` port in `Identity.Application` + `SigningDocFlowBlobStoreAdapter` in the host binding `Files.Domain.IFileStorageService`. Identity stays module-isolated.
- **Tenant-agnostic command + audit override for anonymous M2M.** The Mysign callback is `[AllowAnonymous]` and tenantless, so `TenantAwareBehavior` would reject the command. Added an opt-in `ITenantAgnosticCommand : IBaseCommand` marker (bypasses the tenant gate, keeps audit + validation) and an `IAuditLogger.tenantIdOverride` param so the audit row lands on the tenant derived from the persisted record — i.e. on the correct per-tenant HMAC chain (ADR-004), not a null chain.
- **Allowlist-shrink-to-empty with teeth.** Three guards (G1/G2/G3) seeded from a real current-tree run, each fix removing its own allowlist entry in the same commit, anti-vacuous + tripwire + `Closure_AllowlistIsEmpty` end-state assertions. All three allowlists end **empty**.
- **Full canary green + live smoke.** Architecture 145/145 · Identity.Integration 300/300 · SmokeTests 18/18 · ContractEndpoints 4/4 · 0 public-contract change. User then confirmed both signing paths green live (USB DocFlow + Mysign OTP) — the hardware/vendor gate no test can cover.

## What didn't / lessons

- **My first Phase-4 design was wrong.** I proposed `Identity.Infrastructure` → `Files.Domain` directly ("Option 1"). The user's question ("why are download/upload in Identity — shouldn't they be in Files?") caught it: that edge breaks module isolation. The right answer was the port+adapter, not relocating the work to Files. Lesson: when a cross-module dependency feels awkward, the fix is usually a port at the consumer + an adapter at the composition root, not moving the feature.
- **Empirical seeding corrected two assumptions** (INVARIANT 260522-A): `MasterDataController` trips G1 (constructor coupling), not only G2 (project ref); and three shared-infra admin DbContext injections (`Alerting`/`Backup`/`Webhooks`) must be excluded by design — the guard discriminator is **module** infrastructure, not "any DbContext". A naive universal-DbContext check over-flagged them.
- **G1 has a documented reflection blind spot:** it inspects constructor injection only, so a service-locator pull (`HttpContext.RequestServices.GetRequiredService<T>()`, which `AccountMfaController` used for `UserManager`) is invisible. Closed by refactoring the controller to genuine thin + audited dispatch, not by widening the guard.
- **`dotnet build` must run via the PowerShell tool**, not Bash (Bash blocks the literal word "build"); subagents can't build/test in this repo, so the controller drove all verification.
- **Vault dev mode is ephemeral** — secrets were 404 at smoke-start despite a 6h container uptime; re-seed via `vault-init` (idempotent) is mandatory before Auth/API or pepper/HMAC resolution fails silently.

## Decisions made

- **IBaseCommand + marker bypass** (not plain `IRequest`) for the tenantless callback — keeps it inside audit + validation while opting out of only the tenant gate.
- **Fold MasterData + SystemParams together** in Phase 5 so G1+G2 both reach empty (no carve-out left).
- **`SigningDocFlowOrphanBlobSweepJob` stays in the host** referencing `Identity.Infrastructure.Persistence` — a background job in the composition root may legitimately touch Infrastructure; forcing a port there is gold-plating (ADR-011 "out of scope").

## Test status

Architecture 145/145 · Identity.Integration 300/300 (login + signing canary) · SmokeTests 18/18 · ContractEndpoints 4/4 · SigningDocFlow RLS 3/3 · build 0E. New audit-proof tests: `AccountMfaAuditLogTests`, `MysignCallbackAuditLogTests`, `DocFlowUploadAuditLogTests` (count 0→1 + tenant attribution). Live smoke: USB DocFlow + Mysign OTP USER-CONFIRMED green.

## Next session

- **Backlog optimization is due** — `backlogs/backlog.md` at 625 lines (>600), 11 closed `###` sections still in the active file. Run the safe 2-pass archive (header-diff GATE) → `completed.md`. Deferred from this wrap-up to avoid a risky rewrite at session-close (cf. the 2026-06-20 over-deletion lesson).
- No remediation follow-ups outstanding: all 14 red-team findings addressed across the 6 phases; ADR-011 end-state (3 empty allowlists) asserted; live smoke closed the only open gate.
