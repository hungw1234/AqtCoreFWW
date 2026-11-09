# Journal — Harden Mysign status/error parsing against numeric tokens (VIETTEL-SIGNHASH-STATUS-NUMERIC-RISK)

**Date:** 2026-06-12 · **Flow:** `/cook <backlog-row>` · **Status:** shipped, build 0W/0E, 56/56 Signing unit, code-review DONE_WITH_CONCERNS → H1 fixed in-slice. Commit `5aac3caa` (not pushed).

## Why — a filed risk, audited then confirmed real-ready
Backlog row flagged that `MysignSignHashResponse.Status` (`string?`) is deserialized via a `JsonOpts` lacking `NumberHandling`. The `expires_in` incident earlier today PROVED Viettel returns numbers where its spec says strings. If `/requests/status` returns `status` as a numeric token, STJ throws `JsonException` on `$.status` and breaks the async poll loop. `AllowReadingFromString` does NOT cover this (it is string-token→number-property, the reverse).

Audit (INVARIANT 260522-A) verified ground-truth: `MysignDtos.cs:81` `Status string?`; `ViettelMysignStatusPollClient.cs:29` JsonOpts has no `NumberHandling`; `MysignStatusMapper.Map` switches on `"1"/"4000"/...` string literals. Risk is latent (async-poll config-off; sync success returns `signatures` not `status`) but the code path exists and is one config flip from active — verdict **(d) real-ready**, not YAGNI, because the vendor's number-as-string habit is empirically established.

## Fix (`5aac3caa`) — property-level converter, status + error scope
- New `FlexibleStringJsonConverter : JsonConverter<string?>` — reads String OR Number (or true/false/null) → string.
- Annotated `[JsonConverter(...)]` on `MysignSignHashResponse.Status` + the three `error` fields (login/certinfo/signhash). User chose status+error scope over status-only (error codes like "58100" are the same numeric-string class).
- **Property-level, not per-client JsonOpts** — covers both `ViettelMysignSignHashClient` and `ViettelMysignStatusPollClient` automatically (each owns a separate JsonOpts; only login's had `NumberHandling`). `MysignStatusMapper` untouched (still string switch) — least churn.
- Scoped out: `signatures`/`transactionId` (base64/GUID, never numeric), `error_description` (prose).

## Code-review caught a real self-inflicted regression (H1) — fixed in-slice
First cut read numbers via `GetInt64()` else `GetDecimal()`. Reviewer flagged: `GetDecimal()` throws `FormatException`/`OverflowException` (NOT `JsonException`) on an out-of-decimal-range token → escapes both clients' `catch (JsonException)` → **re-introduces the exact poll-loop break the fix targets.** Low real-world likelihood (codes ≤6 digits) but a genuine contract violation. Rewrote the Number arm to read the **raw literal** from `reader.ValueSpan` (multi-segment via `BuffersExtensions.ToArray`) — never parses, never throws, and is more faithful ("4000" stays "4000", no culture/overflow concerns). Added tests: fractional `1.5`→"1.5" and a 40-digit number → raw string, no throw.

## Reusable lessons
- 🔴 **A "tolerant parse" converter must not itself throw a non-`JsonException`.** STJ special-cases only `JsonException`/`NotSupportedException`; anything else propagates past `catch (JsonException)`. When the whole point is "never break the loop", read the raw token (`ValueSpan`) rather than `GetInt64`/`GetDecimal`.
- 🔴 **Property-level `[JsonConverter]` beats editing N per-client JsonOpts** when the same DTO is deserialized by multiple clients with divergent options — one annotation, every path covered, scoped to exactly the at-risk field.
- 🔴 **`ReadOnlySequence<T>.ToArray()` is ambiguous under LINQ/Immutable usings** (CS0411) — call `BuffersExtensions.ToArray(in seq)` explicitly.

## Verification
build 0W/0E `-warnaserror` · 56/56 Signing unit (9 converter incl. 40-digit-no-throw + fractional; 5 DTO-level on real `MysignSignHashResponse`/`MysignLoginResponse` incl. `MysignStatusMapper` consumption) · existing string-path tests unaffected (no regression to the working sync flow) · code-review all acceptance criteria met after H1 fix.

## Open
- Async-poll modes still not exercised against live UAT (config-off). The converter makes the parse robust whether `status` arrives numeric or string; the actual async OTP-poll smoke remains future work if/when those modes are enabled.
