# Viettel Signing: CCCD User ID Logged as Plaintext PII

**Date**: 2026-06-14 20:50
**Severity**: High
**Component**: Viettel Remote Signing / Logging / PII Compliance (QĐ742 §4.4.a)
**Status**: Resolved

## What Happened

Viettel's remote signing API uses the signer's 12-digit CCCD (Citizen Identity Card number — QĐ742 Confidential PII) as the `userId` parameter. This string was being logged directly in structured-log messages across 14 log sites: 10 in `ViettelMysignProvider`, 1 in `LoginClient`, 1 in `CertInfoClient`, 2 in `SignHashClient`. Log entries like `Logger.LogInformation("Attempting sign with {UserId}={UserId}", userId, userId)` would write the full 12-digit CCCD in plaintext to the log sink — violating QĐ742 §4.4.a (confidentiality requirement).

**Fix**: New internal `SigningLogMasking.MaskUserId(string?)` method masks the userId at all 14 log sites. Format: last 4 digits only (`079201001234`→`***1234`); ≤4 chars→`****`; null/empty→`(none)`. Applied uniformly across all Viettel clients.

## The Brutal Truth

This is frustrating because the backlog row **wildly under-stated the scope**. The original claim: "5 log sites in ViettelMysignProvider." Reality: 14 sites across 4 files — a 280% scope miss. Line-based grep pipeline (`grep '{UserId}' | grep log`) silently skipped multi-line log calls where the placeholder and the template are on different lines (e.g., `ViettelMysignSignHashClient.cs:162,190`). You have to explicitly run multiline-aware grep to catch those. I caught 10/14 on the initial pass, then had to go back with a proper sweep.

And the deeper frustration: we have a masking framework (`SecretMaskingDestructuringPolicy`) that already masks complex-object properties by name suffix (Password, Key, Token). It **does not** cover scalar string template args like `{UserId}`. So a new masker was needed — which is DRY-correct, but it revealed a framework gap. Should have been caught in the design review of the first signing integration.

## Technical Details

**Log sites swept:**
- `ViettelMysignProvider.cs`: 10 sites (RequestAsync, ExecuteAsync, HandleErrorAsync, etc.)
- `LoginClient.cs`: 1 site (OtpVerifyAsync)
- `CertInfoClient.cs`: 1 site (GetCertificateInfoAsync)
- `SignHashClient.cs`: 2 sites (SignAsync)

**Masking logic:**
```csharp
internal static string? MaskUserId(string? userId)
{
    if (string.IsNullOrEmpty(userId)) return "(none)";
    if (userId.Length <= 4) return "****";
    return $"***{userId[^4..]}";  // last 4 chars
}
```

**Unit tests (7/7):**
- 12-digit CCCD: `079201001234`→`***1234` ✓
- 5-char input: `12345`→`****` ✓
- 4-char input: `1234`→`****` ✓
- 3-char input: `123`→`****` ✓
- Empty string: `(none)` ✓
- Null: `(none)` ✓
- Boundary: last-4 residual entropy only ✓

**Scope decision (out of band):**
- Cache key `mysign:tok:…:{userId}` left unmaksed (process-local IMemoryCache, never persisted/logged → not a leak per reviewer).
- Log message templates themselves unchanged (e.g., still `"Attempting sign with {UserId}={UserId}"`; placeholder names are not PII) — only the *substituted values* masked. Dashboard/alerting tools see consistent template names; only the payload is masked.
- Follow-up filed `VIETTEL-VENDOR-ECHO-LOG-PII` (T4, latent): vendor's own error responses may echo CCCD if malformed request is rejected → monitor vendor `{Body}`/`{Error}` logs, but not blocking this slice.

## What We Tried

1. **Initial scan**: Grep for `{UserId}` + `log` in same line → found 10/14 (missed multi-line calls).
2. **Multiline sweep**: Explicit multiline-aware grep (`Grep multiline: true, pattern: 'logger\.Log\w+\([^)]*\{UserId\}')` → found all 14 ✓.
3. **Mask strategy**: Considered extending `SecretMaskingDestructuringPolicy` (complex-object filter), but `{UserId}` is a scalar template arg, not an object property → new scalar masker cleaner.
4. **Verification**: Unit tests for boundary cases (≤4 chars, null, empty, full CCCD).

## Root Cause Analysis

Three causes stacked:

1. **Backlog estimation decay** (INVARIANT 260522-A): Row was filed 5+ days ago, scope claim relied on manual inspection of one file. Repository evolved; log calls added; claim stale. **Empirical audit at slice-start would have caught this** — but the discipline to do it wasn't yet baked into the workflow.

2. **Grep pipeline blind spot**: Single-line grep + pipe filters work for contiguous patterns but silently fail on calls split across lines. No automated sweep validated completeness; manual inspection is fragile.

3. **Logging framework gap**: The existing `SecretMaskingDestructuringPolicy` covers complex objects (property-name suffixes), but template arguments are not objects — they're scalars in the template string. The gap should have been surfaced in the first signing integration design (Feb/early Mar).

## Lessons Learned

1. **Run empirical scope audit at slice-start** (INVARIANT 260522-A applied): Backlog rows decay. Don't trust the claim; probe with grep/file-exists on the exact identifiers. Found scope was 14, not 5.

2. **Use multiline-aware grep for code-structure patterns**: When sweeping for a pattern that may span a call (e.g., `logger.LogXxx(… {UserId})`), **always use `multiline: true`** in the Grep tool. Single-line piping is fragile.

3. **Logging framework design lesson**: Scalar template-arg masking is a distinct concern from complex-object property masking. Future signing integrations should plan for both upfront, not patch after the fact.

4. **Out-of-scope decisions need brief docs**: Decisions like "cache key left unmasked" and "template names unchanged" should be noted (prevents future dev from re-opening these as bugs). Added brief comments in code.

## Next Steps

- **Push commits**: `d3e03891` (fix) + `bc3c895c` (docs) → origin/main ✓
- **Monitor follow-up**: `VIETTEL-VENDOR-ECHO-LOG-PII` (T4) — watch for vendor error echoes; low urgency, no blocking data leak found yet.
- **Backlog hygiene**: Audit remaining rows >5d old before claiming (INVARIANT 260522-A); add multiline-sweep to our grep discipline for logging/template-pattern tasks.

## Verification

- Build `/warnaserror`: 0W/0E ✓
- `SigningLogMasking` unit: 7/7 boundary tests ✓
- `Identity.Infrastructure` suite: 217/217, no regression ✓
- Code review: APPROVE, 0 Critical/High, 2 Low advisory (residual entropy on last-4, vendor-echo — out of scope) ✓
