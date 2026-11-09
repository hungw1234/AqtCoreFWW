# Cell #3 Slice-1B: HttpOrgUnitModuleClient — Client-Only, Provider Deliberately Deferred

**Date**: 2026-07-16 11:08
**Severity**: Low (scoped-down feature slice, no incident)
**Component**: `AqtCoreFW.Infrastructure.Clients` (shared), `IOrgUnitModuleClient` remote transport
**Status**: Resolved (client-side); provider half filed as a separate follow-up, not built

## What Happened

Built the first remote implementation of a cross-module `I*Client` seam: `HttpOrgUnitModuleClient`, an HTTP/JSON
transport for `IOrgUnitModuleClient` that sits alongside the existing `InProcessOrgUnitModuleClient`, toggled by
`OrgUnitModuleClient:Transport` config (default `InProcess`, zero behavior change out of the box). This is meant
to be the reusable template the other 17 `I*Client` interfaces copy when their modules physically extract.

`/aqt-next` recommended cell #3 slice-1B as the next completeness-matrix step. Scouting the full slice before
touching code showed the "obvious" scope — client + provider endpoint + service auth — is NOT extraction-independent.
A provider endpoint doing cross-tenant OrgUnit lookups needs service-to-service auth, and that's currently
`NoOpServiceIdentityProvider`, explicitly deferred until physical extraction (which itself is gated on FU-A, a
user→auditor question that hasn't been answered). Shipping a provider endpoint now means either an unauthenticated
internal endpoint doing cross-tenant lookups (a real hole, not a hypothetical one) or inventing an auth design
under time pressure that the extraction work will likely have to redo anyway. Neither is acceptable, so the scope
got cut to client-only: build the `HttpOrgUnitModuleClient`, the DI toggle, and the header-propagation wiring;
leave the provider endpoint and its auth story for later, as a named follow-up.

## The Brutal Truth

The instinct on a "build the remote client vertical" task is to build both ends and call it done — a client
with nothing serving it isn't testable against a real network stack, and it feels incomplete. Descoping to
client-only meant accepting the slice can only be proven against a TestServer stub, not a real provider. That's
an honest trade: shipping a provider now would have been shipping either a security hole or a throwaway auth
design, both worse than an admittedly partial slice. The satisfying part is that the reusable value for the other
17 seams was never the provider anyway — it's the client shape, the toggle, and the `TenantPropagationHandler`
wiring, all of which is extraction-independent and now proven.

## Technical Details

- New file: `src/shared/AqtCoreFW.Infrastructure/Clients/HttpOrgUnitModuleClient.cs` — 6 methods
  (`GetOrgUnitsByIdsAsync`, `GetRootOrgUnitForTenantAsync`, `GetRootOrgUnitsForTenantsAsync`,
  `ExpandSubtreeAsync`, `ResolveActiveUsersInOrgUnitsAsync`, `GetUserOrgUnitCodesAsync`), each POSTing to an
  `/internal/org-units/*` path, wire DTOs kept as private nested records so they never leak into `*.Contracts`
  (would inflate the arch-guard's contract census).
- Placed in shared `AqtCoreFW.Infrastructure`, not `organization.Infrastructure` — so a post-extraction caller
  gets the HTTP client without dragging in Dapper/`OrgDbContext`. The transport toggle itself stays in
  `OrganizationInfrastructureRegistration`, the only assembly that sees both implementations.
- Activated the previously-dormant `TenantPropagationHandler` (X-Tenant-Id / X-Correlation-Id / service token
  on every outbound call) — first real consumer of that handler.
- Behavior parity with the in-process client: same input-normalization, same empty-input short-circuits, but
  **two different batch caps**: `GetOrgUnitsByIdsAsync` caps on the DISTINCT id count, `GetRootOrgUnitsForTenantsAsync`
  caps on the RAW (pre-distinct) count. Cap enforced client-side before the HTTP call (contract guarantees
  `ArgumentException` on every transport; also saves a wasted round-trip). Deliberately no client-side chunking —
  a single HTTP request has no SQL `IN`-list limit, so any chunking is the provider's problem.
- Real bug caught by the tests, not by inspection: `Results.Json(null)` in the TestServer stub returns an
  **empty body**, not the literal string `"null"`. `HttpClient.ReadFromJsonAsync` throws on an empty body
  (`JsonException: The input does not contain any JSON tokens`). Fixed by reading the body as a string first and
  treating empty/whitespace as `default` before deserializing — same idiom already used in
  `ViettelMysignLoginClient`. This is realistic provider behavior (ASP.NET Minimal APIs do this), so it's a
  correctness fix, not a test-only hack.
- 20 xUnit facts in `tests/integration/AqtCoreFW.Organization.Integration.Tests/Clients/HttpOrgUnitModuleClientTests.cs`,
  all driven through `AddHttpOrgUnitModuleClient` (the real registration path) with only the primary
  `HttpMessageHandler` swapped for an in-memory TestServer stub — per the standing lesson that new cross-cutting
  DI/pipeline wiring needs to be proven through real registration, not hand-rolled setup.

## What We Tried

Considered building the provider endpoint too, gated behind a feature flag, to have a "complete" vertical slice
ready for extraction day. Rejected: a flag doesn't fix the auth gap, it just hides it — anyone flipping the flag
in a shared environment gets an unauthenticated cross-tenant lookup endpoint. Decided the honest move was to
name the gap explicitly and file it rather than build around it.

## Root Cause Analysis

Not a failure — a scope correction caught before code, not after. The near-miss was assuming "cell #3 slice-1B"
meant "build the vertical" without first checking what the vertical actually touches. `/aqt-next`'s recommendation
was directionally right (this is the correct next step) but didn't carry the extraction-coupling detail; that
only showed up once we grepped for what `NoOpServiceIdentityProvider` actually gates.

## Lessons Learned

- Scout-before-scope on a "build now" recommendation is not optional even when the row already looks
  extraction-independent on paper — cell #3 slice-1B looked clean until the provider-auth dependency surfaced.
- Splitting a vertical slice at its extraction-coupling boundary (client vs. provider) kept the reusable half
  buildable now instead of blocking the whole thing on FU-A.
- Mirrored-guard tests must pin the ONE differing argument or they pass vacuously. Code review (APPROVE_WITH_NITS,
  0 Critical/High) caught a real Medium: both over-cap tests used all-unique GUIDs, so distinct count == raw
  count and the tests never actually distinguished the by-ids-DISTINCT cap from the roots-RAW cap — a
  copy-paste-shaped false green. Fixed with 2 discriminating tests using duplicate GUIDs (raw > cap but
  distinct <= cap: by-ids proceeds with 1 hit, roots throws with 0 hits) plus one more for the roots
  empty-input short-circuit.

## Next Steps

- L1/L2 review nits (provider must return canonical UPPER-trimmed codes; PascalCase-request /
  case-insensitive-response wire assumption) carried forward to the provider follow-up — not built this slice.
- Provider endpoint + service-to-service auth design stays blocked on FU-A (auditor question,
  `docs/microservices-compliance-isolation-auditor-questions.md`) — no owner action until that answer lands.
- Verification on record: build 0W/0E; tests 20/20; architecture guards 16/16 (contract census unchanged at 18,
  module isolation intact); `AqtCoreFW.OrgUnit.Contracts` stays a leaf ProjectReference (zero refs, no cycle);
  default `InProcess` transport proven byte-identical via the toggle tests.
- Not yet committed to git as of this entry (working tree has the new/modified files); commit + backlog/changelog
  update is the immediate next action before this can be called fully shipped.
