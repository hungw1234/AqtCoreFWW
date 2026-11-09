# Alerting EMAIL channel — the "obvious" seam was broken, and my first guard was too

**Date:** 2026-07-17 (~17:20–19:20 ICT)
**Severity:** Medium
**Component:** `AqtCoreFW.Infrastructure.Alerting.AlertEvaluationJob` / `Notifications.Infrastructure` / `AqtCoreFW.Notification.Contracts`
**Status:** Resolved
**Commits:** `7494dea3d` (feat), `1e894ced5` (docs) — on branch `feat/p1-stable-alerting-email-channel`, not merged/pushed
**Plan:** `plans/260717-1720-p1-stable-observability-hygiene-alert-email/plan.md`

## What Happened

Backlog row `P1-STABLE-OBSERVABILITY-HYGIENE` (T4, "effort S, cheap hygiene") wanted the
`AlertEvaluationJob` EMAIL case wired to a real send — it was a `logger.LogWarning` stub while
LOG and WEBHOOK already worked (`AlertEvaluationJob.cs:167` pre-change). Shipped a new
`IAlertEmailSender` contract in `AqtCoreFW.Notification.Contracts` (zero-dep) plus
`AlertEmailSenderAdapter` in `Notifications.Infrastructure` delegating to the existing
`IEmailSender`/MailKit. Also added a `Non-Development + Email:Host empty → LogCritical, no send`
guard, since the config plumbing turned out to not exist in prod at all.

## The Brutal Truth

The backlog row's "effort S" assumed a working seam existed to route through. It didn't. The
"obvious" path — `INotificationModuleClient`, the one the stub's own comment pointed at
("delegated to notification module") — is not just unsuited here, it's **already broken for
everyone**: `SendNotificationCommandHandler.cs:67` passes `request.RecipientUserId.ToString()`
(a Guid) as the SMTP `to` address, and `MailboxAddress.Parse` throws on a Guid string. That email
path has never delivered mail to anyone, ever, on any caller. Same bug on the SMS path at `:69`.
I didn't go looking for this — I hit it trying to route through the seam the row assumed worked.

Then I nearly shipped the same class of failure I was fixing. Config Phase 3 as first written
added an `Email` section to `appsettings.json` and stopped there. `vault-init.sh` seeds an
"email pepper" (an unrelated login-hash salt), and `appsettings.Production.json` had no `Email`
section at all — so the base file's `localhost:1025` dev default would silently win in prod. I
would have shipped a slice whose entire purpose was "stop the silent alert drop" while relocating
the silent drop one layer down (log-warning stub → `LogCritical` against an unreachable
`localhost:1025`). `code-reviewer` caught it as H-1, REQUEST_CHANGES, and it was the correct call
— my own AC-5 wasn't met.

## Technical Details

- **Dead seam, pre-existing, both channels.** `SendNotificationCommandHandler.cs:66-67` — Email:
  `emailSender.SendAsync(request.RecipientUserId.ToString(), ...)`. `:69` — identical defect on
  SMS. Confirmed by scout, confirmed worse by `code-reviewer` (SMS half wasn't in my original
  finding).
- **Arch-test blind spot.** `ModuleIsolationTests.cs:22-122` — every rule is
  `Types.InAssembly(module.X)`, i.e. it polices modules reaching *out*, never shared code
  reaching *in*. `AqtCoreFW.Infrastructure` referencing `Notifications.Application` directly
  would have compiled and passed all 180 arch tests. Rejected anyway — `grep -rln
  Notifications.Application.csproj --include=*.csproj src/` returns 0 shared-project hits, and
  `Infrastructure.csproj:41-42`'s existing "no cycle" comment on `OrgUnit.Contracts` is the
  repo's actual precedent: shared needing something a module owns gets a zero-dep `*.Contracts`
  project, not a direct reference.
- **First guard, incomplete:** `appsettings.json` got an `Email` section; `appsettings.Production.json`
  and `vault-init.sh` did not. Fixed: `Email: { Host: "", Password: "" }` placeholder in
  Production (matching the existing `Encryption:FieldKey`/`Audit:HmacKey` empty-placeholder
  convention) plus a real runtime guard in `SendEmailAsync` — outside Development, empty
  `Email:Host` logs Critical naming the Vault key and returns, doesn't throw (a misconfiguration
  isn't transient; a rethrow would just duplicate the log every job cycle).
- Deliberately did **not** add `Email:Password` to `ProductionSecretValidator.RequiredKeys` — that
  list hard-fails boot for every prod deploy, including ones with no EMAIL alert rule configured.
  Would have traded a silent-fail for a false-red boot crash — same failure shape the RabbitMQ
  slice caught earlier the same day (`journal-20260717-1705-...md`).

## What We Tried

- Route through `INotificationModuleClient` — killed, see above (contract mismatch: it needs
  `RecipientUserId`+`TenantId` as Guids; `AlertRule` has only an address string and no tenant,
  alerts are system-wide).
- Direct `Infrastructure` → `Notifications.Application` reference — compiles, arch-tests green,
  killed anyway (grep evidence above; not the repo's pattern).
- **Chosen:** new `IAlertEmailSender` in `Notification.Contracts` (zero ProjectReferences already,
  confirmed by grep), implemented as `AlertEmailSenderAdapter` in `Notifications.Infrastructure`
  over the existing `IEmailSender`.
- Mutation-tested the guard by hand: inverted the `NotifyTarget` check → 4 tests red; commented
  out the DI registration → registration test red; `false &&` into the `Email:Host` guard → 3
  red; guard changed to unconditional `!IsDevelopment()` → the Staging test red. Reviewer killed 4
  more independently (LOG/default/WEBHOOK forced to send email → matching Theory case fails).

## Root Cause Analysis

Two separate root causes, same shape: **trusting a signature or a row estimate instead of
reading what it actually does.**

1. The backlog row's "effort S" was written by someone (me, an earlier session) who saw LOG and
   WEBHOOK working and assumed EMAIL just needed the same treatment through the same seam. Nobody
   had actually traced `SendNotificationCommandHandler` down to the `MailboxAddress.Parse` call
   before committing to the estimate.
2. My own first-pass config guard fell into the identical trap I was supposedly fixing: I added
   the appsettings.json entry and treated the task as done without tracing the full path a real
   prod deploy takes (base config → environment overlay → Vault seed script). Same "the shape is
   there so it must work" shortcut, one layer removed.

## Lessons Learned

- **A green arch-test suite does not mean a shortcut is allowed.** `ModuleIsolationTests` covers
  outbound module boundaries only; shared→module is unpoliced by tooling. When the test can't
  catch it, read the repo's actual precedent (the `Infrastructure.csproj:41-42` comment) instead
  of the gate.
- **"Effort S" on a backlog row is a claim, not a fact** — verify the seam it assumes actually
  works before trusting the estimate. This row's seam had never delivered a single email, for
  anyone, ever.
- **A guard that logs Critical is not a fix if nothing reads the config path it depends on.**
  Config changes need to be traced through every environment file AND the secret-seeding script,
  not just the one file that's easiest to edit.
- Honest caveat on my own work: one reviewer mutant survived — swapping the guard's `return` for
  `throw` still leaves all 13 tests green (the per-rule catch swallows either). The return-vs-throw
  choice is reasoned, not tested. Don't mistake the doc-comment rationale for a pinned invariant.
- A duplicate test slipped through my own first pass: `OutsideDevelopment_ConfiguredEmailHost_Sends`
  had the identical setup and assertion as `EmailChannel_WithTarget_SendsToThatAddress` — it pinned
  the guard's pass-branch by name only, not by behavior. Reviewer's N-3 caught it; replaced with
  `Staging_ConfiguredEmailHost_Sends`, which actually pins the default-deny-on-non-Development
  behavior (mutation-proven: guard → unconditional `!IsDevelopment()` breaks it, the duplicate
  wouldn't have).

## Next Steps

- **`NOTIFICATIONS-RECIPIENT-IS-GUID-NOT-ADDRESS`** (T2, filed) — fix the real bug:
  `SendNotificationCommandHandler.cs:67` and `:69` both pass a Guid where an address/phone is
  expected. Needs a UserId→address resolution step. Owner: next session, not scoped into this
  slice.
- **`ALERTING-TESTRULE-REPORTS-202-ON-FAILED-DELIVERY`** (T4, filed) — `AlertingAdminController`'s
  test-rule endpoint returns 202 even when the per-rule catch swallowed a real dispatch failure.
- **`ALERTING-LOGCRITICAL-HAS-NO-ALERT`** (T2, filed, ops) — both new failure signals in this
  slice are log-only; nothing pages on the alerting system's own Critical log. Also covers the
  residual case where `Email:Host` is set but wrong (passes the `IsNullOrWhiteSpace` guard, then
  fails as an indistinguishable `SocketException`). Needs an ops-side alert rule on this log line,
  not more application code.
- Coverage-gate half of the original backlog row carried to `P1-STABLE-CI-GATES-ENFORCE` — GitHub
  Actions is off by user choice, so flipping enforcement now would ship a gate nothing runs.
- `docs/ops/vault-key-rotation-runbook.md` was missing SMTP entirely (only had `email-pepper`, an
  unrelated login-hash salt) — added `Email__Host`/`Email__Password` rows plus the
  optional-by-design rationale, same commit.

**Verification:** build `src/AqtCoreFW.slnx` 0W/0E · arch tests 180/180 ·
`Infrastructure.Extensions.Tests` 196 passed/1 skipped (pre-existing) · `Notifications.Infrastructure`
16/16 · code-review: pass 1 REQUEST_CHANGES (H-1) → fixed → pass 2 APPROVE_WITH_NITS.

## Unresolved Questions

1. Should `AlertRule.NotifyTarget` be validated as an email address at creation time? Deferred —
   `AlertingAdminController` accepts any string today; no evidence it bites yet.
2. Will ops actually seed `Email__Host` before enabling an EMAIL rule? Runbook now documents both
   keys, but the Critical log is only actionable if someone is watching for it — see
   `ALERTING-LOGCRITICAL-HAS-NO-ALERT`.
3. `WebAppFixture` runs as `"Testing"`, so the new guard is live there with `Email:Host` unset.
   Harmless today (no EMAIL rule is seeded enabled in tests), but would silently skip a send if
   one ever is.
