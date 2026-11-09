# Shipped: Signing Provider-Allowlist Guard — Server-Side Early Authorization

**Date**: 2026-07-04 14:30  
**Severity**: High  
**Component**: Identity.Application.Signing (SignatureOrchestrator)  
**Status**: Resolved

## What Happened

Shipped `SIGN-REQUEST-PROVIDERKEY-NO-TENANT-ALLOWLIST`: fail-closed early authorization in `SignatureOrchestrator.RequestSignatureAsync` (shared chokepoint for BOTH self-sign HTTP path + Forms `InProcessSigningRequestClient` path). Denies 403 (ForbiddenError) any signing request whose assigned signer holds zero Approved active enrollments of the requested provider's EnrollmentKind. Emits audit row (SIGNING_PROVIDER_ALLOWLIST_DENIED) + Prometheus counter (signing_provider_allowlist_denied_total, new meter AqtCoreFW.Identity).

## The Brutal Truth

The plan locked a design decision that **conflicted with an existing tested architectural invariant** — and this only surfaced during full compliance-suite run at finalize. Plan stated: "Put ProviderEnrollmentKindMap (containing vendor keys 'usb-vgca'/'remote-viettel-mysign') in Identity.Domain.Signing, mirroring VgcaCertSubjectParser precedent." Implementation revealed: `Qd742Phase05bSigningComplianceTests.Compliance_D6_NoHardcodedProviderKeyInIdentityDomainAndApplication` bans provider-key literals from Identity.Domain + Identity.Application entirely (QĐ742 / D6 mandate: new VN CA provider must touch only Infrastructure + DI, never core). The rationale "mirrors VgcaCertSubjectParser" was incomplete — VgcaCertSubjectParser parses CCCD subjects, carries no provider keys, so it never triggered D6.

The lesson: a plan's "locked constraint" can silently violate existing tested invariants. Per-project build doesn't catch cross-module architecture guards.

## Technical Details

- **Guard subject:** input.AssignedSignerId (mirrors SignerBindingGuard), not initiator (red-team F-B divergence will land when BP-assigned signing ships).
- **0-enrollment behavior:** Defers to late SignerBindingGuard (no legacy break); unknown providerKey → 404 (existing); registered-but-unmapped → 403 fail-closed.
- **Test corrections during run:**
  1. Forms-path integration test expected ErrorCode "SIGNING_FORBIDDEN" but InProcessSigningRequestClient propagates the typed ForbiddenError's metadata code (`ErrorCodes.Security.Forbidden = "GOV_SEC_002"`) — impl was correct, test expectation wrong.
  2. SignatureOrchestratorTests broke (8 construction sites) when ctor gained IProviderEnrollmentKindMap param → fixed with permissive AllowAllGuard() substitute (those tests verify routing/persistence, not authz).
- **Data audit (F-C blocking gate):** CLEAN — zero enrolled signers newly-denied; both live enrollment kinds mapped; 2 orchestrator callers, both guarded.

## Root Cause: Plan ↔ Compliance Gate Drift

Compliance-suite runs only at finalize, not at plan-approval or per-module build. Plan author had no visibility into D6 scanner. The archived architecture decision (QĐ742 Phase 5b) was not cross-referenced in plan documentation. Result: a 1-hour discovery + re-architecture at integration gate instead of phase-0.

## Solution: Infrastructure + Interface (User-Approved)

Moved concrete ProviderEnrollmentKindMap to `Identity.Infrastructure/Services/Signing/ProviderEnrollmentKindMap.cs` (ONLY layer where vendor keys are legitimate, alongside provider adapters). Exposed via new key-agnostic `IProviderEnrollmentKindMap` interface in Identity.Application. SignatureOrchestrator depends on the abstraction; arch drift test + dedicated map unit test cover the real table. D6 intent preserved (Domain/Application 100% key-free); VNPT-revival ergonomics maintained (1 dict entry + 1 enum value).

## Lessons

1. **Full compliance-suite run at finalize is non-negotiable.** It catches architecture drift that per-project build misses. Budget the re-architecture window.
2. **Distinguish implementation choices from architectural constraints.** "This can only live in Domain" is not the same as "this must live in Domain." Verify both when a plan locks a location.
3. **Architecture invariants (RLS, key-free zones, tier tiers) must be cross-indexed** in plan documentation whenever relevant. Low-cost friction prevention.

## Verification

- Full solution build: 0W/0E
- Identity.Application.Tests: 426/426
- Identity.Infrastructure.Tests: 339/339
- Architecture.Tests: 150/150 (D6 guard + others pass)
- Identity.Integration.Tests (Signing namespace): 169/169
- New tests: 9 guard unit + map unit + 1 arch drift + 4 integration
- No migration, no DTO, no FE change, no public-contract break
- Audit row enum + Prometheus meter registered (AlertRule ops-doc only, no startup seed)

---

**Status**: DONE  
**Summary**: Provider-allowlist guard shipped; D6 compliance conflict resolved via Infrastructure+Interface. Smooth ship post-compliance gate. Journal at `/docs/journals/journal-260704-1430-signing-provider-allowlist-guard.md`
