# ADR-011: Presentation-Layer & Host-Controller Placement Invariant

**Date:** 2026-06-22
**Status:** Accepted
**Deciders:** AqtCoreFW Architecture Team

## Context

An architecture audit of every HTTP surface found the Clean-Architecture dependency rule broken in
the host and two module Presentation layers:

1. **Two signing controllers in `AqtCoreFW.Api` inject `IdentityDbContext` and read/write EF
   directly** (`MysignCallbackResultController`, `FileUploadHandlerController`). They live in the host
   only because the host is the one assembly that references `Identity.Infrastructure` (where the
   DbContext lives). `FileUploadHandlerController` additionally opens a manual connection to run
   `sp_set_session_context N'TenantId'` — Row-Level-Security tenant pinning for an `[AllowAnonymous]`
   machine-to-machine call.
2. Because those controllers skip `ISender.Send`, they bypass the entire MediatR pipeline:
   **zero `AuditBehavior` coverage, zero `ValidationBehavior`, zero domain invariants** on the act of
   signing — the highest-risk write in the system (signing-completion callback + VGCA doc-flow upload,
   QĐ 742 §1.6.b). This undercuts ADR-004 (HMAC-chained audit log).
3. A third (`SigningRequestController`) calls the Application *service* `ISignatureOrchestrator`
   directly, bypassing the audited command pipeline for its one write endpoint.
4. Two module Presentation projects (`MasterData.Presentation`, `SystemParams.Presentation`) carry a
   `ProjectReference` to their own `*.Infrastructure` project, eroding the same rule at the project
   layer.

The root cause is a broken **dependency direction**, not file placement. Fixing the layering
(persistence behind an Application command/handler) dissolves the placement problem and closes the
audit gap; the placement is then made machine-checkable so it cannot silently regress.

## Decision

Three rules, enforced by three reflection/project-scan guards in
`tests/architecture/AqtCoreFW.Architecture.Tests`.

### Rule 1 — Thin controllers, no module Infrastructure (guard G1)

A `ControllerBase` across any controller-hosting assembly (host `AqtCoreFW.Api` + every module
`*.Presentation`) MUST NOT be constructor-coupled to a type in a **module** Infrastructure assembly
(`AqtCoreFW.<Module>.Infrastructure`). Writes dispatch an `IBaseCommand` via `ISender` so the MediatR
pipeline (`AuditBehavior` + `ValidationBehavior` + `TenantAwareBehavior`) applies.

- **Domain repositories are allowed.** The rule targets module Infrastructure, not repositories.
  Injecting a `*.Domain.Repositories` interface for reads is an accepted pattern
  (`ConsentController` injects `IConsentRepository`). The guard checks module Infrastructure coupling
  only — there is no "no repositories" rule.
- **Shared infrastructure is deliberately out of scope.** The shared composition-root assembly
  `AqtCoreFW.Infrastructure` has no owning module and no Presentation layer; host admin controllers
  over it are legitimate residents (see Rule 3). Three of them inject a shared-infra DbContext
  directly (`AlertingAdminController`→`AlertingDbContext`, `BackupAdminController`→`BackupDbContext`,
  `WebhooksAdminController`→`WebhookDbContext`). The guard discriminator is therefore **module
  infrastructure**, not "any DbContext" — a shared-infra DbContext in a host admin controller is
  accepted. (A module DbContext such as `IdentityDbContext` is still caught, because it lives in a
  module `*.Infrastructure` assembly.)
- **Reflection blind spot (documented, not a defect).** G1 inspects constructor injection only. A
  service-locator pull (`HttpContext.RequestServices.GetRequiredService<T>()`) is invisible to it.
  `AccountMfaController` used one for `UserManager`; that gap is closed by refactoring the controller
  to genuine thin + audited command dispatch, not by widening the guard's reflection.

### Rule 2 — Presentation depends only on Application + SharedKernel (guard G2)

No `*.Presentation.csproj` may declare a `<ProjectReference>` to a module Infrastructure project.
Cross-module access goes through Application abstractions. A Domain abstraction that lives in the
source module's own Domain/Application (e.g. `IFileStorageService` injected into an Application
handler) is acceptable.

G2 scans the **project file text**, not runtime `Assembly.GetReferencedAssemblies()`, because the C#
compiler elides a reference whose types are never used — `SystemParams.Presentation` declares a *dead*
Infrastructure reference that a runtime check would not see, yet the declared dependency edge is
exactly what the rule forbids.

### Rule 3 — Host `Api` is a pure composition root (guard G3)

A controller in `AqtCoreFW.Api` whose constructor-injected dependencies belong to exactly one module
(`AqtCoreFW.<Module>.*`, excluding SharedKernel and the shared infra assembly) is a single-module
business controller and belongs in that module's `*.Presentation` layer.

- Footprint is computed from **constructor injection only**. A host controller that injects just
  `ISender` and dispatches a module command is footprint 0 — a legitimate thin host dispatcher, not a
  misplaced single-module controller.
- Footprint 0 (SharedKernel / shared infra only) = legitimate host resident: admin surfaces over
  shared `AqtCoreFW.Infrastructure`, `SecurityController` (SharedKernel-only), `Program`/registration.
- G3 is redundant with G1+G2 for raw correctness but encodes the placement invariant this ADR exists
  to establish, so it stays — paired with positive tests proving each shared-infra `Admin/*` resident
  is NOT flagged (the heuristic does not false-positive the permanent host residents).

## Enforcement mechanism (allowlist shrink)

Each guard ships an explicit allowlist of the known current violators, so the build stays green now
and the allowlist shrinks as each fix lands. Discipline:

- **Empirical seeding.** Allowlists were seeded from a current-tree run of the guards, not an assumed
  list. That run corrected two assumptions: `MasterDataController` trips G1 (not only G2), and the
  three shared-infra admin DbContext injections must be excluded by design (above).
- **Same-commit removal.** Each fix removes its own allowlist entry in the same commit as the
  controller fix, so a reverted fix reverts its allowlist edit atomically and `main` never goes red.
- **Tripwire.** Each allowlist entry must still name a controller/project that still exists AND is
  still a real violation; a fixed/relocated entry left behind fails the tripwire, forcing cleanup.
- **Anti-vacuous.** Each guard asserts a non-empty discovery population so a reflection miss cannot
  pass silently.
- **End-state assertion.** The empty/final allowlist state is asserted once, in the closure test, so a
  future re-addition fails.

## End state — all three allowlists empty

- **G1 allowlist → empty** — no controller is constructor-coupled to a module Infrastructure type.
- **G2 allowlist → empty** — no `*.Presentation` project references a module Infrastructure project.
- **G3 allowlist → empty** — every single-module host controller relocated to its module Presentation.

Each guard asserts its empty end-state (a `Closure_AllowlistIsEmpty` test) so re-introducing an
allowlist entry must be a conscious, reviewed decision. The MasterData read-cache abstraction
(`IAdministrativeUnitService`) was relocated to MasterData.Application in the same remediation — its
Singleton in-memory cache lifetime is preserved (the implementation + registration stay in
Infrastructure), so no follow-up carve-out remains.

## Out of scope (deliberate, not gaps)

- **`SigningDocFlowOrphanBlobSweepJob` → `Identity.Infrastructure.Persistence`.** A background job
  hosted in the composition root may legitimately reference Infrastructure — that is the host's role.
  It is not a controller/Presentation violation; forcing a port here would be gold-plating.
- **`Admin/*` controllers over shared `AqtCoreFW.Infrastructure`** (Alerting / Webhooks / ApiKeys /
  Backup / …). Shared infra has no owning module and no Presentation assembly; the host is the correct
  home. Giving shared infra its own Presentation project is YAGNI.
  (none remaining — the MasterData read-cache abstraction was completed as part of this remediation.)

## Amendment (2026-06-23) — AuthServer host coverage

The OpenIddict auth host `AqtCoreFW.AuthServer` is a second composition root with its own controllers.
It is now swept by **G1** (no controller constructor-coupled to a module Infrastructure type) alongside
`AqtCoreFW.Api` — `AuthServerHost` joined the guard's controller-hosting assembly set. Extending the
guard exposed one real coupling that the blind spot had masked: `ILockoutEscalationService` was defined
in `Identity.Infrastructure.Authentication` and injected into `AuthorizationController`. The interface
was relocated to `Identity.Application.Services` (the implementation stays in Infrastructure), so the
abstraction now lives where the dependency rule expects it — the same move applied to the other
relocated transport-auth interfaces above.

**G3 deliberately does NOT apply to AuthServer.** Rule 3 relocates a single-module host controller into
that module's `*.Presentation`. AuthServer's controllers are OAuth/OIDC protocol endpoints bound to the
OpenIddict host (they `SignIn`/`SignOut` against the auth scheme); they have no module Presentation home
and the auth host IS their terminal residence. The G3 guard therefore stays scoped to `AqtCoreFW.Api`.
Direct use of `UserManager`/`SignInManager`/OpenIddict primitives in these controllers is inherent to
the protocol, not a violation. G2 (no `*.Presentation` project references module Infrastructure) is
unaffected — AuthServer is a host, not a Presentation project, and may reference Infrastructure as a
composition root.

## Consequences

- The signing write endpoints gain audit + validation coverage (ADR-004 honoured on the act of
  signing).
- New controllers are pushed toward thin dispatch by CI; a regression to DbContext-in-controller or a
  Presentation→Infrastructure reference fails the build.
- The shared-infra boundary is explicit: a future maintainer adding a host admin controller over
  shared infra is not blocked, but adding a module-Infrastructure dependency to any controller is.
