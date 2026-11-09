# Journal — MTS-9: Architecture Test Suite Compile Fix (2026-05-12 09:30 ICT)

## What

`AqtCoreFW.Architecture.Tests` had not compiled since W7 signing commit `bec64e2d` (2026-05-11). Fixed the 2 compile errors, then fixed the 3 stale test expectations the green build then exposed. **Architecture suite: 0 errors → 94/94 PASS.** Test-only change, 3 files.

## Compile errors fixed

- `Signing/SignatureProviderCapabilityArchTests.cs` — CS1929: `signAsyncMethod.Should().NotHaveAbstractBody(...)` — `.Should()` on a `MethodInfo` returns FluentAssertions' own `MethodInfoAssertions`, not a `MethodInfo`, so the file's local `this MethodInfo` extension never bound. Replaced with `signAsyncMethod!.IsAbstract.Should().BeFalse(...)`; deleted the now-dead helper class.
- `Signing/CallbackAuthArchTests.cs` — CS0234: `Microsoft.AspNetCore.Mvc.HttpMethodAttribute` doesn't exist; the base attribute lives in `Microsoft.AspNetCore.Mvc.Routing`.

## Stale test expectations corrected (all exposed once the project compiled — none were real arch violations)

1. `Compliance_5_1_DigitalSignatureController_Has2Endpoints` → `…HasSignAndVerifyEndpoints`. The controller now has 5 endpoints (`Sign`, `Verify`, `VerifyXml`, `VerifyJws`, `Confirm`) — `VerifyXml`/`VerifyJws`/`Confirm` were added by the 3-tier signing ship. Those are extra capabilities, not violations; dropped the `HaveCount(2)` pin, kept `Contain("Sign")` + `Contain("Verify")` per QĐ 742 §5.1.
2. `CallbackAuthArchTests` `*CallbackController` discovery filter `t.Name.EndsWith("CallbackController")` matched zero types — the real controllers are `MysignCallbackAuthController` + `MysignCallbackResultController` (`src/host/AqtCoreFW.Api/Controllers/Signing/`). Changed to `t.Name.Contains("Callback") && t.Name.EndsWith("Controller")`. This also un-vacuums `No_Callback_Controller_Has_HttpGet_Method` (was passing only because it found nothing). Both real controllers verified `[AllowAnonymous]` + POST-only → both tests now genuinely pass.
3. `MysignInboundToken_Length_Guarantee` → `…IssueContract_IsPreserved`. There is no synchronous `IssueToken()`; the service exposes `Task<string> IssueTokenAsync(CancellationToken = default)`. Updated the contract assertion accordingly.

Code-review: 9.2/10 APPROVE, 0 critical/high, 3 Low (loose `Contains("Callback")` filter; QĐ 742 §5.1 guard not weakened in substance; weak-but-faithful `IsAbstract.Should().BeFalse()`). Report: `plans/reports/code-reviewer-260512-0905-mts9-arch-test-fixes.md`.

## Decision / lesson

**The "Architecture 87/87 PASS" recorded in the signing-core and QD742-Phase-05b ship summaries was inaccurate** — the suite had never compiled after W7, and the real test count is **94** (W7 added the Signing + Compliance.Phase05b tests). Subagent-wave ship reports' test counts can be unverified; re-run the suite yourself before trusting "N/N PASS" in a ship summary. Memory updated.

## Follow-ups

- MTS-9-FU1 (P3, ~10m): namespace-scope the `Contains("Callback")` controller sweep to `*.Controllers.Signing.*` so a future non-webhook `*Callback*Controller` isn't wrongly subjected to the AllowAnonymous/POST-only guards.
- MTS-10 (P2): Workflow.Integration at 58% pass — still open (3 buckets, pre-existing, untriaged).
