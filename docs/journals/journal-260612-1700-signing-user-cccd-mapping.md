# Journal — Per-user CCCD signing-identity mapping (P1-P4)

**Date:** 2026-06-12 · **Slice:** `/cook plan feature map CCCD` (4 phases) · **Commits:** `413487d9` (P1) · `b49f8f88` (P2) · `783453f8` (P3) · `07e65a9b` (P4) — 13 ahead, not pushed (CI parked) · **Plan:** `260612-1450-signing-user-cccd-mapping`

> **Origin:** the Viettel Mysign sign-smoke (`/ck-debug`) failed with HTTP 404 `59009 "User ID does not exist"` because the BE sent the app-user **Guid** as Viettel's `user_id`, but Viettel UAT is keyed on the signer's **CCCD** (`017091004878`). No app-user→CCCD mapping existed; `DefaultUserId` was dead (`request.UserId` always non-null). User chose the production-correct fix (encrypted per-user CCCD + admin UI) over a dev hack.

## Headline — root-cause was 2-layer, the fix is a feature not a patch
The debug found two faults: (1) Viettel creds were placeholders in user-secrets (ops, user fixes), and (2) **no identity mapping** — the controller unconditionally sends `ResolveUserId()` (the Guid) as the provider user_id, and the `?? DefaultUserId` fallback was unreachable. Layer 2 = a real cross-cutting feature.

## Architecture decisions (locked by user)
- **Storage = encrypted-at-rest column** `ApplicationUser.SigningNationalId` (CCCD = Confidential PII, QĐ742). Reused `EncryptedStringConverter` (`v{N}:` prefix) — same conditional dual-ctor-branch shape as PhoneNumber/TotpSecret (DRY, zero new crypto).
- **Set-value = full admin UI** at BOTH create + edit (user upgraded scope from edit-only).
- **Sign resolution localized to the Application handler** (KISS): handler injects `UserManager`, resolves `cmd.UserId`(string)→`user.SigningNationalId`, passes **CCCD-or-null** to `SignRequest.UserId`. 🔴 **Key insight:** passing null instead of the always-non-null Guid **re-arms the provider's existing `?? DefaultUserId` fallback** — so ZERO provider change needed (planner verified `ViettelMysignProvider.cs:95` on live code). Audit snapshot keeps the **Guid** actor; CCCD never enters the audit JSON (unit-test asserts non-vacuously: Guid present AND CCCD literal absent).
- **PII read-gating = `User.Update`** (user chose least-privilege over the reviewer's "User.View is fine"). GetById field-gates: `IAuthorizationService.AuthorizeAsync(User, User.Update)` → null the CCCD for View-only callers. List views never populate it (Dapper can't decrypt anyway).

## Per-phase + the bugs reviews caught
- **P1** (entity+encryption+migration): hand-authored migration `20260612150000` + `.Designer.cs` (Copy-Item snapshot → transform header, avoids 1400-line round-trip) + snapshot. `MigrationConsistencyTests` 2/2. Review APPROVE 0-block.
- **P2** (sign-resolution + create/edit + validation + gating): review **APPROVE_WITH_CONCERNS** → fixed **H1** (`UserDto` docstring claimed a non-existent "PII masked by controller" — a false security claim; corrected + implemented the actual `User.Update` gate) + **M1** (contract test `NoSensitiveFieldsExposed` didn't guard the new PII field → added assertion). Filed L1 (pre-existing `OrgUnitId` update no-op) + MessagePack NU1903.
- **P3** (FE form): review **REQUEST_CHANGES** → **C1 critical data-loss** I missed: the clobber guard only covered the *loading* window, not *error/empty*. `editUser` comes from the LIST row (CCCD always null); if the GetById detail fetch FAILS, the field is empty with save *enabled* → full-replace PUT wipes the stored CCCD. 🔴 **Fix insight:** the FE `isError` gate is the *correct* layer — it distinguishes "user cleared the field" (load succeeded, empty) from "load failed" (don't save), **preserving the clear-CCCD feature** that a BE absent-means-unchanged contract would break. Gate = `disabled: isEdit && (detailLoading || detailError)` + error Alert. Filed `CCCD-PUT-ABSENT-MEANS-UNCHANGED` as WONTFIX-leaning defense-in-depth.
- **P4** (tests): encryption round-trip 3/3 (clone of `PhoneNumberEncryptionRoundtripTests` — resolved the planner's open-Q: `WebAppFixture` wires the real `IKeyResolver`, so the `v{N}:` assertion is **non-vacuous**) + GetById HTTP 1/1 (SystemAdmin bypass → decrypted CCCD E2E). View-only gating integ deferred (needs View-but-not-Update role seeding; test-auth shortcut only models SystemAdmin full-bypass) → follow-up.

## Verification (final)
slnx build **0W/0E** (NuGet-audit off; pre-existing MessagePack NU1903 is the only failure, filed) · App unit **300/300** · contract **3/3** · **full Identity integration 249/249** (no regression incl. 88 signing) · FE **1009/1009** · tsc 0 · MigrationConsistency 2/2.

## Reusable lessons
- 🔴 A grep on a backlog/plan claim ≠ ground truth — scout BOTH sides (BE provider key + capabilities AND FE registry) surfaced the registration bug the row didn't mention (prior VIETTEL-FE-RAW-CERT) and the identity-mapping gap here.
- 🔴 "docstring claiming enforcement = test-of-truth" fired again (H1): the `UserDto` comment promised masking that didn't exist. Correct the claim to reality immediately.
- 🔴 Field-level PII gating belongs at the **controller** (has the principal), not the Application query — `AuthorizeAsync(User, <permcode>)` reuses the registered policy + handler (DRY) rather than re-injecting `IEffectivePermissionService`.
- 🔴 Anti-clobber for full-replace PUT of an optional field: gate the UI on *successful* detail load, not just "not loading" — the error window is the real hole, and the FE gate preserves intentional-clear semantics that a BE contract change would destroy.
- 🔴 MessagePack NU1903 surfaced as a NEW slnx-/warnaserror-breaker since 2026-06-10 (advisory drift) → use `-p:NuGetAudit=false` to isolate real compile from dep-vuln noise; filed as a CI-re-enable blocker.

## Carry-over / follow-ups filed
- `VIETTEL-PENDINGTX-USERID-FROM-CCCD` (T3): async-callback `Guid.TryParse(request.UserId)` now fails on a CCCD → Guid.Empty pending-tx; source from ExtraParameters when async enabled.
- `VIETTEL-CCCD-LOG-PII` (T3): provider logs `userId`=CCCD plaintext (`:118,133,169,174,204`) → mask/hash.
- `CCCD-PUT-ABSENT-MEANS-UNCHANGED` (T4, WONTFIX-leaning), `IDENTITY-UPDATEUSER-ORGUNIT-NOOP` (T4), `BE-NUGET-MESSAGEPACK-NU1903` (T2, blocks CI re-enable).
- **Prerequisites for the actual Viettel smoke (ops, user):** set Viettel creds user-secrets (ClientId `EofficeVDB_0101382193`, ProfileId `adss:ras:profile:001`, ClientSecret from config txt) + add Viettel NEAC root to trust store (coexist VGCA idx1) + restart API (applies migration) + set the test user's CCCD via the new admin form. This unblocks `260612-1023` Phase 2.
