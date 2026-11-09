# Journal — Sóng-0 Phase-3: PermCodes Vocabulary-Ownership Split + Contract-Test

**Date:** 2026-07-08 · **Plan:** `260706-1237-song0-foundation` (Phase 3 → Done; **plan CLOSED**, all 3 phases) · **Branch:** main · **Status:** committed-pending (not yet pushed)

## What & why

Last remaining phase of the microservices Sóng-0 Foundation. Goal: loosen the heaviest authz coupling before the S1-hub split + harden against fail-closed permission-code drift. Split the 590-line `PermCodes` monolith static class into **21 per-module partial-class files** (`PermCodes.<Module>.cs`) + add 3 architecture-test guards. Runtime authz behavior unchanged by construction (pure `const` relocation).

## Key decisions

- **Scope fork A-full over A-lite/B (user-chosen).** Option B (per-module *assembly* ownership, `FormsPermCodes` in Forms.Presentation) is the only choice that removes compile-time coupling — but it costs 316 call-site churn + a manifest-registration mechanism, and there is no extraction host yet → **premature (YAGNI), deferred to wave S1-hub**. A-full (partial-class, same namespace) gives a file-boundary ownership seam at **0 call-site churn** and aligns with the project's own >200-LOC modularization rule.
- **Catalog-source open-Q resolved = `PermCodes.All`** (not the seeder/DB directly). The pre-existing `PermCodesSeederCoverageTests` already binds `All ↔ seeded` bidirectionally, so `checked ⊆ All ⟹ checked ⊆ grantable` — DRY, pure public-reflection, no host-internal access.
- **Guards-first implementation order.** Wrote + baseline-green the 3 guards on the *single-file* PermCodes BEFORE the split, then re-ran them after the split — turning the guards into a safety-net that proves the mechanical relocation preserved all 130 consts.

## The 3 guards (each catches a distinct drift-class, none redundant)

- **(d) `PermCodesAllDriftTests`** — declared consts == `_all`; no duplicate values. Closes the real gap the seeder-coverage test misses: a const added to a nested class but forgotten in `_all` is neither policy-registered nor seeded → fail-closed 500, invisible (the existing test only iterates `_all`). More important now that consts span 21 files.
- **(b) `PermCodeOwnershipConventionTests`** — each top-level module-class declares codes sharing one first-dot-segment. The reflection form of "module A must not define B's codes" (NetArchTest is assembly/type-scoped, can't do file boundaries). Protects the split's ownership invariant.
- **(c) `PermCodeCheckedGrantableContractTests`** — every controller `[Authorize(Policy=x)]` (class+method, all 17 host assemblies) that is PermCode-shaped ⊆ `PermCodes.All`. The only guard that catches a raw-string `[Authorize("FORMS.SUBMIT.TYPO")]` bypassing the typed constants. Anti-false-green self-tests: `Scanner_Actually_Sees_Real_PermCode_Policies` (non-empty sweep) + `Scanner_Flags_A_Synthetic_NonGrantable_Code` (fabricated fake would be flagged).

## Verification

- Arch-suite **158/158** · `PermCodesSeederCoverageTests` **2/2** · Identity unit **938/938** (App 426 + Domain 173 + Infra 339) · build warnaserror **clean**.
- code-review **APPROVE** — proved 130/130 values byte-identical vs HEAD three independent ways (`_all` array relocated verbatim). 2 LOW notes: #1 guard-(c) skipped inherited action methods on abstract-base controllers (0 current cases) → **fixed inline** (dropped `DeclaredOnly`); #2 `KnownNonPermCodePolicies` exclusion is dead-but-intentional documentation → kept.

## Lessons

- **Audit-before-scope (INVARIANT 260522-A) paid off again.** The plan claimed a "checked ⊆ grantable" contract-test as new value; scouting the actual authz wiring (policies bulk-registered from `PermCodes.All`, 0 raw-string call-sites, existing bidirectional seeder test) revealed the literal controller-scan is mostly redundant — the *distinct* value is the `_all`-drift guard (d). Reframed the 3 guards so each closes a different drift site instead of shipping the plan's original framing verbatim.
- **Partial-class split ≠ decoupling.** Worth stating plainly: relocating consts between partial files of the same type in the same assembly changes 0 compile-time dependencies. It buys navigability + an ownership seam, not extraction-readiness. Honest framing kept the user's YAGNI expectations correct (real decoupling = Option B, later).

## Follow-ups

- Guard (c) latent coverage: policy-bearing action methods on an abstract base controller — now scanned after the inline fix; 0 cases today regardless.
- Sóng-0 Foundation plan closed. Next microservices work = extraction waves S1+ (hub-split, per-service assembly ownership) — out of scope until a host exists.
