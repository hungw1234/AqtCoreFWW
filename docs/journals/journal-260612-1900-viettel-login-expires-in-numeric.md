# Journal — Viettel login `expires_in` numeric-vs-string parse fix

**Date:** 2026-06-12 · **Flow:** `/ck-debug` → fix · **Trigger:** Viettel sign smoke — "Login failed: Could not parse Login response: The JSON value could not be converted to MysignLoginResponse. Path: $.expires_in" · **Status:** fixed, tested 271/271, reviewed APPROVE_WITH_CONCERNS, committed.

## Headline — the spec lied; the wire is ground truth
After the CCCD + read-model-email fixes unblocked save + the edit form, the Viettel login HTTP call finally **succeeded** (no more 404) — but `System.Text.Json` threw on `$.expires_in`. `MysignLoginResponse.ExpiresIn` was `string?` (DTO comment even asserted "Viettel spec returns expires_in as a JSON string '3600'"). STJ in strict mode cannot bind a JSON **number** to a `string` property.

## Root cause — confirmed empirically, not by the spec
Made a **real UAT login curl** (login is not OTP-gated; only sign is) → exact shape:
```
access_token -> str (431 chars)
expires_in   -> int: 3393      ← JSON number, DTO expected string?
```
The response carries only those two fields on success — no other hidden numeric field. So a single field-type mismatch. The integration stubs returned `"expires_in":"3600"` (string) → tests were green while the real integration was broken: **a stub faithful to the assumed spec, not to reality.**

## Fix
- `MysignLoginResponse.ExpiresIn`: `string?` → `int?`.
- `ViettelMysignLoginClient.JsonOpts`: added `NumberHandling = JsonNumberHandling.AllowReadingFromString` → accepts a number AND defensively a stringified `"3600"`.
- `ParseExpiresIn(string?)` → `ParseExpiresIn(int?)`; dropped the redundant `int.TryParse`; defensive default + clamp(3600) + leeway + 30s floor preserved.
- Integration login stubs (`ViettelMysignSyncTests`, `ViettelMysignAsyncPollTests`): `"expires_in":"3600"` → `"expires_in":3600` so the full-flow tests now exercise the real shape (regression guard that would have caught this).
- New unit test `MysignLoginResponseParsingTests` — number / stringified / absent.

## Reusable lessons
- 🔴 **Vendor "all-strings" JSON specs are unreliable — verify on the wire.** Viettel's doc said `expires_in` is a string; UAT returns a number. When a vendor integration parse-fails, curl the real endpoint (when not OTP/2FA-gated) before trusting the DTO.
- 🔴 **A stub that encodes the assumed contract hides reality.** The login stub used the spec's string form → green tests, broken prod. Stubs for external APIs should mirror a captured real response, not the spec prose.
- 🔴 **`AllowReadingFromString` is one-directional** — it lets a numeric field read from a JSON string; it does NOT let a string field read from a JSON number. The number→string case needs a type change (or a custom converter). Knowing the direction matters for the next field (see follow-up).
- 🔴 **STJ stops at the first binding failure** — so confirm the FULL response shape (real call), don't fix only the field in the error and re-hit the next one.

## Verification
slnx build 0W/0E (`/warnaserror`, NuGetAudit off) · `MysignLoginResponseParsingTests` 3/3 · ViettelMysign integration (Sync+Poll+Callback) 17/17 with number stubs · full Identity integration 251/251 no regress. **271 total green.**

## Follow-up filed
- `VIETTEL-SIGNHASH-STATUS-NUMERIC-RISK` (T3, reviewer heads-up): same bug class one call away — `MysignSignHashResponse.Status` is `string?` (`"1"/"4000"/"4002"`) with a `JsonOpts` lacking `NumberHandling`. If UAT returns `status` as a number, the sync-block / poll loop throws identically on `$.status`. Evidence is OTP-gated → the next real sign smoke decides active-vs-theoretical. Fix (if active) needs a flexible `JsonConverter<string>` or numeric type + `MysignStatusMapper` rework (NOT `AllowReadingFromString`, wrong direction).
