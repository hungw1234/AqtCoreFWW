# Journal — Data-driven startup-migrate sets + ADR-010 subset guard

**Date:** 2026-06-21 21:47 ICT
**Trigger:** `/cook ARCH-AUTHSERVER-MIGRATE-SUBSET-GUARD` (after `/aqt-next`)
**Type:** BE arch governance — composition-root refactor + architecture test

## What & why

Closed the deferred follow-up from P4/ADR-010: make the invariant *"AuthServer's startup-migrated
DbContext set MUST stay a subset of Api's"* machine-checkable. The row explicitly warned that the
naive approach — text-scanning both `Program.cs` for `MigrateAsync` calls — is brittle, and asked
for a non-brittle approach (a shared migrate-set the hosts consume).

## Scope decision (surfaced before coding)

INVARIANT 260522-A probe found two things worth pinning before touching code, surfaced to the user:
1. The row is a **conditional defer** ("revisit *if the divergence risk materializes*") — and the
   subset still holds, so the trigger had not fired.
2. The only non-brittle implementation **requires a production refactor** of both composition roots
   (introduce a shared declaration the hosts consume), which carries the P4 login-canary risk —
   not the "pure arch-test, 0.5d" I'd labelled it at `aqt-next` (I corrected that estimate).

User chose the full approach.

## Design

A single shared constant listing all 20 contexts can't live anywhere AuthServer references without
forcing AuthServer to depend on all 20 module Infrastructure assemblies — which would defeat its
intentional minimalism (the whole point of ADR-010). So each host declares **its own** set:
- `AqtCoreFW.Api.StartupMigrationSets.Contexts` — 20 contexts, verbatim original order.
- `AqtCoreFW.AuthServer.StartupMigrationSets.Contexts` — Identity + Messaging (strict subset).

Both `Program.cs` migration blocks switched from inline `GetRequiredService<X>().Database.MigrateAsync()`
to `foreach (var t in StartupMigrationSets.Contexts) await ((DbContext)sp.GetRequiredService(t)).Database.MigrateAsync();`
— order preserved. The arch-test project gained a ProjectReference to the AuthServer host (it already
referenced Api), so `MigrateSetSubsetGuardTests` can read both declared sets and assert the subset.

## The rewire nobody planned for (but should have)

Adding the AuthServer reference + the foreach refactor turned the **full** arch suite red on ONE
pre-existing test: `ModuleStartupWiringTests.EveryRegisteredDbContext_Should_HaveMigrateAsyncCall_InProgramCs`.
It text-scanned `Program.cs` for `MigrateAsync` literals — and the refactor removed exactly those
literals. This was the *same brittleness* the backlog row named, manifesting in a sibling guard.

Fixed it the right way: rewired `ParseMigratedDbContexts()` to read `StartupMigrationSets.Contexts`
by reflection (renamed `…_Should_BeInApiStartupMigrateSet`, deleted the dead regex). The guard's
intent — every module-registered DbContext must be migrated at boot (v2.137p51 incident class) — is
preserved, and it can no longer drift from runtime behavior the way a regex can.

Catching this only at the **full-suite** run (not the filtered new-test run) is the lesson: a filtered
green is not a suite green when you've changed a shared reference graph.

## Verification

- arch suite 116→**120** green (4 new + rewired guard).
- Startup.SmokeTests 18/18 — the Api host actually boots and runs the new foreach-migration path E2E.
- Identity.Integration login canary **297/297** — the project's canonical proof that a
  composition-root / migration-order change didn't break login (order preserved).
- Red-before-green proven 2×: remove Identity from Api set → subset test RED (others green);
  remove Forms from Api set → rewired coverage guard RED. Both reverts clean (`src/` diff has no
  stray markers).
- Review APPROVE_WITH_NITS — reviewer verified the 20-context list is a 1:1 of HEAD's inline calls,
  behavior (gating/scope/order/exception) is preserved, tests have real teeth. NIT-1 (a stale
  `<see cref>` to the renamed method) fixed.

## Lessons

- **A "non-brittle test" that requires a production refactor is a bigger task than a test.** The
  honest move was to correct my own `aqt-next` effort/risk label and let the user own the
  composition-root-touch decision, not to silently widen scope.
- **Changing a test project's reference graph can break unrelated existing tests** — run the FULL
  suite, not just the new file, before declaring green.
- **Brittleness compounds:** the row flagged one brittle text-scan; fixing it properly surfaced a
  second one in a sibling guard. Both now read the single declared source of truth.

## Follow-ups

None filed. The declared sets are now the single source of truth for both runtime migration and the
two guards; future module additions are caught by `ModuleStartupWiringTests` (registered ⊆ migrated)
and the ADR-010 subset by `MigrateSetSubsetGuardTests`.
