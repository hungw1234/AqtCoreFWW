# Journal — AUDIT-CSV-FORMULA-INJECTION (CSV formula-injection guard on audit-log CSV export)

**Date:** 2026-05-12 ~23:25 ICT · **Branch/PR:** `fix/audit-csv-formula-injection` → PR #104 squash → `main` `f8437b15` · **Pri:** P3

## Session goal
Pick the next backlog item via `/cook "what to do next?"` and ship it. User chose **AUDIT-CSV-FORMULA-INJECTION** (a P3 follow-up spawned by `AUDIT-EXPORT-TENANT-FORCE`): `ExportAuditLogsCsvQueryHandler.EscapeCell` had no OWASP CSV-injection guard.

## What worked (verified)
- `EscapeCell` now: (1) if first char ∈ `{= + - @ TAB CR}` → prefix `'`; (2) then existing RFC-4180 comma/quote/newline double-quote wrapping on the (possibly-prefixed) value. Ordering matters — apostrophe must land *inside* the quotes. New `IsFormulaTrigger(char)` helper. Single chokepoint, header row is a const so bypasses.
- Tests `AqtCoreFW.Audit.Application.Tests` 45 → **50/50 PASS**, build 0E. New: `[Theory]` ×4 trigger payloads, prefix-then-wrap interaction (`=1,2"3` → `"'=1,2""3"`), leading-TAB, benign-not-prefixed; `StubRows` helper stubs `IReadDbConnection.QueryAsync<ExportRow>` (4-arg overload — note `ExportRow` is a nested type → reference as `ExportAuditLogsCsvQueryHandler.ExportRow`).
- Code-review APPROVE — 0 Critical/High/Medium; 2 optional Low test gaps (L1 prefix+RFC4180 interaction, L2 TAB/CR coverage) folded in pre-merge.

## What didn't / lessons
- Minor self-inflicted churn: first wrote test with `userId:`/`IpAddress:`/`CorrelationId:` named args, but positional record params keep PascalCase names (`UserId`, …) → CS1739; switched to positional.
- `ExportRow` is `internal` *and* nested inside the handler → in the test project (which has `InternalsVisibleTo`) it must be qualified `ExportAuditLogsCsvQueryHandler.ExportRow`, not bare `ExportRow`.
- Architecture suite not re-run — change is internal to one query handler, no controller/DTO/entity surface; zero arch impact. (CI will confirm 94/94.)

## Decisions
- **Neutralise, not reject** — keep the data, prefix `'` (audit export must be lossless). Standard OWASP trade-off accepted; documented inline that plain `-5`/`+84…` get prefixed too (no exported column is numeric → nil impact).
- **`LF` deliberately not a trigger** — a leading LF is already neutralised by the RFC-4180 quote-wrap; consistent with OWASP guidance.

## Test status
`AqtCoreFW.Audit.Application.Tests` 50/50 PASS. Build 0E. Arch suite untouched (no relevant surface).

## Next session
Backlog still ~99%+; remaining is P2/P3 follow-ups. Candidates: `PHASE6-RLS-SECURITYINCIDENTS-TENANT` (P3, ~2h — verify `SecurityIncident.TenantId` nullability in prod, possibly needs asymmetric audit-bypass RLS), `PHASE1-SEED-m1` (P2, ~3-4h — dedup `WebAppFixture`/`SeederSmokeFixture`), `TESTCONTAINER-COLD-START-ROOT-CAUSE` (P2 — shared integration-suite blocker), `RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS` (P3 — implement the `RUN_MIGRATIONS_ONLY` gate).
