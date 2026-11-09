# Journal — 2026-05-14 17:45 ICT — BP MVP P1 SHIPPED (v2.137p23)

**Commit:** `ef5491f4` on `main` (unpushed) · **Branch:** `main` · **Session length:** ~45 min · **22nd ship of the cluster**

## Session Goal

Execute Phase 1 of the BusinessProcess MVP plan (`plans/260420-1957-business-process-definition-mvp/phase-01-form-template-version-entity.md`) in `/cook --auto` mode: add an immutable `FormTemplateVersion` snapshot entity that the `PublishFormTemplate` command writes atomically alongside the status flip. BP can then pin its form bindings to a stable contract regardless of subsequent template edits.

## What Worked

- **Atomicity pattern via paired repo methods** — `IFormTemplateRepository.AddVersionAsync(version, ct)` stages the entity in the change tracker WITHOUT calling SaveChanges; the existing `UpdateAsync(template, ct)` then flushes both the FormTemplate update and the FormTemplateVersion insert in a single transaction. Verified with `Received.InOrder(() => { AddVersionAsync; UpdateAsync; })` in handler unit test. Cleaner than injecting `FormsDbContext` into the handler — NSubstitute mocks stay tidy.
- **RLS parity baked into the same migration** — `RlsMigrationHelper.GenerateRlsPolicyStatements("forms", "FormTemplateVersions")` ensures the new tenant-scoped table is protected at DB engine level from day-one (Phase-06 lesson from `phase6-rls-restore-shipped-260512` memory — Forms RLS was previously LOST when migrations were regenerated).
- **JSON envelope versioning** — `schemaEnvelopeVersion: 1` constant in the factory + private sealed record payload types pin the JSON shape at compile time. P6 will bump to 2 when adding `prefillSource`; readers tolerate missing keys for forward-compat.
- **Defense-in-depth tenant filter on JOIN** — advisor caught it pre-commit. Get handler now filters `TenantId` on BOTH `v.` AND `t.` sides because production may run as `db_owner` (RLS bypassed). One-line cost; high-payoff insurance.
- **Code-review fix-up turnaround** — initial 8.5/10 score had F1 (`template.Version` drift) + F2 (substring assertion) + F3 (200+empty contract ambiguity) + F5 (handler comment). All folded into the same commit within 10 minutes of the review report; tests re-ran green; no second review needed.

## What Didn't / Lessons

- **`dotnet ef` PATH glitch on Windows** — `dotnet tool restore` reported success but `dotnet ef --version` insisted "Run dotnet tool restore". Workaround: invoke the binary directly via `~/.dotnet/tools/dotnet-ef.exe`. Cost: ~3min. Likely a stale tool-manifest cache between the local manifest at `.config/dotnet-tools.json` and the user-level manifest at `dotnet-tools.json`. Not investigating until it bites a second time.
- **`Entity.Id == 0` collision pre-save** — `FormTemplate.DeactivateField(fieldId, ...)` does a by-Id lookup over `_fields`; when both fields are unsaved (Id=0), the lookup matches the first. Test bypass: call `field.Deactivate()` directly with an explanatory comment. Same final mutation, scoped to verifying the snapshot serialization filter only. Codified in P1 memory.
- **Substring JSON assertion looked fine, wasn't** — `Should().Contain("\"version\"")` matched BOTH `"schemaEnvelopeVersion"` and `"version"` keys. Tightened to `JsonDocument.Parse + GetProperty(...)`. Generic lesson: never substring-assert JSON when the schema has multiple keys ending in the same suffix.

## Decisions Made

- **D1: Keep single-handler-per-file convention, but co-locate List + Get** — `ListFormTemplateVersionsQueryHandler.cs` contains both handlers. Cohesion outweighs the convention here because they share Dapper row records and a TenantId-on-JOIN pattern. Reviewer accepted.
- **D2: 200+empty contract for `/versions` list endpoint** — `GET /api/v1/forms/templates/{publicId}/versions` returns 404 only if the template is missing; 200 with empty array if the template exists but has no snapshots (Draft never published). Documented in controller XML doc.
- **D3: P1-FU `RepublishFormTemplate` deferred not blocking** — `FormTemplate.AddField` on Active templates increments `template.Version` but Publish is Draft→Active only, so live Version can drift higher than `max(FormTemplateVersion.VersionNumber)` silently. BP renderer pins to v1 safely; live Version drift is informational only. Filed P1-FU instead of refactoring `Version++` semantics now (would touch too much MVP scope).
- **D4: Soft-delete + unique-index trap accepted, no filtered index** — snapshots are append-only by design; FK is `Cascade` (hard-delete vaporizes children); no domain method updates a snapshot. Adding `WHERE IsDeleted=0` filtered index is YAGNI until a snapshot soft-delete path is introduced.

## Test Status

| Suite                          | Result          |
|--------------------------------|-----------------|
| Forms.Domain (unit)            | 116 / 116 PASS  |
| Forms.Application (unit)       | 143 / 143 PASS  |
| Architecture                   | 97 / 97 PASS    |
| Solution-wide build            | 0E / 0W         |
| Forms.Integration              | build-only (Testcontainers/CI) |

New tests this session: 7 `FormTemplateVersionFactoryTests` (Domain) + 5 `PublishFormTemplateCommandHandlerTests` (Application: snapshot staging order, JSON envelope structure, skip-on-failure, multi-field ordering, Table-mode parity) + 1 integration spec (`Publish_CreatesVersionSnapshot_VisibleViaListAndGetEndpoints`, runs in CI Testcontainers).

## Next Session

1. **P2** — FormSubmission `WorkflowInstanceId/StateId` linkage + query-latest + `IFormSubmissionRunClient` contract (0.5d). Status reset to `pending` per Validation D2 — only the `WorkflowInstanceId` field shipped early; the `WorkflowStateId` column + migration + factory + contract are all missing. Unlocks P3.
2. **P3** — BusinessProcess module scaffold (3 entities + RLS + migration). Blocks P4.
3. **Push `main`** — 4 commits unpushed since `92db0981` (`ef5491f4` BP MVP P1 + p21–p22 + earlier doc commits). Decide whether to push as a batch or hold until P2 lands.
4. **P1-FU `RepublishFormTemplate`** — only file the row in backlog; do not implement until first admin asks "how do I edit a published form schema?" or a BP renderer mismatch is reported.

## Cross-References

- Plan: `plans/260420-1957-business-process-definition-mvp/phase-01-form-template-version-entity.md` (status flipped to `SHIPPED 2026-05-14`)
- Validation Log Session-3 entry appended to `plan.md` at 17:15
- Memory: `~/.claude/projects/C--AqtCoreFW/memory/bp-mvp-p1-shipped-260514.md`
- Predecessor: [`journal-260514-1708-bp-mvp-p0-otel-shipped-v2137p22.md`](journal-260514-1708-bp-mvp-p0-otel-shipped-v2137p22.md)
- Risk doc'd in phase-01.md: `template.Version` drifts via AddField-while-Active without snapshot → P1-FU `RepublishFormTemplate`
