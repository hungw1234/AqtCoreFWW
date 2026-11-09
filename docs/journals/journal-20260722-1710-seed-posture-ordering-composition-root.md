# Journal 2026-07-22 17:10 — P1-STABLE-SEED-POSTURE: the "2-restart converge" was a registration-order bug, and the first fix re-created the bug class one seeder over

**Slice:** backlog row `P1-STABLE-SEED-POSTURE` (filed 07-16, P1 readiness audit items #8+#13). Branch `fix/seed-posture-seeder-ordering`. Plan `plans/260722-1610-p1-stable-seed-posture/`.

## What shipped

- 6-seeder cross-module cluster (`TenantSeeder → TenantBackfillSeeder → TenantRoleSeeder → OrgTreeSeeder → TenantUserSeeder → TenantAssignmentSeeder`) host-registered in explicit dependency order in `Program.cs`; module registrations keep only the `Configure<>` options bindings.
- `SeederStartupOrderGuardTests` (Architecture.Tests): Program.cs order text-parse + real-registration module-local guard; mutation-verified.
- `SeedPasswords` plaintext deleted from `appsettings.Development.json` (identical code fallback, one reader, dev-gated).
- Runbook §2-restart marked FIXED; dependency-map §5b records registration site + demo-seeder no-FK boot assumption.

Gates: Architecture 187/187, Identity.Infra 339/339, Organization 79/79, Smoke 11/11 (3m56s), build 0E.

## Decisions

1. **Composition-root ordering, not a framework** (user-ratified narrow scope): only 3 real dependency edges existed among 16 seeders; a generic ordering mechanism was YAGNI. Precedent already in-repo: `DemoBusinessProcessSeeder` host-resident for the identical reason.
2. **Delete config key, keep code fallback** (user-ratified): the checked-in `DevAdmin@12345` was byte-identical to `TenantUserSeeder.cs:42`'s fallback — the key was redundant, not a secret prod could reach (`IsDevelopment()` gate + no prod section).
3. **`GetValue<bool>("X:Enabled")` over `.Get<Options>()`** at the host gate — equivalent semantics for the gate itself; malformed sibling properties now fail at StartAsync instead of registration (still boot-abort). Accepted by review.
4. **reset-all.ps1 restart branch kept as safety net** — if `$MK_ABORT_NOUSERS` ever fires again it means an ordering regression, and the arch-test should have gone red first. Cheap insurance, self-documenting.

## Lessons

1. **The fix for a cross-module ordering bug re-created the same bug class one seeder over.** First cut moved 5 conditional seeders to the host but left the unconditional `TenantBackfillSeeder` module-registered — which silently inverted the *documented* "canonical TenantSeeder beats backfill's TENANT-xxxx rows" order (review H1). When relocating registrations out of a sequence, every sibling left behind changes its RELATIVE position too — enumerate the full ordered set, not just the members being moved. Same shape as `feedback-fixing-a-bug-can-arm-a-dormant-hole`, but for ordering: fixing edge A→B can invert edge C→A.
2. **A test fixture's comments are load-bearing claims about behavior you may be about to change.** `SeederSmokeFixture` documented "user seeder runs first, finds no OrgUnits, skips — fast, inside the 90s budget" — the fix invalidated exactly that premise, changing the smoke's workload from skip-fast to full-converge (review H2). Green suites listed in the verify line are only the suites you thought to run; grep the CHANGED BEHAVIOR's name across `tests/**` comments to find suites whose premises you invalidated.
3. **Audit-at-slice-start again paid its 5 minutes** (6th instance): the row said "demo-seeder race" — scout proved the demo half a non-issue (fail-loud / self-contained) and found the REAL inversion in the tenant chain instead, cutting scope M→S and pointing the fix at the right file.

## Unresolved

- Prod seed posture (readiness report §4-Q2) — user-owned, unchanged by this slice.
- Runbook still carries pre-existing `BtcIdentitySeeder` name drift (older rename) — separate hygiene item, not filed (trivial).
