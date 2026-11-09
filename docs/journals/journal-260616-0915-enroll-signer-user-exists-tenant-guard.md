# Journal — Enroll signer-cert user-existence + tenant guard

**Date:** 2026-06-16 09:15 ICT · **Slice:** `SIGN-ENROLL-USER-EXISTS-CHECK` (backlog T4) · **Mode:** `/cook` · **Type:** fix (BE, Identity)

## What

`EnrollSignerCertCommandHandler` now verifies the target signer exists **and** belongs to
the enrolling tenant before any insert. Previously it went dup-check → `AddAsync` with no
user lookup, so a SystemAdmin typo (the endpoint is SystemAdmin-only, with tenant-override)
produced an orphan enrollment row bound to no real user.

## Audit-at-slice-start (INVARIANT 260522-A)

Row was real-ready, confirmed empirically before planning:

- `EnrollSignerCertCommand.cs:44-67` — handler depended only on `ISignerCertEnrollmentRepository`;
  did a duplicate-active-cert check then `AddAsync`. No `FindByIdAsync`, no tenant assertion. Gap real.
- `LockUserCommandHandler` / `UpdateUserCommandHandler` — established the canon: inject
  `UserManager<ApplicationUser>`, `FindByIdAsync` → null → `NotFoundError`. Mirrored it.
- `SigningHandlerTests.cs` already had a `MakeUserManager(user)` substitute helper and **no**
  EnrollSignerCert coverage at all → the new tests slot straight in.

## Threat model — why the explicit tenant check is not redundant with RLS (rule #2)

The row called this "harmless to security; RLS scopes the row's own tenant" — true, the failure
is data-integrity (orphan row), not a cross-tenant write. But the fix still needs the explicit
`user.TenantId != cmd.TenantId` assertion, not just an existence check, because:

- The endpoint resolves tenant via `ResolveTenantId()`, which **supports a SystemAdmin override**.
- The RLS session-context is not guaranteed to be set to that override target, so a cross-tenant
  `FindByIdAsync` is not reliably nulled-out by RLS for this caller.
- The in-handler assertion makes the handler correct independent of session-context alignment.

A single `NotFound` masks both "missing" and "belongs to another tenant" — denying the SystemAdmin
caller a cross-tenant existence oracle. Maps to 404 via `ToApiResponse`; added the matching
`[ProducesResponseType(404)]` to `EnrollCert` for OpenAPI accuracy (behaviour already worked).

## Tests

5 `SigningHandlerTests` cases (the 5th — null-`TenantId` global user — added per code-review L1 to
cover the fail-closed branch the cross-tenant test didn't): in-tenant→enroll (AddAsync received);
missing→404 + DidNotReceive AddAsync; cross-tenant→404 + no insert; global-user null-tenant→404 +
no insert; duplicate-active→400 `SIGNER_CERT_ALREADY_ENROLLED` still fires after the new guard.

## Verification

- Identity.Application unit: 308/308 green (5 new). src `Identity.Application` 0 warnings.
- code-review APPROVE, 0 Critical/High/Medium.

## Out-of-scope nit (filed, not fixed here)

Build surfaced a **pre-existing** CS4014 at `SigningHandlerTests.cs:450` — the older
`SignPayload_UserWithoutCccd` test has an un-awaited `remoteProvider.DidNotReceive().SignAsync(...)`.
A fire-and-forget `DidNotReceive` on an async method is a latent false-green (the assertion may not
run before the test returns). One-char `await` fix, but it lives in an unrelated test method, so it
was kept out of this commit to stay focused and filed as `SIGN-ENROLL-TEST-CS4014-FALSEGREEN`.

## Lesson

When adding a "user must exist" guard, decide existence-only vs existence+scope by the *caller's
tenant authority*, not by whether RLS is present. An admin endpoint with a tenant-override can hand
the handler a `cmd.TenantId` that the RLS session-context doesn't match — so the scope assertion
belongs in the handler, and RLS is defense-in-depth, not the primary control. And mask
missing-vs-wrong-tenant behind one error to avoid a cross-tenant existence oracle.
