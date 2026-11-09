# Journal — ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR (identity drops its [audit] credential)

**Date:** 2026-07-14 (late afternoon) · **Branch:** main · **Row:** ISO-AUDIT-MODULECLIENT-SECURITYHEALTH-REFACTOR
**Follows:** ISO-DAPPER-KEYED-DI-INCREMENT (this was its Decision-3 follow-up) · journal-20260714-1507

## What shipped (cross-module credential-drop refactor, 7 files)
The 2 identity SecurityHealth handlers (`GetAuditIntegrityStatusQuery`, `GetAuditRetentionStatusQuery`) read `[audit]`
DIRECTLY via a keyed `[FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection` — a keyed EXCEPTION that left
identity holding an `[audit]`-capable DB credential (ADR-012 standing exception #5). Refactored them to delegate to the
audit module's cross-module client:
- **`IAuditEvidenceClient`** (Audit.Contracts) +2 methods: `GetIntegrityStatusAsync`, `GetRetentionPolicyStatusAsync`
  (+ 3 return records). Interface doc broadened from "compliance evidence" to "cross-module audit read client — lets
  other modules read audit rollups without holding their own `[audit]` credential".
- **`InProcessAuditEvidenceClient`** (audit Infra, already audit-keyed) implements them — the `[audit]` SQL moved
  **verbatim** (sys.triggers check + `COUNT_BIG` + retention-policy query, audit-admin-bypass preserved).
- **2 identity handlers** → inject `IAuditEvidenceClient`, map to the unchanged FE DTOs, drop the keyed reader.
- **`Identity.Application.csproj`** +ProjectReference → `Audit.Contracts` (contracts-only, mirrors Reporting).
- **`AuditKeyedReaderAllowlistTests`** 12 → **10** (the 2 identity paths removed; method `...TenFiles...`, count `Be(10)`).
- **ADR-012** removed standing-exception #5; recorded the tightening (identity now holds NO `[audit]` credential).

## Net effect
Identity no longer holds an `[audit]`-capable DB credential — a real isolation improvement (one standing exception
gone), cleaner cross-module boundary (identity depends on Audit's CONTRACT, not its DB). Continues the ISO read-isolation
thread + advances extraction-readiness (fewer cross-module direct DB reads).

## Verify
Behavior-preserving (SQL moved verbatim, admin-bypass intact, FE DTOs untouched, `ct` now propagated = strict
improvement). Full solution build **0W/0E**; `AuditKeyedReaderAllowlistTests`+wiring guard **6/6** (sweep finds exactly
10 → identity dropped the key); Identity.Application **426/426**; `Qd742Phase05ComplianceTests` structurally intact.
Code-review **DONE_WITH_CONCERNS** — all 6 checks PASS, 0 Critical/High/Medium; 2 LOW (stale allowlist-test comments
12→10 fixed same commit; changelog/roadmap row = this finalize). No unit test required (trivial type-safe delegation +
verbatim SQL move; no prior test existed).

## Ground-truth correction (INVARIANT 260522-A)
Backlog row named `IAuditModuleClient` — that interface does NOT exist; the real cross-module audit client is
`IAuditEvidenceClient` (Audit.Contracts, used by Reporting). Extended it rather than inventing a second interface (DRY).

## Follow-up (optional, next slice)
Code-review's highest-value next test: a non-`db_owner` integration test on the 2 new `InProcessAuditEvidenceClient`
methods asserting the `audit_admin_bypass` NULL-tenant count (the one silently-loseable behavior in future edits — now
cheaper to guard here than it ever was in identity). Not required for this landing.

## Lessons reinforced
- [[feedback-verify-subagent-reports]] — re-ran allowlist + identity suites + read the SQL move by hand before trusting.
- [[feedback-dapper-no-positional-records]] — re-homed the Dapper `RetentionPolicyRow` as a class in the client.
- INVARIANT 260522-A — the row's named artifact (`IAuditModuleClient`) didn't exist; scouted the real client first.
