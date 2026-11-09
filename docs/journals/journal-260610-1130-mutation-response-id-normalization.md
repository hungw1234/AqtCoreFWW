# Journal — Mutation Response Id Normalization (2026-06-10 ~11:30)

**Slice:** `/cook WF-MUTATION-RESPONSES-UNNORMALIZED-IDS` (interactive) · commit `6783badf` · 4th ship of the day · FE-only future-proofing.

## What shipped

The read query already normalizes BE long ids (JSON numbers → strings) at the API boundary so the designer's string-keyed save map resolves graph refs. The definition-returning **mutation** hooks returned raw `r.data`. Piped all four — `useCreateWorkflow`, `useUpdateWorkflowDefinition`, `useCloneWorkflowDefinition`, `useSaveDefinitionGraph` — through `normalizeDefinitionIds`. 4 hook tests guard it.

## The review catch (M1)

I scoped 3 hooks by grepping for `<WorkflowDefinitionDto>`-returning mutations. The code-reviewer found a 4th, `useUpdateWorkflowDefinition`, that my grep missed because its `apiClient.put(...)` had **no generic type** — so it didn't match the `<WorkflowDefinitionDto>` pattern. But it returns a full DTO via the same `MapToDto` as create (numeric ids), so it's the identical leak class. Included it + added the missing generic.

## Lessons (reusable)

- **A type-based grep sweep misses untyped call sites.** Searching for `<WorkflowDefinitionDto>` found 3 of 4 hooks; the 4th was untyped and invisible to that pattern. When enumerating "all X-returning functions," cross-check against the BE return shape (here: which handlers return `MapToDto`), not just the FE generic annotation. The untyped return was both the reason it was missed and a latent `any`-leak worth fixing on its own.
- **Future-proofing is legitimate scope when the leak class is already proven.** No consumer reads graph refs off these responses today, but the exact same numeric-id-into-name-keyed-payload bug bit the read path this morning. Closing the mutation path before a future consumer reintroduces it is cheap insurance, and the row was explicitly filed for it.
- **Match the mock path to the actual import.** The new test mocks `@/core/api` (what `workflow-api.ts` imports); a sibling test mocks `@/lib/api-client` but gets away with it only because it calls the pure function directly without exercising the client. For a hook test that renders the hook, the mock path must be right or the real client loads.

## Verification

- 4 hook tests (mock `@/core/api`, renderHook, assert branch family + states + transitions come back string; each fails if the hook returned raw data).
- workflow folder 27 files / 240 tests green (236 prior + 4 new) · tsc 0.
- code-review APPROVE (DONE_WITH_CONCERNS → M1 resolved in-scope by adding the 4th hook).
- FE-only; clone/create/update/save-graph API responses unchanged on the wire; no dependency-map impact.

## Carry

- Push pending (committed local `6783badf`).
- 2 `E2E Parallel` orphan pairs still in dev DB (harmless; not filed).
- Open-Q carry: does the FE suite run in CI?
- Stack left UP (5001/5002/5173).
