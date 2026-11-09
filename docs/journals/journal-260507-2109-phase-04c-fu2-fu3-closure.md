# Phase-04C Closure — FU2 + FU3 Journal

**Date:** 2026-05-07 21:09 ICT
**Branch:** main (commits `6b8acfd6` + `12ddcfd1`, both pushed)
**Predecessor:** [`journal-260507-1700-phase-04c-arch-tier-rules.md`](journal-260507-1700-phase-04c-arch-tier-rules.md)
**Backlog:** PHASE4C-FU2 + PHASE4C-FU3 → DONE; PHASE4C-FU1 deferred (3rd-consumer trigger)
**Test delta:** Architecture suite **32/32 → 33/33 PASS** (+R_T2_PUBLICID_REQUIRED)

---

## What shipped

### FU2 (`6b8acfd6`) — Pin Phase-4b naming convention

- `docs/code-standards.md` — new "T2 Tier — Repository Naming Convention (Phase-4b, PR-C precedent)" subsection in Repository Pattern. Pins canonical accessor set (external/internal/projection) + banned shapes table + exemptions + reference to enforcing rules.
- `tests/.../TierRepoArchitectureRulesTests.cs` — fixed 5× docstring drift `GetByIdInternalAsync` → `GetByInternalIdAsync`. R_INTERNAL_CTRL regex (`\w+InternalAsync`) was already spelling-agnostic so no behavior change; pure documentation hygiene.

### FU3 (`12ddcfd1`) — Add positive-coverage rule

- New fact `Rule_T2_Repository_Must_Declare_External_Guid_Accessor` (`R_T2_PUBLICID_REQUIRED`).
- Accepts 2 patterns:
  - **A (preferred, 16 repos):** explicit `Task<T?> GetByPublicIdAsync(Guid publicId, ...)`.
  - **B (acceptable, 4 repos):** inherit `IRepository<T, Guid>` / `IRepository<T, GuidWrapperId>` (e.g. `FileId`).
- Same exemptions as R_T2_REPO_SIG: `AspNetIdentityGuidPkExceptions` (8 ASP.NET Identity Guid-PK satellites) + `T2NoRepoExceptions` (8 DbContext-direct/aggregate-owned entities).
- Coverage breakdown of 30 T2 entities: 16 Pattern A + 4 Pattern B + 8 AspNetIdentity exempt + 8 NoRepo exempt + 2 renamed-but-Pattern A (`FileRecord`→`IFileRepository.cs` is Pattern B; `AccessReviewRecord`/`UserDelegation`/`ConsentRecord`/`ApplicationRole` are Pattern A via rename map).

---

## Key learnings

### 1. Backlog suggestion ≠ source-of-truth

PHASE4C-FU2 backlog said "pin `GetByIdInternalAsync(long)` per PR-C precedent". Source verification (28+ callsites across 7 modules) showed canonical spelling is **`GetByInternalIdAsync`** — backlog had it backwards. **Memory lesson:** verify naming claims against `grep src/` before pinning, even when backlog references a specific commit. Cost of skipping verification: 5 docstring-drift occurrences would have propagated as the canonical reference.

### 2. Reality of the Pattern A/B split surfaced during FU3

FU3 was scoped as "every T2 repo MUST declare `GetByPublicIdAsync(Guid)`" — strict positive rule. Implementing it surfaced 4 T2 repos using inherited `IRepository<T, Guid|GuidWrapper>` instead of explicit declaration:

| Entity | Repo | TId | Inheritance |
|--------|------|-----|-------------|
| FileRecord | `IFileRepository` | `FileId` (Guid wrapper) | `: IRepository<FileRecord, FileId>` |
| FormSubmission | `IFormSubmissionRepository` | `Guid` | `: IRepository<FormSubmission, Guid>` |
| WorkflowDefinition | `IWorkflowDefinitionRepository` | `Guid` | `: IRepository<WorkflowDefinition, Guid>` |
| WorkflowInstance | `IWorkflowInstanceRepository` | `Guid` | `: IRepository<WorkflowInstance, Guid>` |

Two paths considered:

| Option | Effort | Outcome |
|--------|--------|---------|
| Strict rule (Pattern A only) → refactor 4 repos | ~2-3h | Real refactor, scope creep beyond ~30min FU3 budget |
| Acceptance rule (A or B) + document both | ~30min | Honest snapshot of codebase reality, future tightening optional |

Chose acceptance rule. **The test ships honest about the codebase state.** If team later prefers strict Pattern A, refactoring 4 repos + tightening rule regex is a separate ~2-3h ticket.

### 3. KISS over premature unification

PHASE4C-FU1 (DRY: shared `AspNetIdentityGuidPkExceptions` between `IdentifierTierShapeRulesTests.cs` + `TierRepoArchitectureRulesTests.cs`) deferred per "3rd-consumer trigger" rule. Two consumers ≠ DRY violation; three consumers = abstraction earning its keep. Holding the line.

---

## Operational impact

- **Zero runtime impact** — all changes are docs + test code. No DB/migration/API surface change.
- **Phase-08 finalize gate** unblocked further: Phase-04c (PHASE4C-ARCH + FU2 + FU3) fully closed. FU1 trigger-gated, not blocking.
- **Phase-4b prep** improved: naming convention now pinned + positive-coverage rule will catch future T2 repos shipping without external accessor.

---

## Unresolved

- Pattern A vs B intentional split: future cleanup ticket if team wants strict Pattern A everywhere. Cost ~2-3h (4 repos × declare method + adapter + test). No P1 trigger.
- FU1 (DRY exception list) waits for 3rd consumer.
