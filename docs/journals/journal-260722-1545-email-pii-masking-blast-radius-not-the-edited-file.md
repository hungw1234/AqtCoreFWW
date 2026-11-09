# Email PII Masking: the Blast Radius Was Never the File the Row Named

**Date**: 2026-07-22 15:45
**Severity**: High
**Component**: Notifications — `MailKitEmailSender`, `SendPasswordResetEmailHandler`, `AccountAutoDisabledNotificationConsumer`, new `PiiLogMasking` (SharedKernel/Compliance)
**Status**: Resolved

## What Happened

Row `NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL` (T3, filed 2026-07-21 during the SMS slice) named one
sink: `MailKitEmailSender.cs:37,41` logs the recipient email in full on both the success and failure
paths. Email is Confidential PII under QĐ 742 §66, encrypted at rest by the same converter that protects
PhoneNumber — the log sink held plaintext for a column the DB deliberately encrypts. This defect has been
live since the sender was written, not armed by a recent change like the SMS leak; wide exposure, but no
sudden trigger.

Code review (H1) found the same defect class live in two sibling call sites the diff never opened:
`SendPasswordResetEmailHandler.cs:30` and `AccountAutoDisabledNotificationConsumer.cs:69-70,81-82`. The
row named one file. The defect lived in three.

Fixed by extracting a shared `PiiLogMasking` helper — `MaskEmail` (keep first local-part char + full
domain: `user@gov.vn` → `u***@gov.vn`, domain kept intact for tenant-domain triage) and `MaskPhone`
(last-4). Also retired two independently-written near-duplicates into the one helper: the
just-written internal `MailKitEmailSender.MaskEmail` and the pre-existing `WebhookSmsProvider.MaskPhoneNumber`
from 2026-07-21 — three copies of the same idea was the threshold the reviewer flagged, and DRY says
consolidate once you're past two accidental reimplementations, not before.

## The Brutal Truth

I scoped the slice to the one file the row named and would have called it done. The row's defect
*description* was accurate — it just wasn't complete, and I took "accurate" as license to stop reading.
This is the fourth time in six days this shape has bitten (SMS recipient row 07-21, alert test-button
07-21, now this) and it's starting to feel less like bad luck and more like a default failure mode: read
the row, fix the line, ship. The row is a hypothesis about where the bug lives, not a map of where it
lives.

What makes this one distinct from the recent run is the direction of the mistake. 2026-07-21's SMS fix
*armed* a dormant hole by making previously-inert data valid. This one is the inverse: fixing only the
named file would have left two structurally identical holes sitting right next to it, untouched, still
leaking the same PII class through the same failure mode — a `{Email}` or `{To}` argument in a Serilog
message template. Same lesson, opposite mechanics: blast radius isn't where you edit, it's everywhere the
sensitive value travels to a log call. A password-reset handler and an account-auto-disabled consumer both
handle a recipient email on the way to a log line, and neither is anywhere near `MailKitEmailSender` in
the call graph — you only find them by grepping for the *data*, not by reading the file the row pointed
at.

## Technical Details

- `PiiLogMasking.MaskEmail`/`MaskPhone` — new public static class, `SharedKernel/Compliance`. 18 new edge
  tests: empty string, no `@`, single-char local part, multi-`@`, short/long phone, non-digit input — all
  asserting exact masked output, not just "doesn't throw."
- Masking is enforced at the call site, not in `SecretMaskingDestructuringPolicy`, because that policy
  structurally cannot help here: it matches property *names* against a suffix regex and only inspects
  complex objects via `TryDestructure` — a scalar string argument in a message template (`{Email}`,
  `{To}`) never reaches it. Confirmed this on the SMS slice yesterday; held on re-check today.
- 5 email log sinks masked total: `MailKitEmailSender.cs:37,41` (the named file), `SendPasswordResetEmailHandler.cs:30`,
  `AccountAutoDisabledNotificationConsumer.cs:69-70,81-82`.
- Consolidation: `MailKitEmailSender.MaskEmail` (written this slice) and `WebhookSmsProvider.MaskPhoneNumber`
  (07-21) both deleted, replaced with calls to `PiiLogMasking`.
- Tests: SharedKernel.Tests 233/233 (+18 new), Notifications.Infrastructure.Tests 16/16,
  Notifications.Application.Tests 39/39, Architecture.Tests 185/185, build 0 errors.
- Code review: DONE — H1 (the two extra sinks) surfaced and fixed in-slice, no residual Critical/High.

## What We Tried

Started by editing only `MailKitEmailSender.cs`, matching the row's literal scope. Code review pushed back
before this went further — asked whether `{Email}`/`{To}`-shaped template arguments existed anywhere else
in Notifications. Grepping for the recipient-email data path (not the file) surfaced the reset handler and
the auto-disabled consumer immediately; both were sitting in files with zero textual relationship to
`MailKitEmailSender.cs`, which is exactly why a file-scoped read never found them.

## Root Cause Analysis

The row's title named a symptom location, and I treated the symptom location as the fix boundary. The
actual boundary is the data-flow of the sensitive value — every log call the recipient email string
reaches — and that boundary doesn't respect module or file structure. This is the INVARIANT 260522-A shape
applied to code scope instead of backlog-row status: a row can be accurate about what it names and still
be an incomplete map of the defect class it's pointing at.

## Lessons Learned

For a PII-in-logs finding, the audit unit is the *field*, not the *file*. Grep the sensitive property name
(`Email`, `PhoneNumber`, whatever the row names) across every log call in the owning module before
touching the one file the row cites — three sinks found here, two the row never mentioned. Second: hitting
three independent implementations of the same masking idea in two days (`MailKitEmailSender.MaskEmail`,
`WebhookSmsProvider.MaskPhoneNumber`, now a shared one) is a sign this pattern needed a shared primitive
from the first occurrence, not the third — next PII-masking need should reach for `PiiLogMasking` before
writing a local helper.

## Next Steps

Row closed, all three sinks fixed, tests and review clean. Left open, repo-wide: whether call-site masking
(current approach, now centralized in `PiiLogMasking`) or a Serilog enricher is the long-term answer — the
scalar-argument gap in `SecretMaskingDestructuringPolicy` means the enricher path is structurally blocked
until that policy is reworked to intercept template arguments, not just complex-object properties. Not
blocking; `PiiLogMasking` is the canonical call site helper until someone closes that gap. Not committed
yet — git-manager owns the commit and should cite `Closes: NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL`.

Status: DONE
