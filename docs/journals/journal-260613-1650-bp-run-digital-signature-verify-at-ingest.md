# Journal — Digital signatures in BusinessProcess-Run forms

**Date:** 2026-06-13 16:50
**Trigger:** `/ck-debug` on a user-reported symptom — "Ký số chưa khả dụng" Alert when starting the "test ký số" business process at `/business-process-runs/new`.
**Outcome:** Root-caused a three-layer gap, implemented end-to-end signing across all three BP-run form entry points + an authorization widening. Committed `4b62a93d`, pushed origin + bitbucket.

## The symptom and the real root cause

The Alert was the `DigitalSignatureWidget` "unavailable" state. The shallow cause: the BP-run intake form rendered the widget without a `signingProvider`. But debugging past the symptom revealed a **three-layer gap**, not a one-line wiring miss:

1. **FE provider discovery** — `new-bp-run-wizard.tsx` and `form-tabs-container.tsx` called `renderInteractiveField` without `signingProvider`, so `providerKey` was null → "unavailable".
2. **FE payload threading** — both `handleFinish` paths called `cleanFormData` (which drops the DigitalSignature field) but never `extractSignaturePayload`, so even a signed value would be discarded before submit.
3. **BE contract** — `IFormSubmissionRunClient.CreateForBpRunAsync` had no signature parameter. Both BP-run handlers built `SubmitFormCommand` with `Signature = null`.

Consequence (verified in `SubmitFormCommandHandler`): a BP-run form with a **required** DigitalSignature field hard-blocks with `GOV_FRM_021` permanently; an **optional** one silently drops the proof (no integrity evidence). The DigitalSignature field type was globally registered but verify-at-ingest was only wired for the standalone Forms submit path — the field type "leaked" into BP-run forms where the chain didn't exist.

**Lesson:** the reported symptom (a misleading Alert) sat one layer above the load-bearing defect (a cross-module contract with no transport for the proof). Tracing every layer before proposing a fix turned a "pass signingProvider" patch into the correct multi-module slice.

## Key decisions

- **Cross-module carrier (the architectural crux).** BusinessProcess.Application references `Forms.Contracts` but not `Forms.Application`, so it cannot see the Forms `SignaturePayload`. Added a transport-neutral `BpRunSignaturePayload` record in `Forms.Contracts` (mirrors the sibling `SignatureVerifyRequest` byte-array convention) and mapped carrier→`SignaturePayload` at the **single** point that already crosses into Forms.Application — `InProcessFormSubmissionRunClient`. Module isolation preserved; one mapping point feeds all call-sites (DRY).
- **Authorization widening (user decision).** The Mode-2 doc-hash endpoint was gated `FORMS.SUBMIT.CREATE`-only; BP-run fillers hold `BUSINESSPROCESS.BP.VIEW`. The existing permission model has no OR. Added a reusable `AnyPermissionRequirement` + handler behind a named `Policies.SignatureDocHashAccess` policy. Threat-model validated safe: the endpoint returns only a SHA-256 of caller-supplied data for a template the caller already renders, write-ops rate-limited. The standalone-submit endpoint stays Create-only.
- **The discovered third path.** Adding the contract parameter broke the build at a *third* caller I hadn't planned for — `ExecuteBusinessProcessTransitionCommandHandler` (bundled transition forms). The build forced the touch; leaving `signature: null` there would have recreated the identical hard-block in a third surface. After confirming scope with the user, wired the transition path too (BE command/handler/controller + the imperative-handle `transition-confirm-modal` with a `getSignature()` on the panel handle). All three BP-run form surfaces now support signing.

## Verification

- Full solution build (`-p:TreatWarningsAsErrors=true`): 0W/0E.
- `BusinessProcess.Application.Tests`: 105/105 — three carrier-passthrough tests, one per entry point, each asserting the exact carrier reference reaches the Forms client.
- Architecture suite: 107/108 — the one failure (`Qd742Phase03ComplianceTests` MFA-claim const location) is pre-existing and unrelated (a prior refactor moved `MfaAtClaimType` to `MfaClaimEvaluator`; the test still reflects the old location). `ModuleIsolationTests` + the QĐ-742 signing-compliance test passed.
- FE: `tsc --noEmit` clean; full vitest 1035/1035 (hand-wiring `useSubmitBpStateForm` + `useExecuteBpTransition` off their Orval aliases caused no regression; no test mocked those hooks).
- code-review: APPROVE_WITH_NITS, 0 Critical/High. The one Low (malformed base64 → 500) was applied for the new code path (`MapSignature` now throws `ArgumentException` → 4xx).

## Notes / carry-forward

- **Byte-parity was not a new risk** here: both BP-run paths already funnel through `SubmitFormCommand → FormDataCanonicalizer`, so the FE↔BE canonical contract is the same one shipped earlier — no new surface.
- **TenantId** stays server-resolved from JWT; the carrier never carries the signed content (recomputed server-side per QĐ-742).
- **Follow-up (non-blocking):** a BP-level integration test asserting intake-verify-fail → no orphan `WorkflowInstance` would close the rollback path empirically at the BP boundary (currently verified by read + the existing Forms verify suite; the unit tests mock the Forms client).
- **Pre-existing red:** the stale MFA compliance test is unrelated to this work — left for a separate fix.
