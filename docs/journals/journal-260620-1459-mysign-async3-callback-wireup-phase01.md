# Journal — Mysign async=3 callback wire-up, Phase 01

**Date:** 2026-06-20 · **Commit:** `187b4a4f` (feat, on main, not pushed) · **Plan:** `plans/260620-1358-mysign-async3-callback-wireup/` (local)

## Trigger
`/aqt-next` → `/cook VIETTEL-PENDINGTX-USERID-FROM-CCCD`. Backlog row claimed "now active" (T3). Per INVARIANT 260522-A, audited ground-truth first.

## Audit verdict: row was real-blocked, not ready
Empirical probe (5 files) contradicted the label: async=3 (AsyncCallback) was unreachable by any live caller — `opts.DefaultMode=Sync`, zero callers set `mysignMode=AsyncCallback`, live PAdES path = async-poll (async=2). The userId bug at `ViettelMysignProvider.cs:594` was genuinely latent. The "fix from ExtraParameters" was also incomplete (no caller injected userId). Presented to user → **chose to expand scope** to full async=3 end-to-end wire-up (both generic raw-sig + PAdES), full result-retrieval, admin-controlled mode via `SignatureFlowMode`.

## Scout → Plan
- Explore agent mapped the W6 surface: **~50% built** (entity + migration + `/auth` + `/result` controllers + token service + cleanup job + 6 endpoint tests) but **0% wired** (no initiator selects mode, callback URL never sent to Mysign, userId bug, correlation not injected).
- Key architectural finding: async=3 stores a **raw signature** (no PAdES assembly) — the PAdES-via-callback path is a separate, larger lift. Two distinct artifact shapes.
- planner produced a 4-phase plan; resolved 3 open questions against code (generic retrieval reuses existing `/confirm` DB-first; PAdES extract a shared splice service; extend `SignatureFlowMode` enum).

## Phase 01 shipped (generic raw-sig path)
- `SignatureFlowMode.AsyncCallback=2` (Domain) → `MysignFlowModeMap` (single source of truth) → provider `MysignSignMode.AsyncCallback=3`.
- **Layering correction to the plan:** Application references Domain only (NOT Infrastructure), so the handler can't use `MysignFlowModeMap`. Seam moved: handler emits `ExtraParameters["signFlowMode"]` (Domain enum it owns); provider `ResolveMode` maps it. DRY + layering preserved.
- **userId fix:** handler injects `cmd.UserId` (internal Guid) into `ExtraParameters["userId"]`; provider reads it instead of parsing the CCCD-bearing `request.UserId`. Guid.Empty attribution bug gone.
- **Callback URL plumbing:** `ViettelMysignOptions.CallbackUrl` + nullable DTO field, sent only when async=3 (WhenWritingNull drops it for 0/2 → byte-identical sync/poll body), fail-loud if async=3 + empty URL.
- **Regression-safe:** handler injects `signFlowMode` ONLY for AsyncCallback; Async/Sync keep `opts.DefaultMode` — the generic `/sign` path's existing behaviour is untouched (no silent repurposing).

## Security fix (proactive, pre-review)
Caught it before the reviewer: the handler copied the public `/sign` body's `ExtraParameters` into the provider bag, and `ResolveMode` checks `mysignMode` first — so a client could force the mode or spoof `userId`/`tenantId`. Added a reserved-key strip (`mysignMode`/`signFlowMode`/`userId`/`tenantId`) before server-set values. The code-reviewer independently flagged the identical HIGH (it reviewed the pre-strip snapshot) — confirming the fix closed exactly that vector. Strip-guard test added.

## Verification
- Build 0E/0W. Unit: **180/180** Infra·Signing + **44/44** App·Signing (18 new tests: mode-map 4, client false-green+fail-loud guards 9, handler injection+regression+strip 5).
- **False-green guard** baked in: client-level test asserts the callback URL is present in the real outbound signHash body for async=3 and ABSENT for 0/2 (mirrors the client's `WhenWritingNull` JsonOpts, not default serialization).

## Lessons
- **Sealed clients ⊥ NSubstitute.** The tester's provider-userId-round-trip unit test mocked the 4 sealed `ViettelMysign*Client` classes → Castle DynamicProxy fails. Removed it; provider userId round-trip belongs to Phase 02 e2e (WireMock + real DB) which already covers it (step 3 asserts persisted `UserId == user Guid`). Right test layer, not forced unit.
- **Tester race / stale report.** Tester ran ~24 min; its final "653 pass incl round-trip" was stale (I'd already deleted the infeasible file). Always re-run authoritative counts after concurrent agents; don't trust a long-running agent's end-state claim over the live filesystem.
- **Respect `.gitignore`.** git-manager force-added the gitignored `plans/` dir per my instruction; reverted that commit — the repo deliberately keeps plans local. Code commit kept, plan stays on disk.

## Remaining (active plan)
P02 generic retrieval + e2e · P03 PAdES-via-callback assembly · P04 hardening (fail-closed cert binding at `/confirm`) + docs. **Go-live gate:** confirm Viettel callback JSON key (`callbackUrl` `[UNVERIFIED]`) against vendor SDK doc. Deploy prereq: callback URL must be publicly reachable from Viettel's network.

## Unresolved questions
- Exact Viettel callback-URL JSON key name (vendor doc).
- P04 decision locked: fail-closed (400) for unbindable cert on generic callback `/confirm`.
