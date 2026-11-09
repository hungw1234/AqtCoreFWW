# /Files Sign-Button Provider-Eligibility Gate (Option C)

**Date**: 2026-07-02 18:45
**Severity**: Medium
**Component**: Files module (FE), signing workflows
**Status**: Resolved

## What Happened

User reported "Ký số thất bại: Signing certificate is not bound to the assigned signer" 403 error when clicking the PAdES sign button in `/files`. Looked like a binding validation bug in `PadesTransactionInitiator.cs:137`. Initial hypothesis: BE data mismatch or broken enrollment logic.

## The Brutal Truth

The relief and embarrassment of realizing this wasn't a code defect at all — just a missing UX gate. The frustration is that the BE behaved correctly (fail-closed, guard worked), but FE offered a button to users who could NEVER satisfy it. We shipped incomplete features to the UI without verifying eligibility upfront. That's a design discipline failure, not a code failure.

## Technical Details

**Error origin**: `PadesTransactionInitiator.cs:137` → `SignerBindingGuard.NotBound` → 403.

**Empirical findings** (verified via sqlcmd + live API logs + routing scout):
- User `admin@dev.local` has ONE enrollment: ApprovalStatus=2 (Approved), IsActive=1, VGCA Organization cert (serial 785E…).
- Mysign login presented a DIFFERENT cert (Viettel-issued, different serial/issuer/thumbprint).
- No matching Approved enrollment for the Mysign cert → NotBound (correct).
- **Routing discovery**: `/files` has TWO separate sign buttons:
  - "Ký số" (USB VGCA): providerKey=`active-usb-vgca`, uses `DocFlow` strategy, **NO binding guard**, client-side signing.
  - "Ký số có đóng dấu (PAdES)": hardcoded providerKey=`remote-viettel-mysign`, uses `PadesAssemblySigningStrategy`, **RUNS binding guard**, server-side signing.
- **Capability routing**: PAdES strategy requires `ServerSideSigning AND NOT ClientSideSigning`. USB can never route to PAdES. Button visibility was unconditional — offering PAdES to users without an active+approved Mysign enrollment.

## What We Tried

1. Checked BE code — guard logic was sound.
2. Checked DB enrollments — expected Mysign cert missing.
3. Traced live request to real Mysign API → confirmed cert mismatch.
4. Scouted button routing logic — discovered capability-exclusive paths and hardcoded provider keys.

## Root Cause Analysis

**Not** a code bug. **Root defect**: FE offered both buttons without eligibility checks. Conditional rendering is YAGNI-failure — we shipped the button before wiring the guard.

- USB "Ký số" works because `DocFlow` strategy doesn't require enrollment binding (cert is local, user-supplied).
- PAdES "Ký số có đóng dấu" REQUIRES server-side remote Mysign with matching active+approved enrollment.
- No FE gate checked this precondition, so button was offered to everyone.

## Lessons Learned

1. **Fail-closed BE ≠ complete feature.** A 403 that's "correct" still needs UX gating. Debug discipline (DB + live logs + routing) separated data-config mismatch from code defect and prevented unnecessary refactoring.

2. **Capability-exclusive routing demands eligibility gates.** When two buttons have different provider requirements, gate BOTH by capability + enrollment status. This applies to any multi-signing-mode UI.

3. **Mirror BE guard predicates in FE gates exactly.** The binding guard checks `active AND Approved`. Initial FE gate only checked `Approved`, which would allow revoked (IsActive=0) certs to re-introduce the 403. Code-reviewer caught this. **Pattern**: when FE gate mirrors BE guard, replicate ALL predicates, not the obvious half.

4. **Visible-stamp PAdES is remote-only by design.** USB VGCA embeds signature client-side (no local stamp tool). Do NOT duplicate placement. Server-side stamp is Mysign-exclusive.

## Next Steps

1. **Verify `SignatureOrchestrator.VerifySignerBindingAsync` callers** (appears unreferenced). Remove if dead.
2. **PAdES button hardcodes `remote-viettel-mysign`** — if a 2nd remote provider ships, revisit provider selection logic (do NOT hardcode again).
3. **Gate both buttons in `file-list-page.tsx`**:
   - USB: enabled iff `ClientSideSigning` capability.
   - PAdES: enabled iff `ServerSideSigning` AND active+approved Mysign enrollment.
   - Disabled → tooltip explaining why.

**Tests**: file-list-page vitest 11/11 (incl. revoked-cert gate test). eslint + tsc clean.
**Shipped**: commit `[TBD via git-manager]`, plan `260702-1351-files-sign-buttons-provider-eligibility-gate/`.
