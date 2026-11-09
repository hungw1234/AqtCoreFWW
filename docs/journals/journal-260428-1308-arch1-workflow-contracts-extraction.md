# Session Journal — ARCH-1 Workflow.Contracts Extraction

**Date:** 2026-04-28 (afternoon, ~13:08 ICT)  
**Version shipped:** v2.132p11 (SQL Server main)  
**Epic:** `plans/260428-1254-arch1-extract-entity-status-contributor/`

## What Changed

**Context:** v2.132p10 shipped with architecture guard `NoModule_Should_DirectlyReference_AnotherModule` still RED due to pre-existing `Cases.Infrastructure → Workflow.Application` direct ProjectReference. Root cause: Cases module's `CaseStatusEnumContributor` consumed `IEntityStatusEnumContributor` interface living in `Workflow.Application/Validation/`.

**Solution (Option A from backlog ARCH-1):** Extract interface to shared contract assembly.

**Execution:**
- NEW: `src/shared/AqtCoreFW.Workflow.Contracts/Validation/IEntityStatusEnumContributor.cs` (interface definition moved)
- DELETED: `src/modules/workflow/AqtCoreFW.Workflow.Application/Validation/IEntityStatusEnumContributor.cs`
- UPDATED: 8 files — usings statements + ProjectReference in Cases.Infrastructure csproj (Workflow.Application → Workflow.Contracts)

**Files changed (10 total):**
1. NEW interface csproj file
2. DELETED interface from Workflow.Application
3-10. Usings + ProjectReference updates

## Evidence

- **Build:** 0E / 0W
- **Tests:** 543 unit / 12 architecture = ALL PASS
- **Guard status:** `NoModule_Should_DirectlyReference_AnotherModule` = GREEN (was RED cf26baec)
- **Code review:** Risk 1/10 (mechanical extraction, zero behavior change, zero migration)
- **Backlog:** ARCH-1 ticked DONE (v2.132p11)

## Why Option A

Cross-module direct deps on Application-layer types violate module isolation. Three options existed:
- (a) **Extract to contract assembly** ← CHOSEN — mirrors existing v2.132p8 ModuleClient split pattern; low-risk mechanical refactor; scalable approach
- (b) Event-driven enrichment — overkill for enum registry pattern; adds complexity
- (c) Whitelist + ignore — defers problem; blocks future cleanup

## What Stayed the Same

- **Namespace:** `AqtCoreFW.Workflow.Contracts.Validation` (unchanged, only assembly changed)
- **Behavior:** Zero behavior change; no business logic affected
- **Migrations:** Zero EF migrations required
- **Consumer APIs:** Cases.Infrastructure still registers enum contributors the same way

## Risk Assessment

**Low risk (1/10):**
- Mechanical refactor: copy interface, update usings
- No behavior change verified by 12/12 architecture tests passing
- Pattern precedent: v2.132p8 split 10 contract assemblies identically
- No dependent code path changes

**Deployment concern:** Minimal — module assemblies still compose identical DI container post-extraction.

## Architecture Impact

Guard `NoModule_Should_DirectlyReference_AnotherModule` now GREEN. Workflow.Contracts now documents 2 public contracts:
1. IWorkflowModuleClient (v2.132p8)
2. IEntityStatusEnumContributor (v2.132p11, Validation subfolder)

Cases.Infrastructure dependency graph: Cases.Infrastructure → Workflow.Contracts only (not Application).

## Lessons

1. **Guard activation reveals violations:** v2.132p10 activated `NoModule_Should_Reference_AnotherModuleSchema_InRawSql`; v2.132p11 resolved the pre-existing architectural debt from that discovery phase
2. **Contract extraction is scalable:** Same pattern used 10× in v2.132p8 ModuleClient split; confidence high
3. **Minimal scope = lower risk:** Single interface extraction (vs replatforming entire module)

## Files Updated in Finalize

1. docs/dependency-map.md — version + Workflow.Contracts interfaces
2. docs/project-roadmap.md — header version + new v2.132p11 row
3. docs/project-changelog.md — new v2.132p11 entry (top)
4. backlogs/backlog.md — ARCH-1 ticked DONE + header version
5. memory/project-state.md — frontmatter + body sections (v2.132p10 → v2.132p11)
6. memory/MEMORY.md — project-state.md row updated (version string)
7. docs/journals/journal-260428-1308-*.md — this file
