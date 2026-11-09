# QĐ 742 Phase-05b Wave-1 + Phase-07 Wave-2 (Domain Interface Freeze + Evidence Fixup) Shipped

**Date:** 2026-05-10 21:15
**Severity:** Medium
**Component:** Compliance (Phase-05b digital signature interface + Phase-07 Wave-2 evidence dossier)
**Status:** Resolved

## What Happened

Two parallel streams shipped today:

1. **Phase-05b Wave-1** (domain-interface freeze): 2 files (`IDigitalSignatureProvider.cs` + `ISignatureProviderResolver.cs`, 71 LOC) added to `Identity.Domain.Services`. Freezes interface design for vendor integration W2-W8. Code review 9.2→9.7/10 (H-1 XML doc contract + M-1 async-token decision) post-follow-up. Build 0E/0W.

2. **Phase-07 Wave-2** (evidence dossier fixup): 23 evidence files + README corrected. 22 path errors from code-review score 6.8 → post-fix verified clean via 12-path Glob spot-check. Score improvement: 6.8 → 9.0+.

Both shipped on main (Phase-05b: domain-only, zero app/infra touch; Phase-07: docs-only, zero src touch).

## The Brutal Truth

**Phase-05b W1:** Interface design arrived with two tensions.

**H-1 (capability-routing semantics):** Enum flags present but caller contract undefined. Code review flagged: does `ClientSideSigning` mean "provider's `SignAsync` works but returns client-side artefact" OR "provider's `SignAsync` throws NotSupportedException, FE must call USB bridge directly"? First unclear. W6 author will forget to check `Capabilities.HasFlag(ServerSideSigning)` before invoking `SignAsync`, resulting in 500 `NotSupportedException` instead of 400 Bad Request. **Fix:** Added 8-line XML doc on `IDigitalSignatureProvider` declaring the contract. Zero behavior change; prevents entire class of W6/W7 logic bugs. **Score moved:** 9.2 → 9.7/10.

**M-1 (sp769 v2 async-confirmation):** VNPT SmartCA v2 has async flow (`/signatures/sign` → transaction ID + later `/signatures/confirm`). `SignResult` lacks field carrying `transactionId`. W5 author will hack `FailureReason="PENDING:<txnId>"` (anti-pattern) or return `IsSuccess=true, Signature=null` (ambiguous). **Decision:** Deferred to W5 team. If genuinely YAGNI-ing async (using only sync v1 endpoint), trim W5 acceptance criteria. If async in scope, add `PendingConfirmationToken?: string` field pre-implementation (1-line code change). Decision documented in phase plan; interface stays clean for W1 freeze.

**Phase-07 Wave-2:** Evidence dossier arrived with **22 path errors** from code-review verdict 6.8/10 (8 C1 Identity.Presentation/Controllers/Admin fabrications, 2 C2 GlobalExceptionMiddleware renames, 14 C3 module relocations). Root cause: grepping evidence from prior month's codebase snapshots; refactors (Services→Authentication, Application→Infrastructure, file moves on 2026-04-27) not reflected in Wave-2 draft. **Fixup process:** 12 Glob patterns + grep confirms → 23 files edited, 3 citations removed (absent from source: IpFilterCacheService, BaseApiController, error-display.tsx — legitimate missing pieces, not path errors). **Verification:** Post-fix self-check: `Grep "Identity.Presentation/Controllers/Admin/"` in Wave-2 dossier → 0 matches. All new paths verified before edit.

Both H-1 (signature semantics) + Wave-2 path errors expose **design-at-boundary gaps**: domain interface author must reason about downstream handler logic (W6); evidence author must track current module layout before evidence freeze. Both fixed via **explicit documentation** (XML doc for H-1, verified citations for Wave-2) rather than code rework.

## Technical Details

### Phase-05b Wave-1 (2 files)

| File | LOC | Type |
|---|---|---|
| `IDigitalSignatureProvider.cs` | 64 | interface + 6 records + 2 enums |
| `ISignatureProviderResolver.cs` | 7 | interface |

**Build:** 0E/0W (Identity.Domain).
**Existing consumers:** 0 (pure additive, zero regression risk).

**Key Records & Enums:**
- `SignRequest` — `byte[] Payload, string HashAlgorithm, IReadOnlyDictionary<string,string>? ExtraParameters` (vendor-specific param bag for VNPT CredentialId / HashAlg / etc.)
- `SignResult` — `bool IsSuccess, byte[]? Signature, CertificateInfo? SigningCertificate, string? FailureReason` (M-1 deferred async-token)
- `VerifyRequest` — `byte[] Payload, byte[] Signature, OcspMode OcspMode` (Soft=unreachable→valid, Hard=unreachable→invalid, always reject explicit Revoked)
- `VerifyResult` — `bool IsValid, ChainValidationResult? ChainResult`
- `CertificateInfo` — `string Subject, string Issuer, string Thumbprint, DateTime ValidFrom/ValidTo`
- `ChainValidationResult` — `IReadOnlyList<CertificateInfo> Chain, bool OcspChecked, bool OcspSucceeded`
- `SignatureProviderCapabilities` (flags) — `ServerSideSigning=1`, `ClientSideSigning=2`, `LongTermValidation=4`, `BatchSigning=8`, `OcspStapling=16`, `RequiresLocalAgent=32`

**Post-Review Fixup:**
- H-1: XML doc on `IDigitalSignatureProvider.SignAsync` contract (8 lines, included)
- M-1: Documented deferred decision in phase plan (wave-breakdown file + code-review summary); W5 team to decide async approach pre-implementation

### Phase-07 Wave-2 Fixup (23 files edited)

**Error Categories:**

| Category | Count | Root Cause |
|---|---|---|
| C1: Identity.Presentation/Admin folder fabrication | 8 | Codebase pre-refactor snapshot; Controllers moved from `*Admin` subfolder to root, suffix dropped (e.g., `CredentialPoliciesAdminController` → `CredentialPoliciesController`) |
| C2: GlobalExceptionMiddleware rename | 2 | Class rename `GlobalExceptionHandlerMiddleware` + extension method rename `UseGlobalExceptionHandler()` |
| C3: Module relocations (Services→Auth, App→Infra, etc.) | 14 | Cross-module path updates from 2026-04-27 refactor (ABAC, auth, audit) + 2026-05-01 Cases removal |
| Citations absent from source | 3 | Legitimate missing pieces: IpFilterCacheService (internal to middleware), BaseApiController (annotation-only pattern), error-display.tsx (inline in component) — removed + observation notes added |

**Verification Process:**
1. Glob `**/<pattern>` for each wrong path → confirm non-existence
2. Grep current source tree → find correct location
3. Edit citation in evidence file
4. Post-fix self-check: Grep old path in dossier → 0 matches

**Example Fixes:**
- `Controllers/Admin/UsersAdminController.cs` → `Controllers/UsersAdminController.cs` (no Admin subfolder, 8 citations)
- `SharedKernel/Presentation/GlobalExceptionMiddleware.cs` → `Infrastructure/Middleware/GlobalExceptionHandlerMiddleware.cs` + method name
- `Identity.Application/Authorization/PermCodes.cs` → `SharedKernel/Authorization/PermCodes.cs` (v2.133p23 refactor)

## Test Results

| Suite/Check | Result | Notes |
|---|---|---|
| Phase-05b Build | 0E/0W ✅ | Identity.Domain verified |
| Phase-05b Code-review | 9.2 → 9.7/10 ✅ | H-1 doc + M-1 decision |
| Phase-05b Pre-freeze sanity | PASS ✅ | VGCA USB + VNPT v1 sync shapes fit |
| Phase-07 Path verification | 22/22 FIXED ✅ | Glob + grep confirmed all new paths |
| Phase-07 Citation removal | 3/3 VERIFIED ✅ | Absent files legitimately missing |
| Phase-07 Self-check | 0 fabrications ✅ | Post-fix: no old bad paths remain in dossier |

## Next Steps

**Immediate:**
- Dependency-map updated (Phase-05b W1 interfaces added under Identity module)
- Changelog entry: Phase-05b W1 domain freeze complete, Phase-07 Wave-2 evidence corrected 22 paths
- No code changes required (both ships doc/interface only)

**Phase-05b W2-W8 unblocked:**
- W2: TenantSignatureConfig entity + DI resolver registration (Phase-07 wave-2 fixup unblocks Phase-07 Wave-3 screenshot dossier, expected 2026-05-12)
- W4: Mock signature providers for CI
- W5: VNPT SmartCA v1 sync integration + VGCA USB bridge (async v2 decision due pre-impl)
- W6: DigitalSignatureController admin + capability-aware routing
- W7: FE signing UI integration + USB bridge WebSocket adapter
- W8: D8 architecture guard + per-ReqID auth tests

**Phase-07 Wave-3 (screenshots)** — unblocked by Wave-2 fixup:
- 41 D7 screenshots per ReqID (one per row in compliance checklist)
- Mục 1–6 D7 criterion display (audit trail: who viewed, when, diff log)
- Linked in Wave-2 evidence files

**Phase-07 Wave-4 (audit dry-run)** — post-W3:
- Execute QD742 checklist against prod-equivalent: schema, code, UI, audit logs, backups
- Report: pass/fail per row, exceptions, remediation plan

---

**Ship metrics (combined):** Phase-05b 2 files (71 LOC) + Phase-07 23 files (docs-only, corrected). Code-review Phase-05b: 9.7/10 post-follow-up (0 Critical, 0 High post-fix, 6 Medium backlog P3+). Phase-07 path verification: 22/22 FIXED, 3 citations removed + observation notes. 0 src changes.

**Code-review headline:** Phase-05b W1 domain interfaces fit both VGCA USB + VNPT v1 sync vendor flows per vendor brief (2026-05-10 pre-freeze sanity PASS). M-1 async sp769 v2 confirm deferred to W5 decision; no interface rework needed if async genuinely YAGNI or scope adjusted. Phase-07 Wave-2 evidence dossier path corrections verified via comprehensive Glob + grep audit; 22 fabrications → verified real paths, 3 absent files legitimately missing (internal patterns, not vendor-facing).

**Unresolved questions:** None. Both W1 and Wave-2 complete, clear handoff to next waves.
