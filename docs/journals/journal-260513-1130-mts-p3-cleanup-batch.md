# Journal — MTS P3 Cleanup Batch

**Date:** 2026-05-13 11:30–12:10 ICT
**Branch:** `main` (4 direct commits)
**Plan:** `plans/260513-1130-mts-p3-cleanup-batch/`
**Backlog:** MTS-4 / MTS-5 / MTS-6 / MTS-8 (manual-test-session 2026-05-11 follow-ups, all P3)

## Context

User asked "what to do next?" — repo at ~99%+, no P0/P1 open. First instinct (T4-05 Ph02-04, parent header still 🟠 PARTIAL) turned out to be a stale backlog header: `git log` showed every phase + FU2-5 shipped 2026-04-20 → v2.123 (commits `6a04f8ff` / `347ef36a` / `14eec5af` / `17cd8b4d` / `d979b81d` / …). The v2.121 finalize commit *claimed* it ticked the backlog but a later edit re-wrote the parent header back to PARTIAL — same backlog-drift pattern hit several times before. Corrected the header (commit `5d145523`) and also committed the un-committed PR #105 journal (`1002493f`) before starting any new code.

User then picked the MTS P3 batch — the small tech-debt residue from the 2026-05-11 manual-test session.

## What Shipped

### MTS-8 — `WorkflowDefinition.Clone()` deep-copy guards · commit `a65a593b`
Two `Fact`s added to `WorkflowDefinitionVersioningTests.cs`:
- `Clone_DeepCopiesStateVisualDesignerPositions` — pins `PositionX`/`PositionY` (`double?`) on cloned states. Uses real values (120.5, 340.25, –42.0, 0.0) to catch a future shallow-copy where defaults could silently slip through.
- `Clone_DeepCopiesTransitionSourceAndTargetHandles` — pins `SourceHandle = "right"` / `TargetHandle = "left"` on cloned transitions.

Both passed first run (16/16 versioning suite green). The fields were *already* copied by `WorkflowDefinition.cs:105,121` — these tests just lock the behaviour against silent regression. Cheap insurance for the Visual Designer feature that shipped uncovered.

### MTS-5 — Dapper handler sweep (verification only)
Repo-wide grep across collaboration / ai / audit Dapper handlers. Result: **no remaining `DateTimeOffset` Row bridges**. Three Row classes survive (`ConversationRow` in `GetConversations`, `MessageRow` in `GetMessages`/`SearchMessages`, `UnreadRow` in `GetUnreadCount`, `ExportRow` in `ExportAuditLogs`) but every one is legitimate row-shaping (cross-module sender-name enrichment, aggregation, CSV-export shaping with `r.DeletedAt.HasValue` transform), not a type-mismatch workaround. No code change.

### MTS-4 — Collapse `Row`-bridge handlers → direct DTO · commit `c8c53231`
Three handlers used an intermediate sealed record (`Row` / `TemplateRow`) purely to bridge `DateTime` → `DateTimeOffset` because Dapper couldn't materialise `DateTimeOffset` from a `datetime2` column. After the global `SqlMapper.TypeHandler<DateTimeOffset>` + nullable variant were registered (`DapperTypeHandlers.cs:28–29` ← `InfrastructureRegistration.cs:40`), the bridge became dead verbosity:

| Handler | Before | After |
|---|---|---|
| `GetDocumentTemplatesQueryHandler` | `QueryAsync<TemplateRow>` → `.Select(r => new DocumentTemplateDto(…, new DateTimeOffset(r.CreatedAt, TimeSpan.Zero), …))` | `QueryAsync<DocumentTemplateDto>` — enum members from `int` columns (Dapper native), `DateTimeOffset` from `datetime2` (global handler) |
| `GetNotificationTemplatesQueryHandler` | `QueryAsync<Row>` + `.Select` projection | `QueryAsync<NotificationTemplateDto>` |
| `GetNotificationTemplateByIdQueryHandler` | `QueryFirstOrDefaultAsync<Row>` + manual ctor | `QueryFirstOrDefaultAsync<NotificationTemplateDto>` — `dto is null` → `NotFoundError` |

SQL unchanged in all three (still `SELECT PublicId AS Id, …` for the T2 Notifications). Removed one orphan `using AqtCoreFW.Files.Domain.Entities;` along with the dead enum casts.

**Regression guard:** added `TC-NOT-INT-006` in `NotificationEndpointsTests.cs` — create template → `GET /api/v1/admin/notification-templates?tenantId=…` → assert the created row appears with `templateKey`/`channel`/`createdAt` (parsed as `DateTimeOffset`, asserted recent) → `GET …/{id}` → same → unknown id → 404. This covers both list and by-id handlers against real SQL Server (Testcontainers). The Files unit/integration suite doesn't currently hit `GET /api/v1/document-templates` against a seeded template, so the `GetDocumentTemplatesQueryHandler` change rides on mechanical equivalence to the Notifications path + the global-handler guarantee; flag as a known coverage gap if it ever regresses.

The new integration test ran 1/1 in 19s. Tester pass sweep: 274 BE (Workflow.Domain 122 + Notifications unit 26 + Files unit 112 + Notifications.Int 7 + Files.Int 7) + 7 FE — no regression.

### MTS-6 — Move `notifyChange` out of React-Flow state updaters · commit `ae2c6574`
`react-flow-canvas.tsx` was calling `notifyChange(...)` (which invokes the parent `onGraphChange` callback) **inside** `setNodes((nds) => { notifyChange(nds, edges); return nds; })` and the equivalent `setEdges` body — a textbook React-18 anti-pattern. Updater bodies are required to be pure; under `StrictMode` React double-invokes them in dev, and the side effect would fire twice.

Refactor (user picked "useEffect approach" over "imperative call after setState"):

```tsx
const userDrivenChange = useRef(false);
const hasMounted = useRef(false);

// Re-sync from props (save/undo) — defensively clear the flag so external syncs don't echo.
useEffect(() => { userDrivenChange.current = false; setNodes(buildNodes(states, positionMap)); },
  [states, setNodes, positionMap]);
useEffect(() => { userDrivenChange.current = false; setEdges(buildEdges(transitions)); },
  [transitions, setEdges]);

// Post-commit notify — fires after React commits the new state, never inside an updater.
useEffect(() => {
  if (!hasMounted.current) { hasMounted.current = true; return; }
  if (!userDrivenChange.current) return;
  userDrivenChange.current = false;
  notifyChange(nodes, edges);
}, [nodes, edges, notifyChange]);
```

Each user-driven handler (`handleNodesChange`, `handleEdgesChange`, `handleConnect`, `handleReconnect`) sets `userDrivenChange.current = true` synchronously *before* calling the relevant setState. The effect fires once on the next commit with the up-to-date `nodes`/`edges`, then clears the flag.

Side benefits:
- The hacky `notifyChange(nodes, [])` in `handleReconnect` (which tried to mark "dirty" while the edges array was being mutated by another setEdges in flight) disappears — the setEdges change naturally triggers the effect with the correct arrays.
- The mount guard (`hasMounted`) means the parent doesn't get a spurious `onGraphChange` call on initial render. New test `R11-D` pins this.

Code-review caught two nits, both folded in before commit:
- **P2-1** — my `Edit` had anchored on the `/// <summary>` line that was actually the *opener* of `CreateClientWithPermissions`'s doc comment, splitting the doc block. Restored the original opener and gave the new test a plain `//` block instead of mixing styles.
- **P3-1** — re-added `as typeof eds` cast on the `addEdge` return in `handleConnect`, matching the `handleReconnect` style and preserving the literal `type: 'workflowTransition'` discriminator.

### Backlog + changelog sync · commit `2249530f`
- `backlogs/backlog.md` — MTS-4 / MTS-5 / MTS-6 / MTS-8 marked DONE 2026-05-13; MTS-7 stays open (manual UI E2E, out of scope). Last-updated header bumped.
- `docs/project-changelog.md` — v2.137p11 patch entry for the batch.

## Numbers

- **Files changed:** 9 (3 BE handlers, 2 BE tests, 1 FE component, 1 FE test, 1 docs, 1 backlog)
- **LOC:** +163 / −80 (heavy net +ve from comments + the regression integration test)
- **Build:** `dotnet build src/AqtCoreFW.slnx` — 0E / 2 pre-existing NU1510 warnings
- **Tests:** 274 BE + 7 FE PASS, no regression; new: 2 unit + 1 integration + 1 FE
- **FE typecheck:** clean
- **Code-review:** 9.0/10 APPROVE_WITH_NITS → nits fixed
- **Commits:** 4 on `main` (`a65a593b` / `c8c53231` / `ae2c6574` / `2249530f`)
- **Push:** intentionally skipped — user asked for local commits only
- **Earlier in session (housekeeping, before this batch):** `5d145523` corrected stale T4-05 parent header, `1002493f` committed the orphan RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS journal.

## Lessons / Patterns

1. **Verify before cooking — for the 5th+ time.** The user-picked task (T4-05 Ph02-04) was 3-week-old shipped work that the backlog parent header had forgotten about. Spending 2 minutes on `git log --oneline | grep T4-05` saved ~5 hours of speculative reimplementation. Memory `verify-fe-consumer-before-be-build` extends naturally to *every* backlog row: source-verify before cook, no matter how detailed the plan looks.

2. **Dapper "Row" bridges have a specific job — `DateTimeOffset` is no longer that job.** Once the global type handlers landed, the bridge became cargo-culted from older code. MTS-4 removed three. MTS-5 confirmed there aren't any more lurking in collaboration/ai/audit. Worth a one-liner in `code-standards.md` next time someone touches the Dapper section: "Row records are for shaping (joins, aggregations, transforms), not for type mismatch — `DateTimeOffset` materialises via the global handler."

3. **`Edit` anchoring on `/// <summary>` is a foot-gun.** When you insert a new method into a class, anchoring before the first `/// <summary>` you find can split the *previous* method's doc block. Worth either picking a more specific anchor (the actual method signature) or inspecting ±2 lines before applying.

4. **The "useEffect for side-effects after state change" pattern needs a flag-and-mount-guard pair to behave correctly in the presence of prop re-syncs.** Without `hasMounted`, the parent gets a spurious notify on initial render. Without `userDrivenChange`, external re-syncs (save/undo) echo back as user edits. The combo is cheap and pins both ends.

## Coverage Gap (Flagged)

`GetDocumentTemplatesQueryHandler` got the same `Row` collapse but no integration test currently exercises `GET /api/v1/document-templates` against a seeded template (the Files integration tests use role-based clients without `PermCodes.Files.Template.Manage`, so they can't create a template to list). The change is mechanically identical to the Notifications path and rides on the global handler — but a future P3 to either (a) extend `FileEndpointsTests` with a permission-stub helper and add a list/by-id round-trip, or (b) manually curl-verify after a real deployment, would close the gap. Recorded as an out-of-scope note rather than a backlog item because it's a coverage-improvement rather than a bug.

## Next

- MTS-7 (manual Visual Designer UI E2E — drag → save → reload → assert positions render) is the only MTS row still open. Manual test on the user's end.
- RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING remains blocked on ops (DBA migration login + Vault key + ESO entry). Repo-side uncomment is ~15min once ops checklist clears.
- RLS-PRINCIPAL-FAILFAST (P3, YAGNI-flagged) and SKIP-STARTUP-MIGRATIONS-FU1 (P3, YAGNI) sit waiting if a future tightening cycle picks them up.
