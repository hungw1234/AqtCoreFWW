# Signing Dev Enablement P1: Trust Store Architecture + VGCA Chain Verification

**Date**: 2026-06-12 10:23  
**Severity**: Medium  
**Component**: CoreFW.Signing (VGCA provider, PKIX validator, cert chains)  
**Status**: P1 Complete; P2-P4 Ready

## What Happened

Red-team + architecture review session clarified signing topology (SIGN delegates to provider, VERIFY self-contained in CoreFW); discovered cert chain is 3-deep (leaf→intermediate→root), exposed plaintext Viettel secret, and revised trust-store architecture to separate root-anchors from intermediate path-builders. Fixed OCSP to close chains on raw-path inputs. 1 commit ahead, NOT pushed.

## The Brutal Truth

We were operating on assumptions about delegation boundaries — code audit showed VERIFY is never delegated (PKIX+OCSP always local), but intermediate certs were being trusted like roots. The real pain: merging intermediates into the root store silently widens trust — pinning passes on intermediate, chain validates even if real anchor is absent. That's a subtle architecture smell that code review doesn't catch (both pools use same "trusted" nomenclature). Red-team caught it; fixing it required separating two data structures that had been conflated. Also: plaintext Viettel secret in `other/` was weeks old, untracked but still a P0 miss.

## Technical Details

### Cert Chain Discovery (verified via openssl)
VGCA p12 (`RSA3072_*.p12`, `-legacy` flag for RC2-40 CBC):
```
openssl pkcs12 -in RSA3072_*.p12 -legacy -nokeys -text
  → leaf: "Test RSA3072"
  → intermediate: "CA phục vụ các cơ quan Nhà nước G2"
  → root: "RootCA chuyên dùng Chính phủ G2" (self-signed)
```

Viettel CertInfo response (v1.9 API):
```
/certificates/info → returns chain of 3: end-entity + intermediate + issuer
FE only returns leaf cert → raw-path w/ intermediates:null can't close without path-builder
```

### Architecture Clarification
| Operation | Delegation | Why | Provider |
|-----------|-------------|-----|----------|
| **SIGN** | ✓ Always | key never held by BE | VGCA (wss://127.0.0.1:8987) or Viettel REST |
| **VERIFY** | ✗ Never | local PKIX + OCSP | CoreFW self-contained |
| **Path-build** | ✗ Never | requires stable intermediate pool | PKIX validator only |

VGCA USB provider: `UsbTokenSignatureProvider.SignAsync` throws NotSupported (desktop tool is user-local, unreachable by BE); VERIFY on USB tokens must happen client-side or via PKIX replay with archival re-verify (LTV/QĐ-742 requires independent re-verify per token lifecycle).

### SECURITY P0 Fixed
Plaintext `config_viettel_ca.txt` (ClientSecret visible) sat in `other/remote_signing_viettel/` untracked but discoverable. Added gitignore rules for config files + p12 + pass files. No history scrub needed (untracked).

### P1 Decision: Dual Trust Pools (VERIFIED)
**Red-team H1 finding:** Merged intermediates into `VnCaRootStore` makes them trust anchors → silent trust widening (cert under intermediate validates even if real root absent).

**Fix:** Two pools:
- `VnCaRootStore` (roots = trustAnchors only, pinned set, never intermediates)
- `VnCaIntermediateStore` (NEW, intermediates = path-building candidates in allCerts ONLY, never trustAnchors, never in pin-set)

Both pools live in validator code (no provider rewrites). M1: kept `VnCaRootStore` internal ctor (InternalsVisibleTo covers DI factory) — no public-promotion churn.

**OCSP fix:** was skipping on raw-path (issuer null when providers pass `intermediates:null`). Now: validator sources issuer from PKIX-built chain, OCSP runs on all paths.

## What We Tried

1. Validate Viettel config endpoints against `ViettelMysignProvider` code → confirmed match, no rewrite (P2 config-only).
2. Grep `SubmitFormCommandHandler` → zero signature wiring found (red-team C1: forms verify is NET-NEW, not "harden").
3. Verified hashAlgo flagged by red-team → code shows client defaults to SHA-256 OID (correct), downgraded from "fix" to "verified-settled."
4. Self-checked each red-team verdict empirically before revision (glob p12 dir, csproj InternalsVisibleTo, grep SubmitForm).

## Root Cause Analysis

**Architecture assumption leakage:** SIGN + VERIFY both sound like "delegation" — user expected both to delegate, code showed only SIGN does. Lack of separation between trust-anchors and path-builders allowed intermediate merging without visual friction (both live in "trusted" collections). Red-team was essential because this sits at the validator interface, invisible to API contract or handler test.

**Cert chain depth:** real Viettel + VGCA both 3-deep. FE returning leaf only is correct (leaf cert is the identity), but raw-path closure requires intermediate list. This is why Viettel CertInfo returns full chain (BE extracts, passes to validator).

**Secret exposure:** `other/` untracked but accessible to anyone with repo clone; config files ignored in dev-setup docs but never formalized in gitignore. P0 because ClientSecret is in plaintext, not environment-gated.

## Lessons Learned

1. **Delegation boundaries need explicit naming:** "Sign" vs "Verify" is clear; intermediate pools named like root stores create silent confusion. Use naming + docstring + separate types, not just logic isolation.

2. **Red-team verdict NEEDS-REWORK caught real architecture debt:** H1 (trust widening), C1 (net-new cross-module feature not "harden"), C2/H2 (chain depth). Self-verified each before revising (no blind audit-follow). This paid off because red-team was exploring threat surface, not just code smell.

3. **Code review catches line errors, not design rifts:** reviewer ran tests independently (didn't trust pass claim after earlier false-pass lesson), confirmed 17+169 green. Both implementer + reviewer verified hashAlgo logic (settled, not "wrong"), showing healthy skepticism.

4. **Verified-decision-sticky:** hashAlgo was VERIFIED in code (OID defaults correct) → downgraded from "fix" to "documented-settled", per review-audit §1. Didn't over-fix a non-bug.

## Next Steps

- **P1 NOT PUSHED** (1 ahead; CI parked since April per project state). Push when unblocked.
- **P2 ready:** Viettel config + CertInfo chain extraction; no code change, only seed config.
- **P3 ready:** Extract VGCA root+intermediate from p12; openssl already verified chain.
- **P4 big:** Forms verify-at-ingest (NEW cross-module feature, 2-3d). Requires P2+P3, IIdentitySigningClient contract, hard-block 400 on verify-fail, persist ChainResult JSON + DER.
- **Follow-up YAGNI:** VnCaIntermediateStore parse-skip could LogDebug (reviewer Low/Med nit, deferred).

**Owner:** P1 complete; P2-P4 planning/scope complete; P2 claims user's next session.
