# ADR-007: MFA TOTP Dual-Storage (AspNetUserTokens + Encrypted Backup Column)

**Date:** 2026-05-02
**Status:** Accepted
**Deciders:** AqtCoreFW Development Team
**Supersedes:** none
**Related:** Phase-03 QĐ 742 ATTT cấp 4 (`plans/260428-1740-cap-4-qd-742-compliance/phase-03-mfa-policy-ui.md`)

## Context

Phase-03 implements TOTP-based MFA enrolment (QĐ 742 §1.6.a). Two storage locations
end up holding the same secret material:

1. **AspNetUserTokens** — the row written by ASP.NET Identity's
   `IUserAuthenticatorKeyStore` implementation (LoginProvider `[AspNetUserStore]`,
   Name `AuthenticatorKey`). This row is what `UserManager.GetAuthenticatorKeyAsync`
   reads and what `SignInManager.TwoFactorAuthenticatorSignInAsync` validates.
2. **`ApplicationUser.TotpSecret`** — a custom encrypted column added by the Phase-03
   migration `20260501151652_AddMfaPolicyAndUserMfaFields`. Round-trips through
   `EncryptedStringConverter` so plaintext never lands in DB pages or backups.

The original Phase-03 commit (`59cce7d2`) wrote *only* to `TotpSecret` and the
validate path read *only* from `AspNetUserTokens` — the two halves never met,
breaking enrol-then-login end-to-end (caught post-hoc by code review,
session 260502-0702).

We considered three options:

- **A — Drop `TotpSecret`, AspNetUserTokens only.** Cleanest, KISS. Loses an
  encrypted backup, ties audit/key-rotation tooling to the Identity table layout.
- **B — Drop AspNetUserTokens, custom column only.** Forces re-implementing
  `IUserAuthenticatorKeyStore` and `IUserTwoFactorStore` to point at our column.
  Misses out on `SignInManager.TwoFactorAuthenticatorSignInAsync` which expects
  the standard token store.
- **C — Dual-write.** Keep both. Identity is the live validate path; the custom
  column is an encrypted backup readable by audit and key-rotation tooling
  without coupling to Identity internals.

## Decision

**Adopt option C — dual-write.** AspNetUserTokens is the **primary** validate-path
store; `ApplicationUser.TotpSecret` is the **encrypted backup**. Both must agree
at all times for an enrolled user.

### Write contract

`MfaService.CompleteTotpEnrolAsync` (Phase-03):

1. Verify the supplied TOTP code against the cached pending secret.
2. `_userManager.SetAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", pendingSecret)` — primary.
3. `_userManager.SetTwoFactorEnabledAsync(user, true)` — required for `SignInManager.RequiresTwoFactor` to fire on cookie login.
4. Set `user.TotpSecret = pendingSecret` (encrypted on persist) — backup.
5. Set `user.TotpEnrolledAt = DateTime.UtcNow`.
6. `_userManager.UpdateAsync(user)` — single round-trip flushes the entity changes.

We deliberately use `SetAuthenticationTokenAsync` (public on `UserManager`) instead
of `SetAuthenticatorKeyAsync` (only protected on `IUserAuthenticatorKeyStore`).

### Clear contract

`ApproveEmergencyDisableCommandHandler` (Phase-03 emergency disable, QĐ 742 §1.6.b):

1. `userManager.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey")` — deletes the AspNetUserTokens row outright. (We deliberately do *not* use `ResetAuthenticatorKeyAsync`, which would generate a new random replacement that is still installable.)
2. `userManager.SetTwoFactorEnabledAsync(user, false)` — flag off so `SignInManager` skips MFA branch even if a stale key somehow lingers.
3. Null `TotpSecret`, `TotpEnrolledAt`, `RecoveryCodesHash`, `MfaGraceUntil` on the entity.
4. `userManager.UpdateAsync(user)`.

### Validate contract

`MfaService.ValidateTotpAsync` and AuthServer `MfaChallengeController` continue
reading **only from AspNetUserTokens** via `_userManager.GetAuthenticatorKeyAsync`.
The encrypted backup column is never on the hot validate path — keeping cost low
for every login.

### Regression guard

Integration test
`MfaFlowTests.EnrolThenValidate_RealFlow_BothStorageBackendsAgree` exercises
enrol → assert AspNetUserTokens key matches → assert `TotpSecret` matches → assert
`TwoFactorEnabled=true` → assert `IMfaService.ValidateTotpAsync` returns true.
This is the canonical guard against any future contributor re-introducing the
dual-storage drift bug.

## Consequences

### Positive

- Standard ASP.NET Identity flows (cookie + ROPC + recovery via `SignInManager`) work unmodified.
- Encrypted column survives Identity table schema migrations independently — useful for audit replay or future key-rotation tooling that doesn't want to depend on `[AspNetUserStore]` token-store internals.
- Single integration test guards both halves.

### Negative

- **Two storage rows must stay in sync.** Mitigation: writes happen inside one logical operation (no separate commits) and the regression test asserts both. If a future bug splits them, the test fails.
- **Failure mode if `UpdateAsync` fails after `SetAuthenticationTokenAsync` succeeds.** We accept this — `SetTwoFactorEnabledAsync` is the gate for `SignInManager.RequiresTwoFactor`; if `UpdateAsync` fails it's also `false`-by-default until set. We log compensating-action context in the error log; an operator can re-trigger enrolment. We deliberately do NOT wrap in `IDbContextTransaction` — Identity's token store may live in a separate transaction scope (and the failure rate of in-memory `UpdateAsync` is negligible on a healthy SQL Server).
- **Migration backfill not provided.** This template repo has no production users (memory `aqtcorefw-template-dev-only`); downstream projects that adopt Phase-03 after a prior Identity-only enrolment must run a one-shot script copying `AuthenticatorKey` → `ApplicationUser.TotpSecret`. Not in scope for this template.

### Neutral

- Future per-tenant MFA policy (currently `MfaPolicy` is global role-based, no `TenantId`) does not interact with dual-storage. If H5 multi-tenancy lands later, both storage halves apply per-user regardless of policy scope.

## Alternatives Considered (and rejected)

- **Option A** — drop backup column. Rejected because user explicitly wanted the encrypted backup retained for audit/rotation and to keep the entity self-describing without joining `AspNetUserTokens`.
- **Option B** — custom IUserAuthenticatorKeyStore. Rejected because losing `SignInManager.TwoFactorAuthenticatorSignInAsync` would force re-implementing cookie MFA challenge handling, multiplying surface area.
- **Single-write (current state pre-fix)** — rejected as broken; was the bug.

## References

- Phase-03 plan: `plans/260428-1740-cap-4-qd-742-compliance/phase-03-mfa-policy-ui.md`
- Code-reviewer report: `plans/reports/code-reviewer-260502-0702-phase-03-posthoc.md`
- Affected files:
  - `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/MfaService.cs`
  - `src/modules/identity/AqtCoreFW.Identity.Application/MfaEmergency/ApproveEmergencyDisableCommand.cs`
  - `tests/integration/AqtCoreFW.Identity.Integration.Tests/MfaFlowTests.cs`
- Microsoft Identity tokens reference: AspNet `UserStore<TUser>.SetAuthenticatorKeyAsync` writes to AspNetUserTokens with InternalLoginProvider=`[AspNetUserStore]` + Name=`AuthenticatorKey`.
