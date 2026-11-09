# Files Sign Buttons: Enrollment-Driven Gate Correction

**Date**: 2026-07-02 18:20  
**Severity**: Medium  
**Component**: Files module / Signing UI  
**Status**: Resolved

## What Happened

6th ship of the day corrected the 5th ship (commit 84684d28). Initial fix gated the two /files page sign buttons (`sign-uploaded-file-usb` + `sign-uploaded-file-pades`) on tenant's active provider capability (via `useSigningProviderInfo → hasCapability`). User rejected the diagnosis with explicit directive: **"người dùng đăng ký loại ký nào thì ưu tiên hơn tenant"** — the user's OWN enrollment status must decide, not tenant config. This correction implements enrollment-driven gates: `canUsbSign = active + Approved UsbTokenVgca enrollment`; `canPadesSign = active + Approved RemoteViettelMysign enrollment`.

## The Brutal Truth

The frustration here isn't the initial gate — it's that we shipped a "data/config mismatch" diagnosis with high confidence without source-proving the BE contract. User's push-back forced a proper debug: actually reading `SignatureOrchestrator.RequestSignatureAsync` + `IdentityInfrastructureRegistration` revealed the BE has zero tenant-config coupling. USB signing is unconditionally DI-registered; DocFlow resolves provider purely from client-supplied `providerKey` via `SigningStrategyRouter`. The gate was pure over-restriction. The real win: a user who didn't accept a confident-but-wrong answer + proper debug discipline that turned opinion into fact.

## Technical Details

**Old gate (commit 84684d28, now reverted):**
```typescript
// file-list-page.tsx
const { hasCapability } = useSigningProviderInfo();
<Button disabled={!hasCapability('ClientSideSigning')} /> // USB button
<Button disabled={!hasCapability('ServerSideSigning')} />  // PAdES button
```

**Source verification** (BE actually has NO tenant-config check):
- `SignatureOrchestrator.cs:36` → `SigningStrategyRouter.ResolveStrategy(providerKey)` → resolves purely from client input
- `IdentityInfrastructureRegistration.cs:211` → USB provider (`UsbTokenVgca`) registered unconditionally
- `DocFlowSigningStrategy` → no `SignerBindingGuard` on USB vs remote

**New gate (FE-only, deliberate UX tightening):**
```typescript
// file-list-page.tsx + sign-uploaded-file-modal.tsx
const { userEnrollments } = useUserEnrollments();
const canUsbSign = userEnrollments.some(e => 
  e.type === 'UsbTokenVgca' && e.isActive && e.approvalStatus === 'Approved');
const canPadesSign = userEnrollments.some(e =>
  e.type === 'RemoteViettelMysign' && e.isActive && e.approvalStatus === 'Approved');

// USB modal now pins providerKey='usb-vgca' instead of reading tenant provider
// (else it'd send remote-viettel-mysign + docFlow → SIGNING_ROUTE_UNSUPPORTED)
```

**Files touched:**
- `web/src/features/files/file-list-page.tsx` — dual enrollment-driven buttons
- `web/src/features/files/sign-uploaded-file-modal.tsx` — fixed providerKey pin + removed dead "provider unavailable" branch
- 3 test files: 30/30 vitest pass across (file-list-page.test.tsx, sign-uploaded-file-modal.test.tsx, file-actions-toolbar.test.tsx)

**Validation:**
- vitest 30/30 (3 files, all pass)
- tsc app + tsc test — zero errors
- eslint — clean
- code-reviewer: `DONE_WITH_CONCERNS` (Low findings only; all cleaned)

## What We Tried

**Option A** (rejected): Assume tenant config was the source of truth; add fallback to user's primary enrollment if tenant disabled signing.
- Problem: conflates two separate concerns (org policy + user capability)
- User feedback made clear: user enrollment IS the policy here

**Option B** (accepted): Replace tenant-config gate with enrollment-based gate.
- Source-verify BE contract (USB + remote both unconditional in routing)
- PIN providerKey in USB modal to prevent route-unsupported errors
- Gate reflects user's actual binding, not org default

## Root Cause Analysis

**Why the initial gate was wrong:** We inferred a BE coupling that didn't exist. `useSigningProviderInfo` reads tenant-level capability, but the BE's `SignatureOrchestrator` never consults `TenantSignatureConfig` to authorize a signing request — it just routes by `providerKey`. The FE gate was stricter than necessary AND wrong (disabled USB when tenant had remote-only config, even if user had USB enrollment).

**Why source-verification matters:** A 30-second grep of `SignatureOrchestrator` + `IdentityInfrastructureRegistration` killed the "config mismatch" hypothesis. But we shipped without that 30 seconds. The user had to reject it for us to do the work.

## Lessons Learned

1. **Before mirroring a BE guard on the FE, source-prove the BE actually enforces the coupling.** Not all FE gates need to be less strict than the BE, but they must be based on what the BE actually checks.
2. **An FE gate can legitimately be stricter than the BE** (this one is: requires Approved enrollment, which DocFlow doesn't check). But the tightening must be intentional UX policy, not defensive misunderstanding.
3. **User feedback that rejects a confident diagnosis is a gift.** It forces proper debug instead of rationalization. The USB + remote gates now reflect reality: user's enrollment status, not org config.
4. **Enrollment coupling is fundamental here.** USB requires active + Approved enrollment in the user's own profile; remote requires active + Approved Viettel Mysign. This is independent of tenant policy. FE gates should reflect this.

## Next Steps

- Commit includes files only (web/src/* + test files)
- Code reviewer approved at Low severity; all findings cleared
- All test suites green
- Next work: finalize git commit + push (git-manager handles)

Status: DONE
