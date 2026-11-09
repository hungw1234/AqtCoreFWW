# Journal — Alerting dev-seed seam

**Date:** 2026-07-04 (~15:10) · **Branch:** main · Follow-up to the signing provider-allowlist guard (`7ead92ba`).

## What

Added `AlertRuleDevSeeder` (`src/shared/AqtCoreFW.Infrastructure/Alerting/`) — a startup `IHostedService` that idempotently seeds the signing-allowlist deny-rate `AlertRule` in a **DISABLED** state, gated by config `Alerting:DevSeed:Enabled` (default OFF). Upgrades the prior "ops-doc only" alerting delivery (the seam didn't exist when the guard shipped) to a real seed seam. Registered in `AlertingRegistration.AddAlertingInfrastructure`.

## Decisions

- **Guard = config flag** (`Alerting:DevSeed:Enabled`, user-chosen) over `IsDevelopment()` / env-var. Ops can toggle per-env without a dev build; default OFF preserves "no prod auto-seed".
- **Seed DISABLED** via `AlertRule.Create(...).Disable(Guid.Empty)` — the evaluator loads only `.Where(r => r.Enabled)`, so a disabled rule is present-for-ops but silent (no per-minute noise when Prometheus is absent). Seed-actor `Guid.Empty` mirrors `DemoWorkflowSeeder`.
- **Idempotent skip-by-Name**; the seed only ever CREATEs — never re-enables/overwrites an ops-tuned rule. The `DevRules` list is the extension seam.
- Must implement `IIntegrationTestSeeder` — an arch test (`SeederMarkerContractTests`) fails any `*Seeder : IHostedService` that lacks the marker (else `WebAppFixture` silently strips it).

## Friction / lessons

- **InMemory test trap:** `UseInMemoryDatabase($"...{Guid.NewGuid()}")` evaluates the lambda per DbContext instantiation → each scope got a different store → seeder wrote one DB, the assertion read another (3 red). Fix: capture the DB name ONCE per provider.
- **Code-review caught a phantom test:** the original "does-not-re-enable" test seeded twice and asserted `Enabled=false` — a tautology (the rule was never enabled between runs). Reworked to real teeth: seed → simulate ops `.Enable()` → seed again → assert `Enabled` stays true.
- **Multi-replica caveat:** check-then-insert isn't atomic across replicas; runbook softened to "local dev; single-instance for staging/prod, else create via admin API".

## Verification

Build 0W/0E. Tests: seeder unit 5/5 (InMemory EF), `SeederMarkerContractTests` 2/2, `Infrastructure.Extensions.Tests` 148/149 (1 pre-existing skip), `SeederSmokeFixture` 18/18 (boot log: "seeded 1 disabled dev AlertRule(s)"). No regression.
