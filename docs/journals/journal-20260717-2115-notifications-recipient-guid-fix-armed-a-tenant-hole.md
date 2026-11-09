# Notifications recipient fix — closing one bug armed a dormant tenant hole

**Date:** 2026-07-17 (~20:15–21:10 ICT)
**Severity:** High (security finding, fixed in-slice)
**Component:** `Notifications.Application.SendNotificationCommandHandler` / `NotificationsController` /
`InProcessIdentityModuleClient`
**Status:** Resolved
**Commits:** `8fef0db92` (fix email resolution), `9b6ca196a` (security: tenant binding), `292a717b3` (docs)
**Branch:** `fix/notifications-resolve-recipient-email` — not merged/pushed
**Plan:** `plans/260717-2015-notifications-recipient-guid-not-address/plan.md`

## What Happened

Backlog row `NOTIFICATIONS-RECIPIENT-IS-GUID-NOT-ADDRESS` (EMAIL half; filed by me a few hours
earlier from the alerting slice, see previous journal) — `SendNotificationCommandHandler.cs:67`
passed `request.RecipientUserId.ToString()` straight to `IEmailSender.SendAsync`, so MailKit threw
a `FormatException` on `MailboxAddress.Parse` for every single email notification, ever. Fixed by
resolving the address through `IIdentityModuleClient.GetUserInfoByIdsAsync` before dispatch. Scope
grew from 3 files to 5 mid-slice after `code-reviewer` found two High findings on pass 1.

## The Brutal Truth

This is the part worth being honest about: **fixing this bug armed a cross-tenant phishing hole
that had been sitting there, inert, the whole time.** `NotificationsController.Send` binds
`TenantId` from the request body while the other four actions on the same controller (`:34, 46,
62, 75`) all use `ResolveTenantId()`. Before my fix that inconsistency didn't matter — the handler
mailed a Guid, MailKit threw, nobody got anything. After my fix, the handler resolves a real,
decrypted mailbox address from whatever tenant the *caller* put in the JSON body. Anyone holding
`Notif.Msg.Send` could pick an arbitrary `TenantId`, send to any `RecipientUserId`, and get a
working phishing delivery — plus a 200-vs-422 oracle for whether that user exists in that tenant.

I didn't introduce this. It was pre-existing and silent because the bug I was fixing was
accidentally acting as a kill switch. The reviewer's phrasing stuck with me: I hadn't asked "what
is this crash currently protecting?" — I'd only asked "how do I stop the crash?"

## Technical Details

- `InProcessIdentityModuleClient.cs:83-85` has a loose tenant filter — `AND (TenantId IS NULL OR
  TenantId = @TenantId)` — justified in its own comment as *"not a leak — caller already holds the
  UserId and the preceding join scoped by tenant."* My new caller (`SendNotificationCommandHandler`
  via the controller) is the first one where **both** `UserId` and `TenantId` are attacker-chosen.
  The comment's precondition held for every existing caller; it does not hold for mine.
- `ApiControllerBase.cs:21-32` already has a legitimate cross-tenant path for SystemAdmin via the
  `X-Tenant-Id` header, so binding from `ResolveTenantId()` costs zero real capability — it moves
  cross-tenant sends from "anyone with the send permission" to "SystemAdmin only", matching every
  sibling action on the controller.
- Second High finding (unrelated to security, same review pass): my plan's own risk table claimed
  "callers handle `Result.Fail`". False. `InProcessNotificationModuleClient.cs:25` returns a bare
  `Task` and discards the Result — fire-and-forget — and both live event-handler callers ignore it
  too. My fix as first written would have swapped a loud `FormatException` in the logs for total
  silence: the exact silent-drop shape the alerting slice (`journal-20260717-1933-...md`, same day,
  ~2 hours earlier) had just spent a whole slice removing. Fixed by adding `LogWarning` before
  `MarkAsFailed` on the resolution-failure branch.

## What We Tried

- Trusted the scout report's claim that `UserInfoDto` carries phone and that `Email` is not
  encrypted at rest. Both false, and the report's own pasted DTO contradicted the phone claim (no
  `PhoneNumber` field). Verified against source before writing any Dapper query: `Email` **is**
  AES-GCM encrypted at rest, decrypted only inside `InProcessIdentityModuleClient.cs:105`. Had I
  trusted the scout and hand-rolled a "no new ProjectReference" Dapper read to avoid the
  `Identity.Contracts` dependency, I'd have mailed ciphertext to every recipient.
- Considered a new Presentation test project for the 5 tenant-binding tests. Rejected — repo has
  zero `*.Presentation.Tests` projects; standing one up for a single controller binding is scope
  creep. Landed them in `Architecture.Tests`, which already references `Notifications.Presentation`.
  Filed `NOTIFICATIONS-PRESENTATION-TESTS-PLACEMENT` as an open decision rather than silently
  picking a permanent home.
- Worried hardest about the early-return on resolution failure skipping the UoW commit, losing the
  persisted row entirely. Reviewer refuted it from source: `NotificationRepository.cs:29-39` calls
  `SaveChangesAsync` itself in both `AddAsync` and `UpdateAsync`; there is no transaction pipeline
  behavior (`Program.cs:140-142` registers only TenantAware/Validation/Audit). The row survives as
  `Failed`, not vanished. Worth recording a wrong worry, not just the right calls.

## Root Cause Analysis

Two distinct root causes stacked in one slice:

1. **The controller's tenant binding was already inconsistent with its four siblings** — a
   copy-paste or early-draft leftover that never got caught because the code path downstream of it
   was permanently broken (MailKit throwing on every call). The bug I fixed was, unintentionally,
   load-bearing security theater.
2. **A safety-justifying comment (`InProcessIdentityModuleClient.cs:83-85`) encoded an assumption
   about callers, not an invariant the type system enforces.** It was true when written. It stopped
   being true the moment a new caller violated its precondition, and nothing forced a re-check.

## Lessons Learned

- **When a bug is silently protecting something because it crashes before reaching the protected
  code, fixing the bug arms whatever it was hiding.** Ask "what's currently safe only because this
  is broken?" before landing a fix that makes a dead path live again — especially anything touching
  auth/tenant scoping downstream of the change.
- **A comment justifying a loose filter is a contract with the callers that exist at the time it
  was written, not a guarantee.** Every new caller of `GetUserInfoByIdsAsync` (or any function whose
  safety comment cites "the caller already does X") needs that precondition re-verified, not
  assumed.
- **Verify a subagent's factual claims against source when the claim is self-contradictory** — the
  scout report asserted a DTO field it had itself pasted as absent. That kind of internal
  contradiction is a free tripwire; don't skip past it because the rest of the report reads fine.
- **Old tests asserting the buggy behavior (`SendNotificationCommandHandlerTests.cs:97,113`
  expecting `RecipientId.ToString()`) are not safety nets — they're locks on the bug.** Turning them
  red was the correct outcome, not a regression signal, because nothing in the mock chain
  (NSubstitute accepts any string) could have caught the real defect in the first place.

## Next Steps

- **SMS is still broken** (`:69` still passes the Guid as a phone number). Not fixable with the
  current `UserInfoDto` — it has no `PhoneNumber` field, so this needs a public-contract change.
  Filed `NOTIFICATIONS-SMS-RECIPIENT-IS-GUID-NOT-PHONE`, split out by user decision. Its test is
  kept and annotated as intentionally pinning a known defect.
- **`NOTIFICATIONS-PRESENTATION-TESTS-PLACEMENT`** — decide whether tenant-binding/controller
  behavior tests get a real `*.Presentation.Tests` home or stay parked in `Architecture.Tests`.
  Owner: next session with test-infra time to spare.
- Open question carried from plan: is `ApplicationUser.PhoneNumber` encrypted at rest like `Email`?
  Not checked this slice — blocks the SMS follow-up until answered.
- Open question: does any legitimate caller need cross-tenant send *without* being SystemAdmin? The
  fix assumes not. If that assumption is wrong, `ResolveTenantId()` needs a documented escape hatch
  instead of the blanket restriction just shipped.

**Verification:** build `src/AqtCoreFW.slnx` 0W/0E · arch tests **185/185** (was 180, +5) ·
`Notifications.Application` 35/35 (was 30) · `Workflow.Application` 642/642 · `Forms.Application`
344/344 · code-review: pass 1 REQUEST_CHANGES (2 High) → both fixed → pass 2 APPROVE_WITH_NITS.
Non-vacuity by mutation (each reverted): restoring `RecipientUserId.ToString()` → email test red ·
hoisting the lookup above the channel switch → inapp + tenant tests red · re-trusting the body
tenant → 4 of 5 security tests red · reviewer independently killed a 5th mutant by removing
`MarkAsFailed()` on the early-return path. Closure basis: local verification only — CI is off by
standing user choice for this repo phase.

## Unresolved Questions

1. Is `ApplicationUser.PhoneNumber` encrypted at rest like `Email`? Blocks the SMS follow-up.
2. Do the 5 tenant-binding tests belong in `Architecture.Tests` long-term, or does the repo need a
   `*.Presentation.Tests` project?
3. Does any legitimate caller need cross-tenant send without SystemAdmin? Fix assumes no.
