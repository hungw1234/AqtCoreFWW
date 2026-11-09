# SMS Recipient Was a Guid, and Fixing It Armed a Log Line That Had Been Safe for Weeks

**Date**: 2026-07-21 15:10
**Severity**: High
**Component**: Notifications — `SendNotificationCommandHandler`, `WebhookSmsProvider`, Identity module client
**Status**: Resolved

## What Happened

Row `NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE` — the SMS twin of a bug whose email half shipped 2026-07-17. `SendNotificationCommandHandler` was calling `RecipientUserId.ToString()` and handing that string straight to the SMS provider as the destination. Every SMS send failed at the provider, silently, because a Guid is not a phone number and nothing in the pipeline checked.

The row's prescribed fix was: add `PhoneNumber` to `UserInfoDto`, extend the Dapper projection that backs it. Didn't do that. `GetUserInfoByIdsAsync` feeds 25 display-name enrichment call sites, including two SignalR broadcast handlers, and phone number is Confidential PII under QĐ 742 §66. Widening that DTO would have sprayed decrypted PII across 24 callers that never asked for it, for the benefit of the one that did. Shipped a separate PII-scoped lookup method instead — `UserInfoDto` untouched, zero of the 25 consumers edited.

Two more things surfaced during the slice, neither in the row.

## The Brutal Truth

The row telling me the wrong fix isn't the interesting failure — rows go stale, that's known. What's uncomfortable is *why* it was wrong: it encoded whoever filed it's mental model of "obviously you add the field to the DTO," and that model looked reasonable in isolation and only fell apart once I actually counted the blast radius. If I'd cooked this row literally instead of scouting first, I'd have shipped a PII leak into a SignalR broadcast path, closed the row as done, and it would have looked completely fine in review because nothing about "add PhoneNumber to UserInfoDto" screams danger by itself.

The part that actually stung: making SMS work meant a log line at `WebhookSmsProvider.cs:27` and `:31` — `{PhoneNumber}` at Information and Error — went from harmless to a plaintext Confidential-PII sink on every send. That line has existed since `a7919abf7`. It was never touched this slice. It was safe purely because its argument had always been a Guid. I fixed the argument, and in doing so I turned dead code that logged garbage into live code that logs a real phone number. Serilog's `SecretMaskingDestructuringPolicy` does not catch this — checked the regex, it matches property *names* ending in `(Password|Key|Token|Secret|Vault|HmacKey|FieldKey)$`, and `PhoneNumber` matches none of that; more fundamentally `TryDestructure` only inspects complex objects, and a string argument in a message template never reaches it at all. This is the third time this exact shape has bitten: 2026-07-17, fixing the email recipient armed a dormant cross-tenant phishing primitive that a Guid-crash had been keeping inert (`feedback-fixing-a-bug-can-arm-a-dormant-hole`). I did not go looking at `WebhookSmsProvider.cs` on my own — the slice plan didn't touch it, so I didn't read it. Code review did, and caught it. Masked to last-4 at the call site.

The third thing is smaller but still cost real time: the scout report said "only one implementation of `IIdentityModuleClient` exists; no stubs or test doubles need updating." Build failed with 8 CS0535 errors from 4 hand-rolled `StubIdentityClient` classes in Workflow integration tests that don't implement the new interface member. The scout had grepped for implementations and stopped — it never compiled anything, so it never saw the stubs that only surface as a build error, not a grep hit. Second scout-overconfidence miss this session; the first was a migration-directory claim earlier today. Grep answers "does this exist" — it does not answer "does this compile."

## Technical Details

- Fix: new PII-scoped method on the Identity module client returning `PhoneNumber` only for the caller that needs it, distinct from `GetUserInfoByIdsAsync` / `UserInfoDto`. `SendNotificationCommandHandler` now calls the scoped method for SMS destination resolution.
- `WebhookSmsProvider.cs:27,31` — `{PhoneNumber}` template arg replaced with a last-4 mask at the call site (`***-1234` shape); no change to `SecretMaskingDestructuringPolicy` itself, since that policy structurally cannot see a string argument.
- Decrypt path proved by mutation, not just green tests: swapped `decryptor.Decrypt(...)` for the raw ciphertext column, test went red, restored, went green. A mocked unit test would never have observed the decrypt boundary at all — and the entire point of this slice was to stop shipping ciphertext (or a Guid) as an SMS destination, so this was the one assertion that actually mattered.
- Code review flagged making `tenantId` a required (not optional) parameter on the new lookup as "strongly recommended hardening." Checked before accepting rather than taking the label at face value: `identity.AspNetUsers` has **no RLS policy at all** — absent from `RlsPolicyPresenceTests` — so the SQL tenant predicate in this method is the *only* tenant isolation on that read path. An optional parameter would have been one omitted call-site argument away from a cross-tenant PII read. Verifying turned "nice to have" into "load-bearing"; made it required.
- Filed, not fixed: `NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL` — `MailKitEmailSender.cs:37,41` logs `{To}` in full. Same shape as the SMS finding, pre-existing, but *not* armed by this change (email recipient resolution already worked before today), so left out of scope deliberately rather than scope-creeped in.
- Build 0 errors after the stub fix. Notifications.Application 39/39, Notifications.Infrastructure 16/16, Architecture 185/185, Identity.Integration 2/2, Workflow 6/6 touched tests green.
- Code review: DONE_WITH_CONCERNS — 1 Critical (the log line), 1 High (the optional `tenantId`), both applied in-slice before close.

## What We Tried

Followed the row's literal instruction first mentally, then scouted the 25 call sites of `GetUserInfoByIdsAsync` before writing any code — that's what redirected the approach to a scoped method instead of a DTO field. Did not, on my own initiative, open `WebhookSmsProvider.cs` to check for logging — the slice's mental scope was "handler + Identity client," and the provider file wasn't in that scope, so it got skipped until review forced the look. That's the gap: "not in scope" and "not affected by this change" are different claims, and I conflated them again despite having written the exact lesson down five days ago.

## Root Cause Analysis

Same underlying failure as `feedback-fixing-a-bug-can-arm-a-dormant-hole`, twice now in one week: a fix that makes previously-invalid data valid changes the risk profile of every downstream consumer of that data, including ones the slice never planned to touch, and a crash or garbage value can be silently doing the job of a guard that was never written. The scout miss is a different root cause — grep-only verification of an interface-implementation claim, no compile step to confirm it, no search for hand-rolled test doubles (which by definition won't show up as "implementations" in a design-pattern-aware search unless the search also checks build output).

## Lessons Learned

Before shipping a fix that converts previously-inert data (a Guid standing in for a phone number, a crash-before-send) into valid data, grep every consumer of the field being fixed for logging, not just for functional callers — a `{PropertyName}` in a log template is a consumer too, and it doesn't show up when you're scanning for business logic. Second time writing this exact sentence in five days; it needs to become a standing pre-ship checklist item for any "make X valid" fix, not something re-derived from a review catch each time. Second: when a scout says "only one implementation exists," that claim covers what grep can see — it does not cover hand-rolled test doubles, and it is not verified until the build actually runs. A scout report that hasn't triggered a compile is a hypothesis, not a fact, on anything involving interface contracts. Third: when code review calls something "hardening," check what it's actually guarding before accepting or downgrading the label — here "hardening" was load-bearing because the underlying table had zero RLS coverage, and that's not visible without checking `RlsPolicyPresenceTests` directly.

## Next Steps

Row closed, build/tests clean, review concerns applied in-slice. Not committed yet per this session's instruction — git-manager owns the commit and should cite `Closes: NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE` per the standing commit-message rule. One follow-up filed and deliberately left open: `NOTIFICATIONS-EMAIL-ADDRESS-LOGGED-IN-FULL` (T2/T3, same shape as the SMS finding, pre-existing and not armed by this change). Open question for whoever picks up that follow-up: is there a systematic way to grep "PII-typed property used as a Serilog message-template argument" across the repo, rather than finding these one call site at a time via review — the destructuring-policy defense only covers complex objects, so every string-valued PII field logged as a template arg is currently invisible to it.
