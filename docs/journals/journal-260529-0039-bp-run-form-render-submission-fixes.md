# Journal — 2026-05-29 00:39 — BP-Run form rendering + submission fixes (3 bugs)

## Session goal
Live debug session on the BP-Run runtime flow (`/aqt-runapp` stack). Three distinct
user-facing form defects surfaced and were fixed + committed separately:
1. POST `/runs` with empty intake data → 500 `GOV_SYS_000` instead of a validation error.
2. "Bắt đầu quy trình mới" intake form section rendered empty despite required fields.
3. (flagged, then fixed) transition confirm modal — same form-render class, different data path.

## What worked (verified + commit refs)
- **`480ef98b` 500→422:** transport-neutral `BpRunSubmissionOutcome` record replacing the thrown
  `InvalidOperationException` in `IFormSubmissionRunClient.CreateForBpRunAsync`. Kept the cross-module
  contract FluentResults-free (gRPC-ready). 3 handlers roll back the cross-module UoW + translate via
  `ToFailureResult` (CrossTenant→404, per-field→422). Verified live: fresh-token curl returned 422 with
  `full_name` + `request_details` field keys.
- **`dfc16ba4` intake/state render:** root cause = casting the published version-snapshot envelope
  (`key`/`validation`, no `isActive`) straight to `FormFieldDto` → `isActive` filter dropped all fields.
  NEW `parseSnapshotSchema` adapter maps snapshot → renderer-ready DTO; wizard + state-form tabs route
  through it. Verified live: 4 fields render + run started (instance `019e6f89-…`), submit not 422.
- **`261e66f5` transition contract:** BE `RequireForms` returned bare `long[]`; FE consumed objects with
  `.fields` → spread-of-undefined crash. Enriched outcome with `BpRequiredTransitionForm{ id, publicId,
  schemaJson }` (snapshot inline, mirrors intake/state pattern); FE parses at boundary via the SAME
  adapter. BP.Application.Tests 96/96, FE typecheck app+test clean, code-review 0 blocking.

## What didn't / lessons
- **Mock-search missed the root `tests/` tree** (earlier session): first blast-radius grep only hit
  `src/tests`; the repo also has a ROOT `tests/` with BP.Application unit + TenantIsolation tests mocking
  `CreateForBpRunAsync`. Lesson: scout the whole repo, not just `src/`.
- **Bash CWD ≠ repo root:** the Bash tool runs from `web/`; bare `git add src/...` fails pathspec. Prefix
  `cd /c/AqtCoreFW &&` for git. (Backslash line-continuation in the Bash tool also mangled a pathspec —
  use single-line `git add`.)
- **Entangled commits:** Bug-1's return-type change forces all 3 handlers + 2 test files to compile
  together, so the transition handler + its test carry BOTH Bug-1 and Bug-3 edits. To keep 3 bisect-clean
  commits, fully reverted Bug-3 from the working tree → committed Bug-1 (95/95) + Bug-2 → reapplied Bug-3
  → committed (96/96). Each commit verified to build/pass at its point.
- **Bug 3 is latent, NOT verified live:** only reachable when a designer binds a required form to a
  transition; no seed configures one and the dev stack was torn down. Proof is structural (contract halves
  now agree, tsc) + unit (handler test locks the inline-snapshot shape). Honest gap.

## Decisions made
- **Option 2a (transport-neutral outcome)** over throwing or FluentResults-in-contract — keeps the
  cross-module client transport-agnostic for a future gRPC/JSON split.
- **FE adapter at the response boundary** (allowed-actions-bar) rather than changing the modal — modal
  stays unchanged, reuses `parseSnapshotSchema` (same as intake/state). Wire shape kept as array (FE
  multi-tab already expects array) despite BE `>1` guard meaning count ≤1 — collapsing to a single object
  was flagged as optional polish, not done (no user decision to change it).
- **Snapshot `options` gap left as-is** — `FieldPayload` snapshot omits select/radio options; out of scope
  for these fixes (BP forms currently Text/Textarea only). Tracked as a backlog follow-up.

## Test status
- BE: `AqtCoreFW.BusinessProcess.Application.Tests` 96/96 (full assembly, unfiltered). BP.Presentation 0W/0E.
- FE: typecheck app + test clean; `form-snapshot-adapter` 3/3.
- No integration test asserts the old transition wire shape (checked) — no integration regression.

## Next session
- **PUSH** local main (5 commits ahead: BTC phase-01 `394e2efb` + p124 `1f3b0968`?… + p125 ×3) to origin
  (+ bitbucket sync?). User-gated.
- **BP-FORM-OPTIONS-SNAPSHOT** (Low) — version-snapshot serializer omits select/radio `options`; Select
  fields in BP intake/state/transition forms render without choices.
- **BP-TRANSITION-FORM-E2E** (Low) — Bug 3 verified structurally only; add an integration/e2e test that
  configures a required transition form and drives the `requiresForms` → render → submit path live.
