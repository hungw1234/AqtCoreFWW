# Journal — Forms DigitalSignature submit coverage (gap A + B)

**Date:** 2026-06-16 08:40 ICT · **Slice:** `SIGN-FORMS-DIGSIG-INTEGRATION-COVERAGE` (backlog T3) · **Mode:** `/cook` · **Type:** test (no production change)

## What

Closed the Slice-3 (C1) coverage gap for the authed DigitalSignature submit path. One unit
assertion + one new 3-case Testcontainers integration file. No production code touched.

## Audit-at-slice-start reframed the row (INVARIANT 260522-A)

The row read as "add 3–5 integration cases (happy + mismatch GOV_FRM_022)". Empirical scout
before planning showed that framing was **half-right**:

- The handler decision logic is **already exhaustively unit-tested** — `SubmitFormCommandHandlerTests`
  has 10 dig-sig cases (valid→evidence, tampered→022+no-persist, required→021, Mode-1/Mode-2
  canonical bytes, TenantId-from-command, no-field→not-invoked), all with a mocked client.
  A full integration suite mirroring those would be DRY/YAGNI duplication.
- grep confirmed **no test anywhere** pins `verifyRequest.SignerUserId == SubmittedBy` — the
  actual C1 anti-impersonation binding the review flagged as "covered transitively". That, plus
  the absence of any *real-DB / real-HTTP* proof, were the genuine gaps.

So the work split into **(A)** a one-line unit assertion (the real binding gap, cheap) and
**(B)** an integration suite scoped to what unit-with-mocks cannot prove (HTTP base64→DTO→command
mapping, the six evidence columns landing in SQL, hard-block → real HTTP status + zero rows).
Scope was a genuine test-investment fork → surfaced to the user (chose targeted A+B) rather than
blindly executing the row's literal "3–5 cases" or silently narrowing it.

## Two ground-truth corrections the row/assumptions got wrong

1. **HTTP status is 422, not 400.** The row said hard-block "400". The submit action maps a
   `ValidationError` Result.Fail through `ToApiResponse` and declares `[ProducesResponseType(422)]`
   → GOV_FRM_021/022 surface as **422 UnprocessableEntity** (confirmed against `FormEndpointsTests`
   which gets 422 for a different validation failure). Tests assert 422.
2. **DigitalSignature is NOT in `FormValidationService.SkipValidationTypes`** (that set is only
   Section/Label/Divider/Formula). So a `required=true` dig-sig field is subject to the generic
   required-value check — if `data` carries no value for the field key, validation returns a
   generic "field required" 422 *before* the signature-specific GOV_FRM_021 ever fires. The tests
   therefore send `data[sigKey]="signed"` to clear data-validation and isolate the signature gates;
   the live-accepted FE necessarily supplies such a value too. (Noted, not filed — works in practice.)

## Key decisions

- **Mock `IIdentitySigningClient` at the Forms boundary.** The cryptographic verify + binding-guard
  verdict is Identity's responsibility, already covered by `InProcessIdentitySigningClientTests` /
  `SignerBindingGuard`. A Forms integration test that tried to produce a real valid signature would
  need real crypto + an enrolled cert — non-deterministic and testing the wrong module. Mocking the
  verdict and asserting Forms' *use* of the contract (server-resolved SignerUserId, evidence
  persistence, all-or-nothing block) is the correct seam.
- **Assert persistence against SQL, not the mock.** The happy-path reads the row back via
  `WebAppFixture.Services` → `FormsDbContext` (the per-test `WithWebHostBuilder` server shares the
  Testcontainers DB), proving the six evidence columns actually persist — the thing `AddAsync`-was-
  called unit assertions can't guarantee.
- **Prove the block is all-or-nothing** by counting submissions for the template's internal Id == 0
  after a failed submit, not merely checking the HTTP status.

## Verification

- Gap A unit: 1/1. Full Forms unit suite: 259/259.
- Gap B: 3/3 new integration. Full Forms integration suite: 17/17 (no regression).
- 0 build warnings (a CS4014 on the NSubstitute capture-setup call was fixed by awaiting it,
  mirroring the unit-test pattern). code-review APPROVE.

## Lesson

When a coverage backlog row says "add N integration tests", first check what the *unit* suite
already proves. Integration tests earn their (Testcontainers) cost only on what mocks can't reach:
real DI wiring, transport/DTO mapping, actual DB persistence, real HTTP status. Re-covering pure
decision logic at the integration level is duplication, not coverage. And verify the row's stated
HTTP codes / contracts against the real controller — they drift.
