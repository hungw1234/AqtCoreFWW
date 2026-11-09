# Journal — DigitalSignature unavailable UX: split message intent & fix agent probe

**Date:** 2026-06-16 13:00 ICT · **Slice:** `POLISH-C-DIGSIG-UNAVAILABLE-UX` (backlog Theme C) · **Mode:** `/cook` · **Type:** refactor (FE-only, shipped forms)

## What

Two UX dead-ends on the already-shipped DigitalSignature signing flow were actionable via message refinement and error-discrimination at the real layer.

**C1 — provider-absent message:** The single `!providerKey` info Alert conflated two unrelated situations. Split into:
- **Authenticated tenant with no admin-configured provider** (`signingProvider != null` context available): warn "liên hệ quản trị viên", they can't resolve this themselves — org-config gap.
- **Public/anonymous form or builder preview** (no `signingProvider` passed at call sites): info "không hỗ trợ ký số" — the feature is off-limits here by design, not misconfiguration.

**C2 — agent-probe error discrimination:** `isVgcaAgentAvailable()` swallowed every failure (timeout, exception, non-zero rc, malformed response) and returned bare `false`. The downstream `catch` in `signing-prompt.tsx` became DEAD CODE — all three failure modes collapsed to "install VGCASignService" guide, indistinguishable from true not-installed.

Root-cause: the discrimination had to move into the probe itself. Changed the adapter contract `isAgentAvailable: boolean` → `probeAgentStatus: 'available' | 'not-installed' | 'unreachable'`:
- `not-installed` = plugin global absent (install guide, unchanged).
- `unreachable` = plugin present but timeout/exception/non-zero/malformed (new warning + "Thử lại" retry via `retryNonce` re-probe).
- `available` = all OK; `canSign` excludes `unreachable`.

The warning says the problem is transient and recoverable by user, which is true for connectivity issues — meaningfully different UX from "your system lacks this plugin entirely".

## Audit-at-slice-start reframed the file pointer (INVARIANT 260522-A)

The backlog row pointed at `signing-prompt.tsx:119` catch handler, but a 5-min empirical scan found the real file had drifted: the Alert blocking submit was now at `digital-signature-widget.tsx:142`, inside a `renderInteractiveField` map. The audit corrected the pointer before any code write, saving a fix-in-wrong-file detour.

## Why the probe layer matters

Swallowing an exception to return a boolean erases the reason the operation failed. Downstream code cannot distinguish "plugin not found" from "plugin found but network timeout" when both return `false`. The consequence here was a useless guide: "install this" when the plugin might be installed but unreachable. Always preserve failure semantics at the layer where the failure occurs.

## Interface contract ripple

Changing `isAgentAvailable()` → `probeAgentStatus()` required:
- Grep all consumers (2 call sites in `signing-prompt.tsx`).
- Grep all implementations (`VGCASignatureAdapter`, `UsbTokenSignatureAdapter`, test doubles).
- Update 2 unrelated probe test files (`probeSignatureAgent.test.ts`, `signing-prompt.test.tsx`) from boolean assertions to 3-state enums. **Name-filter tests risk missing siblings** — a tester filtering on "available" would have skipped these. Both files had to be audited manually because the probe invocation is not a direct function call but a Promise resolved inside `useSignatureConfig`.

## Review surfaced a pre-existing gate failure

`npm run typecheck:test` (tsconfig.test.json) is RED in unrelated `form-attachment-preview.test.tsx` (mock typing drift). The app-level `tsc` was 0E so it stayed hidden behind the wrong validation gate. This is a genuine footgun: test-specific config errors don't block CI until someone runs the full-suite; meanwhile, developers assume type coverage. Filed as `WEB-TYPECHECK-TEST-FORM-ATTACHMENT-PREVIEW` for a follow-up hardening pass.

## Verification

- `npm run typecheck` (app) 0E.
- `npm run lint` 0E.
- `vitest --run` 1058/1058 (+3 new probe tests for the 3-state enum).
- i18n: added `providerConfigGapWarning`, `agentUnreachableWarning`, `retryProbeLabel` to vi.json + en.json; removed orphan `unavailableDesc`.
- Code-review: APPROVE_WITH_NITS; 0 Critical/High/Medium.

## Lesson

**Trace every layer before patching the symptom.** The row pointed at signing-prompt's error handler, but the real error was two layers down in the probe's silent swallow. A catch block that never fires is invisible; the discrimination had to move to where the error actually happens. And **trace the ripple of interface changes before declaring done** — renaming a public contract method hits consumers, impls, and tests; if the test invocation is indirect (async hook → promise resolution), name-based test filtering can miss it.

## Next steps

- File `WEB-TYPECHECK-TEST-FORM-ATTACHMENT-PREVIEW` for separate hardening (tsconfig.test alignment).
- C3 `POLISH-D` and beyond deferred; POLISH-C DONE and ready for merge.
