# QĐ-742 BE Forms Digital Signature — 04a–04d Complete

**Date**: 2026-06-13 09:00
**Severity**: Medium
**Component**: Forms.Application, Identity.Contracts, Identity.Signing (cross-module)
**Status**: Resolved (4 slices pushed; FE + smoke remain)

## What Happened

Shipped the back-end half of "Forms Digital Signature verify-at-ingest" (QĐ-742 integrity track) across 4 independently-committed slices spanning 6 days:

- **04a** (`9f0d7bb0`): `IIdentitySigningClient` cross-module verify façade; InProcess impl resolves tenant OCSP + provider, maps audit evidence; misconfig fails closed. 8 tests.
- **04b** (`2f54ddf6`): `FormSubmission` +6 nullable evidence columns + write-once `RecordSignatureEvidence`; hand-authored migration (backward-safe).
- **04c** (`309d58b3`): verify-at-ingest in both submit handlers (auth+public); hard-block-before-persist (400, no DB write); `FormDataCanonicalizer` Mode-1 deterministic canonical JSON; `SignaturePayload` bundle. New `DigitalSignature` field type (FormFieldType=8).
- **04d** (`8d583ebd`): Mode-2 server-doc-hash signing (SHA-256 of `{template,version,canonical-data}` projection); new `POST .../signature-doc-hash` endpoints (auth+public). Mode-1/Mode-2 share one field-selection helper; mode in `ValidationRulesJson` fails safe to Mode-1.

All slices: full slnx `/warnaserror` 0E; Forms.Application 258/258 ✓; ModuleIsolation 12/12 ✓; MigrationConsistency 2/2 ✓; code-review APPROVE per slice.

## The Brutal Truth

**Scout overturned the plan's entire premise.** I delegated a one-phase-file plan to a cook agent expecting straightforward field + handler changes. Scout read the FE and found: the existing `Signature` field is a PNG hand-drawing widget, NOT cryptographic. The verify-at-ingest flow was missing the signature material entirely — no way to get bytes + cert into the form fill. Surfaced to the user instead of coding blind. User scope-expanded to both payload modes + a new `DigitalSignature` field type (leaving the PNG field untouched). A second scout found `<SigningPrompt>` + Viettel/VGCA adapters fully reusable → the feared "FE crypto widget" complexity was mostly wiring.

**Lesson: a one-line FE step in a BE-focused plan masks whole missing subsystems. Scout the FE before trusting the brief.**

Delegated implementation (04c + 04d) to a fullstack agent. Both marked tasks "done" + "all-green." I re-ran the entire slnx + test suites empirically every slice. Caught: (a) a unicode curly-quote injected into controller string literal (would compile, break verify routing), (b) a code-reviewer-flagged bug where mode-parser threw `InvalidOperationException` on mistyped config (should be `JsonException` → 500 leak); fixed with explicit `ValueKind` guards + tests. Delegated-green does not mean verified-green.

**Lesson: verify subagent claims empirically; don't trust pass-claim without running the suite yourself.**

## Technical Details

### Canonicalization (Mode-1 payload = form content)
- Determinism was the top risk: FE & BE must emit byte-identical bytes or every signature false-rejects.
- Chose: ordinal-sorted keys, null-omit, NFC, `UnsafeRelaxedJsonEscaping` (match JS), UTF-8.
- Pinned with fixed-vector hex tests that ARE the FE contract (Mode-1 and Mode-2 share one field-selection helper → structurally cannot drift).
- Payload = canonical-JSON-bytes; Provider's `VerifyAsync` hashes internally (`SHA256.HashData(Payload)` then `VerifyHash`).

### Mode-2 (server document render)
- Payload = SHA-256 hash of `{templateId, version, canonicalFormData}` deterministic projection.
- Determinism: projection is JSON + version (not timestamps, not randomness).
- New endpoints: `POST /forms/{formId}/signature-doc-hash` (auth + public variants).
- Controller routes mode via `int mode` param; `ValidationRulesJson` stores mode; default Mode-1 on missing.

### Migration & Evidence Schema
- 6 new nullable columns: `SignatureStatus`, `SignaturePayload`, `SignedAt`, `SignedByUserId`, `CertificateIssuer`, `CertificateThumbprint`.
- Write-once pattern: `RecordSignatureEvidence` method (idempotent on null-state).
- Hand-authored migration (Designer snapshot alone insufficient for Designer → .Designer.cs rebuild; copied snapshot + transformed header + moved `[Migration]` attr off main to avoid CS0579).
- Backward-safe: no data loss, no breaking reads, old signed=NULL evidence.

### Hard-Block Invariant
- Both submit handlers (auth + public) call verify **before** `_context.SaveChangesAsync()`.
- Verify fails (IsValid=false or exception) → immediate 400 BadRequest, no DB write.
- Failure details (payload validation, crypto error) logged; response generic (no subsetting).

## What We Tried

1. **Initial scope (Mode-1 only)** — accepted by user; Mode-2 deferred to keep slice size sane.
2. **Tried storing mode in a new migration column** — backed out; reused `ValidationRulesJson` (SRP: rules hold mode). Saner dependency.
3. **Payload hashing trade-off**: Raw PDF bytes (non-deterministic) vs document projection (deterministic). Chose projection (avoids PDF toolchain complexity, deterministic by design, user controls canonicalization + versioning).
4. **Where to house the field-selection helper?** Both modes need it. Avoided duplication by making it a single static helper in `DigitalSignatureHelper` (tight coupling acceptable within Forms.Application; cross-module client doesn't expose this detail).

## Root Cause Analysis

The plan was BE-first but did not assume "sign What?" was already answered. It was not. FE had a signature widget but zero cryptographic context. The brief ("verify-at-ingest") is a backend concern that hides a frontend requirement: "provide the thing to verify." Typical tension when feature work spans modules and someone (user) owns scope but documentation (the plan) only owns half.

Subagent delegations worked well on the coding but the pass/fail claims were self-judged and off-base (both claimed all-green; both had bugs). Empirical re-run is now table-stakes for any delegated implementation on this team.

## Lessons Learned

1. **Scout the FE before signing off on a BE plan.** If the story touches two modules, assume the brief is incomplete until you see wire-to-wire.

2. **Resolve load-bearing crypto unknowns empirically before designing the contract.** I read `UsbTokenSignatureProvider.VerifyAsync` code directly (does it hash internally? does it expect precomputed?). That fixed Mode-1 Payload=content and Mode-2 Payload=doc-hash — a self-consistent pair, not a guess.

3. **Canonicalization determinism IS the feature.** Pinned with fixed-vector hex tests. Mode-1 and Mode-2 share one field-selection helper to prevent accidental drift. Don't relax this.

4. **Per-slice commit cadence on multi-day features.** 4 independently-green, independently-reviewed, independently-committed slices (04a foundational, 04b integrity-core, 04c Mode-1, 04d Mode-2). Each builds /warnaserror 0E + tests + review before commit. Bisect-friendly for blame.

5. **Verify delegated claims empirically.** Subagent self-mark + pass-claim is not verification. I re-ran slnx + test suites on every slice (takes ~6 min per slice; caught 2 real bugs). Non-negotiable.

6. **State low-risk decisions; only ask genuine forks.** I chose Mode-1-first (user approved Mode-2 deferred), reuse ValidationRulesJson (no new column), content-projection hash (sidestep PDF non-determinism). Only asked the user the genuine trade-offs. Kept velocity.

## Next Steps

**Remaining** (carried forward as `FORMS-DIGSIG-FE-REMAINING`):
- **04e**: FE `DigitalSignature` field type + signing modal + payload threading (byte-match-against-BE-spec is where determinism gets validated).
- **04f**: Docs (update `dependency-map.md` + `code-standards.md` crypto section) + end-to-end manual smoke checklist (sign real form via Viettel OTP / VGCA USB).

**Unresolved**:
- FE field-type contract needs empirical validation against Mode-1 + Mode-2 hex vectors (test data not yet ported to FE test suite).
- Live OTP / USB signing has not been exercised post-refactor (risk: mismatched provider endpoint, config drift post-Viettel Mysign work).

## Metrics

- Lines: 4 slices, 6 files touched (Forms.Application handlers + DTOs + migration, Identity.Contracts interface, Identity.Signing InProcess impl + tests).
- Build: 0 errors, 0 warnings under `/warnaserror`.
- Tests: Forms.Application 258/258, ModuleIsolation 12/12, MigrationConsistency 2/2 (all green).
- Review: 4 slices, APPROVE per slice (initial Review C1 minor: CS0579 duplicate-migration-attr; fixed).
- Pushed: origin + bitbucket, ahead=0.
