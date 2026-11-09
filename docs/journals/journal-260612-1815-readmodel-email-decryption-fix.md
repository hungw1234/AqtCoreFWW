# Journal — Read-model email decryption (edit-form "Email không hợp lệ")

**Date:** 2026-06-12 · **Flow:** `/ck-debug` → fix (BE root) · **Trigger:** admin edit-user form rejected save with "Email không hợp lệ", Email field showing `v1:...` ciphertext · **Status:** implemented, tested (~856 green), reviewed APPROVE_WITH_CONCERNS (doc note applied), not committed.

## Headline — one root cause, two manifestations, two choke points
Dapper read paths return the AES-GCM-encrypted `AspNetUsers.Email` column **verbatim** — `EncryptedStringConverter` only runs on the EF/`UserManager` path. So every read-model surface of user email showed ciphertext.

- **Manifestation B (reported):** edit form prefilled `editUser.email` (list-row ciphertext) → antd `{ type:'email' }` rule fails → save blocked. (Email field is `disabled` + not in the PUT body — purely the validation rule blocked it.)
- **Manifestation A (co-symptom):** user list table renders the same ciphertext (truncated).

**Quy kết:** pre-existing (P3 diff only added the `signingNationalId` line; `email: editUser.email` is older). The CCCD Step-E guidance (open edit form to set CCCD) surfaced it. NOT a regression from the CCCD feature.

## Investigation turns (the hypothesis flipped twice)
1. First guess "GetById returns encrypted email" — **wrong**. GetById uses `UserManager.FindByIdAsync` → EF → converter → **decrypted**. It's the **list** (Dapper) that leaks.
2. "Fix 4 handlers" — **collapsed to 2**. 3 of the 4 (delegations/files/org) don't `SELECT Email` directly; they enrich via `IIdentityModuleClient.GetUserInfoByIdsAsync`. The real choke point is that client (Dapper `SELECT ... Email` → `UserInfoDto.Email`), feeding ~10 consumers (collab ×5, workflow ×2, files, delegations, org). So root = **2 points**: `ListUsersQueryHandler` (direct) + `GetUserInfoByIdsAsync` (fan-out).
3. Boundary check: `ExternalIdentities.Email` + `LoginAttempts.Email` have NO `HasConversion` → plaintext → out of scope. `FindByEmailAsync` selects NO Email (hash-keyed) → no leak.

## Design (user chose "fix root BE read-model" over FE-localized)
- **Layering constraint:** `EncryptedStringConverter` + `IKeyResolver` live in `Infrastructure`; `ListUsersQueryHandler` is in `Application` (no Infrastructure ref). Decrypt primitive was `private static`.
- **Mechanism:** new `IEncryptedFieldDecryptor` (SharedKernel) ← impl in Infrastructure wrapping `IKeyResolver`; refactor `EncryptedStringConverter.Decrypt` → `public static DecryptString` so converter + decryptor share **one** crypto path (DRY, no new crypto). DI singleton next to `IKeyResolver`.
- **Applied:** `ListUsersQueryHandler` + `InProcessIdentityModuleClient.GetUserInfoByIdsAsync` inject the decryptor. `UserInfoDto.Email` is init-only → **rebuilt** the DTO (not mutated) to keep the contract immutable.
- **Free fix for B:** list now returns plaintext → `editUser.email` valid → edit form saves. Zero FE change.

## Reviews caught
- **Blast-radius (the risk):** all 10 `GetUserInfoByIdsAsync` consumers verified **display-only** (`FullName ?? Email` fallbacks; dict key is `UserId` Guid). None used `.Email` as key/equality/hash → decrypting is safe.
- **Medium (doc-only, applied):** catch handles only `FormatException`/`CryptographicException`; `KeyNotFoundException` (Vault down) **deliberately propagates** = fail-loud. Added a comment so a future maintainer doesn't widen the catch to `Exception` and mask a key outage behind ciphertext display.

## Reusable lessons
- 🔴 **Dapper read-model + EF value converter = silent ciphertext leak.** Any `SELECT <encrypted-col>` via `IReadDbConnection` bypasses the converter. When a PII column is encrypted-at-rest, audit every Dapper read that surfaces it.
- 🔴 **Trace enrichment to its choke point before fanning out a fix.** 4 flagged handlers collapsed to 1 cross-module client + 1 direct query. Fixing the client fixes ~10 consumers at once.
- 🔴 **The EF/UserManager path decrypts; the Dapper path doesn't** — the asymmetry is the whole bug. GetById "just worked" (EF) which is why the first hypothesis was backwards.
- 🔴 **Expose the crypto once.** Refactoring `Decrypt`→`public static DecryptString` kept a single AES-GCM path; the read-model decryptor reuses it rather than copying crypto.

## Verification
slnx build 0W/0E (`/warnaserror`, NuGetAudit off) · EncryptedFieldDecryptor 4/4 · Identity.App 300/300 · Identity.Infra 169/169 · **Identity.Integration 251/251** (2 new non-vacuous email-decrypt with live `IKeyResolver` + 11 regression incl. CCCD/tenant/groups). ~856 total green.

## Follow-ups filed
- `USERS-EMAIL-SEARCH-LIKE-INERT` (T4, pre-existing): `ListUsersQueryHandler:31` `u.Email LIKE @Search` matches the encrypted column → email substring search never hits. Equality already moved to `NormalizedEmailHash`; substring search needs a different approach (out of scope here, surfaced by the reviewer).
