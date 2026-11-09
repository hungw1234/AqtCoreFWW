# T3-MENU-SEC Seeder Gap Closure

**Date**: 2026-05-22 18:00 ICT  
**Severity**: High  
**Component**: Menu seeding infrastructure  
**Status**: Resolved (v2.137p97)

## What Shipped

T3-MENU-SEC closure v2.137p97 (commits `4ab1f6dd` + `c6bd1ee5`, direct-to-main):

1. **Added SECURITY menu group + 11 children** to `E2ETestDataSeeder.Menus.cs`:
   - 1 top-level SECURITY container (gates `IP_RULE.VIEW`)
   - 4 direct leaves: ip-whitelist, audit-policy, backup-policy, signature-config
   - 1 security-health sub-container + 6 health leaves
   - Mirrors FE `getSecurityMenuChildren()` (admin-menu-entries.ts:199-242)

2. **7 permission wires**: SECURITY group + 4 leaf perms (SECURITY.IP_RULE.VIEW / AUDIT.RETENTION.VIEW / SYSTEM.CONFIG.MANAGE / SECURITY.SIGNATURE_CONFIG.VIEW) + health-sub cascade + MFA leaf (folded under user gate: same bug-class as T3-MENU-SEC, no new decision gating).

3. **Removed "SECURITY" from `NoMenuModulePrefixesAllowlist`** in `ModuleStartupWiringTests.cs` — closes root cause; now enforced by `EveryModuleWith_ViewPermissions_Should_HaveAppMenuEntry` invariant.

## Why This Mattered

Backlog row T3-MENU-SEC (filed 2026-05-17) claimed "SECURITY has 3 VIEW perms" — actual scout at 2026-05-22 found 4 (MFA_POLICY/IP_RULE/HEALTH/SIGNATURE_CONFIG). **META-L 260522-A invariant applied:** re-grep carry-fwd lists at slice-start caught perm-set drift between row-file and execution. Without re-scout, would have shipped incomplete seeder.

Also fixed **bug-class sibling MFA_POLICY.VIEW** — perm exists (v2.137p52 BP defect closure), FE wires it under Identity group, seeder never had it. Folded same-session under user ratification (4-A-B pattern: <10min + same bug-class + no new gating).

## The Technical Reality

Seeding gaps are invisible until **Authorize attribute enforcement** hits prod: user clicks menu → 403. Happens because FE and BE seeder drift asynchronously. Build 0E/0W (6 pre-existing Scriban NU1903 unrelated); Architecture 101/101; ModuleStartupWiringTests 2/2. Code-review APPROVE 9.4/10 — 0 Crit/High/Med. Carry-fwd: L2 (product-policy SECURITY group scope clarification) + L3 (doc ordinal comments housekeeping).

## NEW META-L

**(260522-K)** Perm-set drift between backlog row and execution — INVARIANT: enumerate perms → re-grep canonical PermCodes.All FIRST, don't trust row enumeration.

**(260522-L)** Subagent file-path hallucination in changelog sections — agent extrapolated from convention not git diff. INVARIANT: subagent prompts require explicit git diff read before writing "Files Changed".

**(260522-M)** Fatigue-mode override stays clean at minimal-mechanical scope when gates are dense (<1h slices + build/arch/review/docs gates pre-define quality).

## Carry-Forward

L2 + L3 filed to backlog. Architecture invariant verified. Ready for prod.

**Cycle**: ~50min (impl + test + review + finalize). 23 ships today before this. Fatigue floor breached again but quality preserved via scope + gating discipline.
