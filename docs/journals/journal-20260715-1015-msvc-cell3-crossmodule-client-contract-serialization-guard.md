# Journal — Cross-Module Client Contract Serialization-Safety Guard (Cell #3 slice-1A)

**Date:** 2026-07-15 · **Session start:** ~09:00 ICT
**Track:** Microservices transformation (🥇 #1) — completeness-matrix cell #3 "Sync inter-service + auth propagation"
**Ships:** 1 (`24ffa71b`, test-only + docs)
**Plan:** `plans/260715-0902-msvc-cell3-crossmodule-client-contract-purity-guard/` (1-phase)

---

## What & why

`/aqt-next` (via `/ak:cook "what to do next?"`) surfaced that the `[audit]` read-isolation arc is code-exhausted (only FU-A, a non-code auditor action, remains on the physical-extraction path). User directed: **focus on microservices code, defer the confirm-docs (FU-A/OPS)**. That forced the real question — what microservices code exists that is *not* gated on the auditor decision?

The completeness matrix (§4) answers it: the **five hard ❌ cells** (#3 sync-client, #5 saga, #6 authz-remote, #7 audit-chain, #10 test-substrate) are monolith-side, extraction-independent, and decision #6 sanctions building them now. User picked **cell #3** — "18 `I*Client` in-process → remote = largest workstream".

Scout then reframed the slice. The seam infra is already ~built (the matrix's "80% ready in the hard places"): `TenantPropagationHandler` is a complete outbound `DelegatingHandler`, `IServiceIdentityProvider` is abstracted, and the contracts pass only DTOs. What's missing is (a) the `Remote*Client` layer and (b) a guard that keeps the contracts remote-ready. Slice-1A = the **guard first** (cheap, locks the invariant, empirically proves the "80% ready" claim); slice-1B (first `Remote*Client` vertical) = next.

## What shipped

- **`CrossModuleClientContractSerializationSafetyTests`** (4 `[Fact]`) — asserts every method (param + return, `Task`/`ValueTask` unwrapped) on all **18** cross-module `I*Client` contracts under `src/shared/*.Contracts` is remote-serialization-safe. A **default-deny recursive** type checker (denylist `Stream`/`IQueryable`/`Expression`/delegate/`Type`/`DbContext`/`.Domain`-ns/ASP.NET; allowlist scalar/enum/raw-JSON/safe-collection/`*.Contracts`-POCO with a cycle guard). Discovery is **hybrid** — a source-text census over the `*.Contracts` folders cross-checked bidirectionally against reflection, so a new *unreferenced* `*.Contracts` assembly can't silently undercount (the failure the pure-reflection sibling `AuditKeyedReaderAllowlistTests` deliberately avoided with a source sweep).
- **12 `ProjectReference`** to the cross-module `*.Contracts` assemblies in `AqtCoreFW.Architecture.Tests.csproj` (Cases.Contracts intentionally omitted — empty scaffold, census maps it to zero clients).
- **Two carve-outs** for schema-less-but-serializable payloads: `object` as a collection value (form-prefill value bag) and `JsonDocument`/`JsonElement` (raw JSON).

## The finding the guard caught

The planner verified GREEN over 5/12 assemblies. The guard, scanning all 12, caught what the manual scout missed: **`IRuleEngineModuleClient.EvaluateAsync(JsonDocument input)`**. `JsonDocument` is the strongly-typed sibling of the `object` value-bag already carved out — dynamic JSON that does cross a wire. User decided to **whitelist `JsonDocument`/`JsonElement`** (consistent with the `object` carve-out) rather than touch the public contract. This is the guard doing its job: surfacing a remote-readiness question human review overlooked, on its very first run.

## Verification

- Guard **4/4**; full `Architecture.Tests` **178/178** (174 baseline + 4); build **0W/0E** (`-warnaserror`).
- Code-review **APPROVE_WITH_NITS** — 0 Critical/High. One Medium ("POCO recursion inspected public properties but not public fields — a latent hole in the default-deny promise for a future field-based DTO; zero live instance") **fixed in-slice** by adding `GetFields` alongside `GetProperties`; re-verified 178/178. Anti-vacuity design (bidirectional census↔reflection + pinned count 18 + 3 mutation-proof facts) called the strongest part.

## Decisions & scope calls

- **Guard-first (1A) over jumping to the vertical (1B)** — cheap, empirically proves the contracts are remote-clean before building transport on them; if it had found a dirty contract, the work reorders. It did find one (JsonDocument), resolved by carve-out not contract change.
- **Cases.Contracts excluded** — the planner said 12 `*.Contracts`; the filesystem has 13. Cases is an empty scaffold (no client, `obj/`-only). Refined the algorithm so the census (not a blind glob) decides which assemblies to load — handles Cases cleanly *and* still catches a future new assembly that *does* declare a client.
- **`JsonDocument` whitelisted, contract untouched** — tightening `IRuleEngineModuleClient` to a typed input is a public-contract change, out of the test-only slice; filed nothing for it (dynamic rule input is a legitimate design).
- **Light-touch on IRuleEngineModuleClient's stale XML-doc** (`Microservice: Grpc…` — decision #9 moved to HTTP) — noted in dependency-map, not fixed (out of scope).

## State after

Cell #3 slice-1A done. **Slice-1B** (`MSVC-CELL3-SLICE1B-REMOTE-CLIENT-VERTICAL`, ready/unblocked) is the next code-able microservices step — a first `HttpOrgUnitModuleClient` vertical (typed `HttpClient` + `TenantPropagationHandler` wiring + internal endpoint + DI toggle). Physical-extraction wave-sequencing (S1/S2) still gates on **FU-A** (auditor, non-code) — unchanged.

## Lessons reinforced

- `feedback-reverify-plan-before-execute` — the plan said 12 `*.Contracts`; the filesystem had 13 (Cases). Re-probing paths before implementing caught the drift before it became a runtime `Assembly.Load` failure.
- `feedback-red-team-before-implement` / guard-as-oracle — the guard itself was the red-team: it found the `JsonDocument` seam the human scout (5/12) missed. Cheap guards over broad surfaces beat exhaustive manual scans.
- `feedback-verify-subagent-reports` — sampled the docs-manager output (backlog rows, changelog) and the code-reviewer's field-recursion finding before trusting them.

## Unresolved questions

- None blocking. (Deferred, YAGNI: a POCO field-mutation-proof test + a generic-client regex — filed as a sub-note on the slice-1B row; both need slice-1B's concrete shape first.)
