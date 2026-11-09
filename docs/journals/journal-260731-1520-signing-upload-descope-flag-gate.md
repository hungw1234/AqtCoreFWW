# Signing Upload De-Scope + Flag-Gate (v1.0 Launch De-Escalation)

**Date**: 2026-07-31 15:20
**Severity**: Medium
**Component**: Digital Signature (Upload Path), Feature Flags, Orchestrator
**Status**: Resolved

## What Happened

INVARIANT 260522-A audit on the `SIGN-FULL-PDF-CRYPTO-VERIFY` launch blocker revealed the row was mis-described: reads as 3-4d Phase-6 verify task, but empirical probe found hard dep chain ~18-25d (Phase 4: in-process qpdf sandbox + Phase 5: remote PAdES TSA/OCSP). Stub oracle was dead (commit a3c84ca05), no src/ code for phases 4-5, no launch consumer for verify. User interview clarified: v1.0 signing = form-gen PDF via USB client-side only — no inbound upload-arbitrary-sign in real usage. **Decision:** de-scope verify to v1.1 + gate the upload-sign path OFF at launch via feature flag.

## The Brutal Truth

Three days of work mischaracterized as three hours. The backlog snapshot (Phase-6 as self-contained 3-4d) decayed faster than ship cadence — by the time `/ak-cook` ran, dependencies were known to be incomplete, but the row read as a simple shortcut. The stub oracle (deleted 07-23) made verification impossible; red-team RCE surface guard is architecture-correct but has zero launch caller. Calling this a "blocker" for a codebase that never exercises the path is just keeping a sunk cost alive. The hard decision (upload OFF at launch) is the right one — it buys v1.1 time to do the sandbox + remote tiers properly.

## Technical Details

**Backlog row audit findings:**
- Phase-6 verify stub (`StubDigitalSignatureService`, `IsValid=true`) deleted 2026-07-23 commit a3c84ca05 — oracle gone, 0 callers
- Phase-4 (in-process qpdf+broker+worker sandbox) and Phase-5 (remote PAdES assembler) have zero src/ code; grep `qpdf src/`=0, grep `PAdES src/`=0
- Red-team C4 forbids untrusted PDF parsing outside Phase-4 sandbox → no valid shortcut path

**Implementation (2026-07-31 session):**
- New global feature flag: `feature:signing.upload-arbitrary-pdf` (default false ALL envs), seeded in SystemParamSeeder, added FeatureFlagsController.GlobalOnlyFlags allowlist
- Guard relocate: initial impl landed on RequestSignatureCommandHandler; code-review flagged MEDIUM — InProcessSigningRequestClient calls orchestrator directly, bypassing MediatR command (safe only by-accident-of-caller hardcoding FormPdf). Guard moved to SignatureOrchestrator.RequestSignatureAsync (single chokepoint, both callers pass). Handler reverted to plain delegate.
- Guard logic: UploadedPdf + flagOff → ConflictError (409), persists nothing; FormPdf+flagOff → not gated, reaches routing, fails BadRequest (expected).
- Guard relocation shifted 8 orchestrator ctor sites (all passing flag-enabled substitute now); test suite re-anchored: 2 new guard tests in SignatureOrchestratorTests
- FE: useUploadSignEnabled() hook (react-query, default false during load); file-list-page hides both uploaded-PDF sign buttons (USB + PAdES) when flag off. vi.mock-leak fixed: beforeEach restores true.

## Test Results

- SignatureOrchestratorTests: 13/13 green (11 existing + 2 new guard)
- Identity Integration DigitalSignatureApiTests: 15/15 green — proves full API DI resolve (orchestrator IFeatureFlagService dep + transitive Identity.Infrastructure←Identity.Application chain)
- FE sign-uploaded-file-flow.test.tsx: 14/14 green, typecheck clean
- Build: 3 touched BE projects, 0 warnings

## What We Tried

1. Stub-verify shortcut: rejected after backlog audit found oracle dead + 18-25d dep chain
2. Guard on RequestSignatureCommandHandler: code-review caught unsafe bypass via InProcessSigningRequestClient → relocated to orchestrator
3. Multiple guard implementations per ArtifactKind: not needed (UploadedPdf check is enough; FormPdf unaffected by feature flag per design)

## Root Cause Analysis

Backlog decay: a "launch blocker" tagged 3-4d did not account for two unknowns (Phase 4/5 = 0-code, stub oracle dead). The row became a sunk-cost placeholder — no user is calling upload-arbitrary-sign at launch, so the verify capability has zero consumer in v1.0. Keeping it as a blocker was a commitment to engineering a surface that has no real use case yet. The verify path IS the right architecture (sandbox+remote), but it belongs to v1.1 after we see real inbound usage patterns (liên-thông). De-scope unblocks v1.0 without killing the path — just gates it OFF until intent + design clarity arrive.

Relocating guard: initial impl by accident-of-caller safety is a smell — InProcessSigningRequestClient passed directly to orchestrator, handler never saw the call. Chokepoint-based guard (orchestrator) is correct; relocating shifts sibling registrations (ctor sites). This is not a bug in the shift — it's a risk that sneaks in when changing ordered sequences (feedback-relocating-registrations-shifts-the-ones-left-behind). Explicit verification that 8 sites all pass flagged substitute caught it pre-merge.

## Lessons Learned

1. **Backlog audit + oracle check before trusting row scope.** A 3-4d estimate without an oracle (working stub or real code) is a hypothesis, not a fact. Audit empirical: grep for code, check deps, verify there's something to test against.
2. **De-scoping without killing architecture.** Feature flag OFF is not a hack — it's the right launch posture when a consumer is missing. It lets the path stay intact (no dead-seam removal) and re-enable when intent clarifies (liên-thông RFCs, M2/LTV auth policies).
3. **Chokepoint-based guards are safer than scattered checks.** Relocated from handler to orchestrator eliminated the bypass vector. Cost: ctor sites that build orchestrator must now pass flag substitute, but that's explicit coverage (8 sites updated + 2 new tests = no silent bypass).
4. **vi.mock-leak in test setup.** `vi.clearAllMocks()` does NOT reset mockReturnValue — had to manually restore true in beforeEach. Symptom was a subsequent test seeing stale mock state from a prior test's spy.

## Next Steps

- Backlog row retagged `[blocks-v1.0]` → `[v1.1]` with 3 re-escalation guard conditions: (a) flag re-enabled, (b) inbound liên-thông RFC surfaces, (c) Phase-5 (remote PAdES TSA/OCSP + LTV/M2 auth) ships
- Phase-02c-D anchor note updated: upload path OFF v1.0 by design, feature flag path open
- Roadmap + changelog: v1.1 signing chain + M2/LTV TSA note + remote-tier line item
- User-owned open: M2/LTV compliance scope + audit-hash deferred to v1.1 RFCs (no change to v1.0 code)

**Commit**: aeab6213e (backlog re-audit + flag-gate implementation)
