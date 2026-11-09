# Journal — Isolation Seam Red-Teamed, Re-scoped to EF-First Read-Isolation Plan

**Date:** 2026-07-13 (evening) · **Branch:** main · **Ships:** 0 code (1 docs commit `877045f2` housekeeping + push) · **Deliverable:** validated plan `plans/260713-1720-logical-isolation-connection-resolver-seam` (gitignored)

## Session goal
`/aqt-next` orient → microservices #1 track was human-gated on FU-A (auditor). Brainstorm "what to do while waiting" → converged on **logical isolation** (per-service schema logins on shared DB) → plan → gate. No code intended; produce a validated plan.

## What worked
- **Two adversarial gates caught two large mistakes before any code:**
  1. **Red-team killed the plan premise** (4 Critical, source-verified). The brainstorm reframe — "read-isolation is a clean read-side property on `DapperReadDbConnection`; a behavior-preserving seam is a no-regret foundation" — was disproven: (a) UoW-active reads short-circuit to the shared write connection (`DapperReadDbConnection.cs:94-97`); (b) EF LINQ reads bypass the seam (`AsNoTracking` 195× / 63 files, over audit/signing/PII); (c) the audit HMAC read path opens raw ADO on `GetConnectionString("Default")` (`HmacChainService.cs:369,585`), never via the seam. Plus a Critical DI defect: AuthServer skips `AddInfrastructure` (`Program.cs:229`) → the ctor change would 500 audited login at runtime, hidden by `ValidateOnBuild=false`.
  2. **Threat-model reframe blocked a catastrophic over-correction.** User initially chose "break the UoW wall" (distributed-tx/saga rewrite) to isolate UoW-active reads. Reframe: a read inside a coordinated cross-module UoW is part of an operation that already has authority over both schemas — NOT "service A snooping service B"; it dissolves into a cross-service saga at physical extraction. Breaking write-atomicity to isolate an op from itself = huge cost, ~0 compliance benefit. User accepted → pragmatic subset.
- Self-verified the heaviest red-team finding (F2) against my own earlier read of `DapperReadDbConnection.cs` before capitulating — did not blindly defer to the audit.

## What didn't / lessons
- **My brainstorm reframe was wrong** on the load-bearing technical claim (missed the UoW short-circuit + EF + Hmac read paths). Scout confirmed the two named seam classes but did not trace ALL runtime paths of the invariant. **Lesson (saved to memory `feedback-isolation-seam-must-cover-all-invariant-paths`):** before claiming a seam achieves an invariant, enumerate + trace EVERY runtime path (grep `new SqlConnection(`, `GetConnectionString(`, `AsNoTracking(`, UoW short-circuit branches), not just the named class.
- Red-team-the-plan-in-embryo (SSOT + scout) BEFORE authoring phases would have been even cheaper — reinforces `feedback-red-team-before-implement`.

## Decisions made
- **Direction: logical isolation** on the shared DB (NOT physical extraction); FU-A auditor gate downgraded to non-blocking (C3 already proved isolation technically achievable).
- **Killed:** the behavior-preserving "connection resolver seam" (isolated nothing).
- **Re-scoped to** a vertical read-isolation slice for the audit boundary, **EF-first** (validate): isolate EF (per-context login) + `HmacChainService` audit reads; prove with a non-`db_owner` test (DENY `[audit]`, SqlEx 229).
- **Deferred (follow-on increment):** standalone-Dapper read isolation via **keyed DI per module** (`[FromKeyedServices]`) — the shared module-agnostic reader is the real hard part; spiked separately so it doesn't block EF isolation.
- **Login provisioning:** reuse C3 spike pattern (script/migration + GRANT/DENY + Vault secret).
- **Documented exceptions (ADR-012):** UoW-active reads + admin/db_owner audit reader stay on the broad login.
- **Honesty caveat:** the EF-first slice delivers a proven, repeatable EF-path isolation — NOT a complete boundary until the Dapper increment lands.

## Test status
n/a — no code, no tests run this session. Plan is TDD-structured (Phase 3 = non-`db_owner` isolation test as the oracle).

## Next session
- `/clear` → `/ak:cook C:\AqtCoreFW\plans\260713-1720-logical-isolation-connection-resolver-seam\plan.md` (EF-first slice).
- Phase-1 census (full raw-ADO enumeration) is the first cook step.
- Standalone-Dapper keyed-DI increment tracked as a follow-on.
- Memory index near read-limit (~19.6KB / 24.4KB) → dedicated compaction pass due.
