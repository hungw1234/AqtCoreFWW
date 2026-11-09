# Phase 0 — Signing-core Security Remediation (GATING)

**Date:** 2026-06-15 · **Plan:** `plans/260615-1804-document-signing-foundation/phase-00-security-remediation.md`
**Commits (NOT pushed):** H3 `be7c0264` · C2 `7cd044bc` · C1 `013b074e`

## What & why

Three real security defects in the **shipped** signing-core that the document-signing
foundation silently assumed were solid. All three gate PDF signing. Each verified
empirically against current code before any change (red-team report cited stale file
paths — `SignPayloadCommandHandler.cs` had become `SignPayloadCommand.cs`; the defects
themselves were all confirmed TRUE). Shipped as 3 independently-green commits.

## Slice 1 — H3 atomic nonce (`be7c0264`)

`SigningDocFlowNonce.TryUse()` was an in-memory `UseCount++` with no DB guard → parallel
uploads with the same JWT double-spent the nonce. Fix: `RowVersion` optimistic-concurrency
token on nonce + record (mirrors `SigningPendingTransaction`), controller maps
`DbUpdateConcurrencyException` → 401, plus a `CHECK (UseCount <= MaxUses)` defence-in-depth.
Preserved the deliberate fetch-storage-before-commit ordering. Persistence-level concurrency
test (two DbContexts, real conflict) — no MinIO/HTTP needed.

## Slice 2 — C2 DocFlow tenant-isolation (`7cd044bc`)

`[AllowAnonymous]` round-trip with no tenant in the JWT, record/nonce loaded with no tenant
assert, FE-supplied `OriginalStorageKey` unvalidated, uploaded bytes stored as "signed" after
only a client content-type check, no RLS on the signing tables. Fix: signed `tenant_id` JWT
claim + explicit `record/nonce.TenantId == jwt.tenant` asserts (primary control, works under
db_owner) + RLS on both tables (defence-in-depth) + storage-key tenant-prefix validation +
server-side `%PDF` magic-byte sniff. Full crypto + cert-bound PDF verify deferred to the
Phase-6 sandbox (BE must not parse untrusted PDF here).

## Slice 3 — C1 cert==assigned-signer (`013b074e`)

The marquee anti-impersonation invariant was vaporware: remote fell back to a configured
default account when the signer had no CCCD (misattributed signature); USB/detached verify
accepted ANY chain-valid cert. Fix: new `SignerCertEnrollment` store (serial+issuer/thumbprint
= forge-resistant key) + RLS + admin enroll/list API; `SignerBindingGuard` (returns a verdict,
never throws) = enrolled-key match → CCCD-from-subject bootstrap → else fail-closed; remote
fails closed on missing national id + binds the returned cert (sync + deferred confirm); Forms
verify-at-ingest binds the cert to the authenticated submitter (anon public-form path has no
signer → skipped).

## Lessons

- **Stale-path red-team findings still verified TRUE** — audit-at-slice-start (INVARIANT
  260522-A) re-located the drifted file (`SignPayloadCommandHandler.cs`→`SignPayloadCommand.cs`)
  and confirmed all 3 defects empirically before touching shipped code.
- **EF migration discovery needs `[Migration]` — which lives in the `.Designer.cs`.** The C2 RLS
  migration first shipped WITHOUT a Designer → EF silently skipped it → policies never created →
  RLS tests RED with no error. The tester *hypothesised* "CREATE POLICY failed silently"; the real
  cause (verified by inspecting that ALL `[Migration(` attrs live in Designers + the RlsPresence DB
  was a fresh Testcontainers run) was the missing Designer. Generated it from the snapshot →
  discovered + green. **Raw-SQL-only migrations still need a Designer.**
- **Anon machine path + RLS:** the `[AllowAnonymous]` DocFlow callback has no tenant session
  context, so RLS alone can't isolate it (blocks-all under least-priv, allows-all under db_owner).
  The signed-JWT-claim + explicit code assert is the real control; RLS is defence-in-depth, and the
  controller sets SESSION_CONTEXT from the JWT so RLS engages under a future least-priv login.
- **Binding spans modules:** the USB/detached half of C1 had to wire into Forms verify-at-ingest
  (`IIdentitySigningClient.VerifySignatureAsync` gained `SignerUserId`, verify+bind made atomic) —
  C1 is not Identity-only. This makes authed form/remote signing fail-closed without signer
  provisioning (enrolled cert or matching `SigningNationalId`) — intended anti-impersonation posture.
- **Verify subagent claims:** code-reviewer caught a unit-test project (`Identity.Application.Tests`)
  that did not compile after the ctor change — the tester's earlier "all green" run had filtered to
  the integration project and missed it. Always run the full affected suite, not a filter.

## Follow-ups

orphan-blob sweep · least-priv DocFlow SESSION_CONTEXT hardening · enroll user-exists check ·
Forms digsig integration coverage · full crypto/cert PDF verify (Phase 6) · anon signing binding ·
pre-existing `Qd742Phase03ComplianceTests` MFA-claim arch failure (unrelated).
