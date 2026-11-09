# Journal — CrossSchemaSqlScanner write-side coverage (MSVC-WAVE0-P3)

**Date:** 2026-07-10 11:21 ICT · **Row:** MSVC-WAVE0-P3-CROSS-SCHEMA-REQUEST-PATH · **Mode:** `/cook --interactive` · **Plan:** `plans/260710-1111-crossschema-scanner-writeside/`

## What & why

Closed the write-side blind spot in `CrossSchemaSqlScanner` — the architecture-test helper that guards the module→schema boundary for microservices extraction. It flagged read-side cross-schema SQL (`FROM`/`JOIN <schema>.`) but was blind to write-side (`UPDATE`/`INSERT [INTO]`/`MERGE [INTO] <schema>.`). A cross-schema write couples module ownership harder than a read, so the write side was the more dangerous half to leave open.

## Scout verdict (INVARIANT 260522-A) — the row was mostly a re-affirm

The row's headline ask ("route genuine request-path coupling") was a no-op:
- The 3 real request-path couplings were refactored to ModuleClient interfaces at v2.132p10 (per the arch-test doc-comment).
- Whitelist = 6 install-time seeders + 2 read-only D7 dashboard queries — all legitimate, each with a Reason; preserved untouched.
- `NoModule_Should_Reference_AnotherModuleSchema_InRawSql` was already green (Architecture 161/161).
- Empirical write-side probe: all 20 raw-SQL sites owner-local, 0 cross-schema writes, 0 interpolated-schema SQL.

So the extension catches **0 current violations** — pure regression-prevention for the locked extraction goal (`MSVC-5SVC-TARGET`). User chose "extend scanner + close" over pure YAGNI-close.

## Change (test-project only, no `src/`)

- `CrossSchemaSqlScanner.cs`: broadened the shared keyword alternation of the two regexes — `FROM/JOIN` → `+UPDATE/INSERT [INTO]/MERGE [INTO]`, with `INTO` optional (T-SQL-valid) and a leading `\b` word-boundary. `DELETE FROM` was already covered via the `FROM` branch. The pre-existing `KnownSchemas.Contains` filter (15 real schema names) keeps the broadening false-positive-safe.
- `CrossSchemaSqlScannerTests.cs` (new): 7 non-vacuous self-tests driving the real `Scan()` over a throwaway temp module tree — one positive per write verb (UPDATE / INSERT INTO / bare INSERT / MERGE), a FROM read-regression guard, owned-schema negative, whitelisted-file negative.

## Review + verify

Code-review APPROVE_WITH_NITS. Applied both Low findings: (1) `INSERT` without `INTO` was a real T-SQL detection hole → made INTO optional + added a bare-INSERT self-test; (2) added `\b` to close a substring match (`subMERGE into …`). Two nits left as pre-existing (inline trailing comments still scanned; scanner dedup doc slightly overstated) — not introduced here.

Architecture.Tests **168/168** (was 161; +7), build 0W/0E under `-warnaserror`. The real-tree scan stayed green → the broadening introduced 0 false positives on the actual codebase.

## Lesson

Third microservices-prep row this session where the backlog claim decayed: two (P2, AUDIT-PII) were fully stale; P3 was a genuine "honest re-affirm" the row itself predicted. The empirical audit-before-open discipline turned a mis-scoped "route ~20 couplings" ask into a correct 40-line defensive guard completion. When a guard catches one side of a symmetric risk (read vs write), the missing side is worth completing even at 0 current hits — the guard's whole job is preventing the regression that hasn't happened yet.

## Unresolved

- None blocking. Interpolated-schema `FromSqlRaw` detection remains an accepted, documented scanner limitation (0 current cases).
