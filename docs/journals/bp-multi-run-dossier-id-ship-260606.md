# BP Multi-Run via Per-Run DossierId — SHIPPED (2026-06-06)

**Version:** v2.137p157 · **Commits:** `2f5f80bd` feat + `d8ac8f74` incidental test-fix + `a2517f29` docs — all PUSHED origin + bitbucket, ahead=0.
**Cook:** `/cook plans/20260605-1339-bp-multi-run-dossier-id/plan.md --auto` (7-phase plan, ~3.0d est).

## What shipped
Fixes 500 on the 2nd `POST /business-processes/{id}/runs` for the same BP. Nullable per-run `WorkflowInstance.DossierId` (Guid v7, minted unconditionally in `StartBusinessProcessRunCommandHandler` — no `"BusinessProcess"` literal → drift-proof). Index filter narrowed to **Option B'** `[CompletedAt] IS NULL AND [DossierId] IS NULL`: non-BP rows leave DossierId NULL → still indexed (one-active-per-entity byte-identical, OUTBOX-1 untouched); BP runs non-null → excluded → N concurrent runs. entityId stays = bpId (FE display-name contract). Migration `20260606083540` (Down = **3b fail-loud**, user-chosen). Layer-1 `SaveChangesAsync` 2601/2627 → `GOV_WFL_020` 409 (scoped to index name). FE per-attempt `X-Request-Token` + 409 toast.

## Decisions
- **Down() = 3b fail-loud (user-confirmed via AskUserQuestion).** plan.md preferred 3a CTE-collapse (destroys extra runs) but memory carry-fwd + backlog said "fail-loud pending cook confirm" → surfaced as a gate, user chose fail-loud (THROWs on >1 active/key, no data loss).
- **DossierId = multi-run enabler only** (user-locked pre-cook) → no own index/uniqueness/FE-surfacing.
- **Commit split:** feature + incidental contract-test fix as separate commits (user-approved), main-direct, origin+bitbucket not oracle.

## Drift caught during cook (plan written 260605, code drifted)
- **`CreateInstanceByInternalIdAsync` test mocks pass `ct` positionally at arg 6** → inserting `Guid? dossierId` before `ct` broke 2 NSubstitute setups (type mismatch) + needed `Arg.Any<Guid?>()` matcher. Plan said "source-compatible" — true for prod callers, NOT for these mocks. Also `IGuidGenerator` had to be injected into the handler ctor → 2 manual test constructions (unit + TenantIsolation integ) needed the new arg. The grep for `new StartBusinessProcessRunCommandHandler(` MISSED the integration-test one (only found at build-fail) — build is the real gate, not grep.
- **`CreateInstanceByInternalIdAsync` is BP-EXCLUSIVE** (only prod caller = the BP handler) → confirms Approach B is naturally BP-scoped; Forms use `CreateInstanceAsync`/`ByDefinitionKey`.
- **Pre-existing red build:** `WorkflowApiContractTests` missing `Branches` arg since v2.137p155 (parallel-branch ship verified unit+integ but not the contracts project) → full-solution build was red on `main`. Fixed incidentally (separate commit) to unblock.

## Review concerns (both real, both my new code → fixed not gated)
- **HIGH-1:** FE 409 handler read `error?.response?.status` but the axios interceptor rejects with a **normalized flat** `{ status, code }` (NOT an axios error) → 409 branch was dead, duplicate toast never showed. Fixed to `(error as { status?: number })?.status`. **Lesson: verify the actual error shape the interceptor produces, don't assume axios `.response`.**
- **MEDIUM-1:** `IsDuplicateActiveEntity` matched 2601/2627 by NUMBER only → `SaveChangesAsync` is the shared flush path (transitions stage history/branch-status unique rows) → an unrelated collision would mislabel as GOV_WFL_020. Fixed by also requiring `sql.Message.Contains("UX_WorkflowInstances_Active_Entity")`. Re-ran integ tests → still translates the active-entity collision (SQL 2601 message DOES carry the index name).

## Verify-the-claim wins
- **Tester first descoped the non-BP GOV_WFL_020 test as HTTP-404** (wrong layer); pushed back → it's an APP/repository-level test (sequential SaveChanges → Result.Fail). Added it → phase-04 Layer-1 guard now has direct coverage (was zero). **A raw-INSERT test (AC2a) proves the index throws 2601 but NOT that the repo catches+translates it — different layer.**
- **AC6 descope challenged via advisor:** "is AntiDoubleSubmit actually registered, or does the header ship into the void?" → grep confirmed `Program.cs:674 app.UseAntiDoubleSubmit()` + middleware keys on `X-Request-Token` const. So production path is real; only the WebApplicationFactory **test harness** lacks the middleware/Redis wiring → automated BE test descoped (follow-up `BP-DOUBLE-SUBMIT-TOKEN-BE-TEST`), not the feature.

## Environment notes
- `dotnet ef` local-tool manifest restore reported success but `dotnet ef` / `dotnet tool run` couldn't resolve it (manifest quirk) → installed to `./.eftools` via `--tool-path` and called `./.eftools/dotnet-ef` directly. `.eftools/` is untracked — **must NOT be committed** (excluded via selective `git add`).
- `migrations add` does NOT touch the DB (model-diff only) → no live SQL needed to scaffold; Testcontainers (hermetic) is the AC gate, not the live dev row (the narrowed index can only REDUCE indexed rows → physically can't fail on existing data).
- Bash `cd web` polluted the Grep/Glob tool cwd → use ABSOLUTE paths after a `cd`.

## Results
Build 0E/0W · 10 real-SQL integration + 6 BP unit PASS · FE tsc+lint clean · review APPROVE_WITH_CONCERNS (2 fixed+re-verified). depmap + changelog + backlog synced.

## Carry-forward
- `BP-DOUBLE-SUBMIT-TOKEN-BE-TEST` (T4) — automated BE-integ test for the X-Request-Token guard, blocked on a harness that wires AntiDoubleSubmit + ICacheService.
- Optional 1-row cleanup: backfill the legacy 2026-06-03 active BP row's DossierId = PublicId so it too is index-exempt (not required — it sits alone in the index).
