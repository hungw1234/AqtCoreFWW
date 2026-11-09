# ClamAV Follow-ups: Flag Warm @ First Boot + Attachment Preview Scan Status Gate

**Date:** 2026-06-14 10:05  
**Type:** fix + arch + FE reuse  
**Trigger:** `/ck:cook CLAMAV-FOLLOWUPS` (follow `sunday-260614-clamav-managed-stack-dev-scan-activation`)  
**Commits:** `c15c40c9` (1 ahead origin, NOT pushed)

## What

Two ship-ready fixes + two intentional deferrals spanning flag cold-boot race, attachment preview staleation, and signal infrastructure. Closed the hardest foot-gun from the ClamAV activation (flag-not-warmed window) and killed a UX regression that exposed uncompleted uploads as available.

**Shipped (2):**

1. **CLAMAV-FIRSTBOOT-FLAG-RACE (BE) RESOLVED** — `SystemParamSeeder.cs` now injects `IFeatureFlagService`; immediately after `SaveChangesAsync` writes `feature:files.virus-scan`, if any newly-added key starts with `feature:` prefix, reload the flag cache via `await featureFlags.ReloadAllAsync(ct)`. Closes the window: the singleton L0 cache's initial load (`FeatureFlagRefreshService` hosted-service startup) ran before seeder wrote the row, so the flag read false until the 5-min periodic fired. A file uploaded in that gap hit `ClamAvScanJob:51` bypass branch (MarkAvailable WITHOUT scan). Design choice: explicit reload over reordering DI (intent-revealing; not fragile to registration order). Guard scoped to `toAdd` feature-prefixed keys (warm DB or non-feature seed doesn't reload needlessly). Idempotent (periodic refresh re-reads same rows).

2. **FORMS-ATTACHMENT-PREVIEW-SCANSTATUS (FE) RESOLVED** — `form-attachment-preview.tsx` `AttachmentPreview` now uses the shared `useFileScanStatus` hook instead of its own metadata query; gates render on `FileStatus`: Quarantined → "Đang quét virus…" tag (no download), Rejected → rejected tag (no download), Available → unchanged. Image-blob fetch gated on Available. Kills the broken download link a still-scanning file showed in submission + BP-run detail views (BE `DownloadFileQueryHandler:41` 404s non-Available anyway). DRY bonus: the hook's terminal-status polling-stop + auto-refresh on Quarantined→Available transition now fixed stale-link glitch the original 5-min staleTime couldn't catch. Both consumers (preview + `bp-run-submissions-card:72`) now consistent.

**Deferred (2):**

3. **BP-SCANSTATUS-SIGNALR** — evaluated, re-affirmed YAGNI: 2s polling at 30-cap sufficient for form-filler UX; SignalR file-scan wiring non-trivial, no scale pressure. File `T5` for post-launch. (No code, just decision trail in task notes.)

4. **CLAMAV-PROD-FLAG-POLICY** — untouched on purpose. `SystemParamSeeder.cs:48-51` has `IsDevelopment()` dev-gate on the seeded flag **intentionally**. Audit per `review-audit-self-decision §1` (decision-verified) + §3 (guard user decision): PROD flag policy is a compliance/ops posture decision (NĐ68), not a code default — auto-flipping would reverse a prior business choice. Kept as-is; owner: ops/product.

## Key Lessons

**INVARIANT 260522-A audit yield:** slice-start backlog audit separated a real code task from a business decision. Empirical scout of `SystemParamSeeder.cs` revealed the `IsDevelopment()` gate was INTENTIONAL per original comment. Instead of silently flipping `feature:files.virus-scan` to true in prod, flagged the posture decision for user — prevented wrong auto-reverse of compliance stance.

**Cold-boot gate cache:** a security control keyed off a lazily-loaded flag MUST warm the flag when the control's data is written, else window opens. Explicit reload at seeder-output, not relying on periodic refresh, closes the gap between write and first-read.

**DRY reuse pays UX dividend:** reusing `useFileScanStatus` instead of bespoke `metadata` query gave us auto-refresh (Quarantined→Available bounces) the narrower endpoint lacked. Single source of truth (hook) > duplicated narrower one.

**Guard decl-mismatch early:** `SystemParamSeeder` had `_featureFlagService` never wired due to missing DI registration; tsc clean didn't catch (no reference, TS-nothing). Wired correctly; verified at runtime `/aqt-runapp` Step 1.5 reseed.

## Verify

**BE:** SystemParams.Infra 0W/0E · SystemParamSeeder.Tests 41/41 (+3 new: cold-boot-flag-warm, non-feature-no-reload, idempotent) · Files.Infra 35/35 (no regress) · Files.Application 114/114 · runtime: dev startup traces feature-reload POST-seeder.

**FE:** forms module tsc 0E · eslint 0E (i18n tags via `t()` applied per M1 reviewer note) · vitest 150/150 (+4 new: Quarantined-tag, Rejected-tag, Available-no-tag, polling-stops-on-Available) · storybook `form-attachment-preview.stories.tsx` verified Quarantined=tag, Available=no-tag.

**Code review:** APPROVE_WITH_NITS (M1 i18n-tags applied; L1 no functional). 0 Critical/High.

## Files Changed

- `SystemParamSeeder.cs` — inject `IFeatureFlagService`; post-save reload `feature:` keys
- `form-attachment-preview.tsx` — swap metadata query for `useFileScanStatus` hook; gate image fetch on Available
- `SystemParamSeederTests.cs` (new) — cold-boot reload, non-feature idempotent, idempotent re-run
- `form-attachment-preview.test.tsx` (new) — Quarantined/Rejected/Available render, polling cease
- `backlogs/backlog.md` — row `CLAMAV-FIRSTBOOT-FLAG-RACE` closed; `BP-SCANSTATUS-SIGNALR` defer-filed T5
- `docs/project-changelog.md` — entry v2.XYZ-finalized; audit-guard pattern documented

**Linked:** `journal-260614-0945-audit-hmac-chain-dedicated-hangfire-queue.md` (sibling Hangfire queue fix) · `journal-260614-0845-clamav-managed-stack-dev-scan-activation.md` (parent ClamAV stack activation).

## Emotional Note

The flag-warm fix felt like dodging a bullet in slow-motion — empirical trace during planning caught a transient gap that wouldn't surface in test (seeder always runs fresh, no race). The real win: stopping myself from *auto-flipping a compliance flag* just because audit said "verify this design" — trust the user to separate code-fixes from business-decisions, ask rather than assume.

Attachment preview was the simple one, pure UX glitch from incomplete stanza wiring. Nice to delete 8 lines of metadata-query and gain auto-refresh as a side effect of DRY.
