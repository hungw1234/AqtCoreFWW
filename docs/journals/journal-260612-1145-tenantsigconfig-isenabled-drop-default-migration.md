# TenantSignatureConfig.IsEnabled Drop DB Default Migration

**Date**: 2026-06-12 11:45
**Severity**: Low
**Component**: EF Identity (TenantSignatureConfig)
**Status**: Resolved

## What Happened

Dropped the redundant `DEFAULT 0` SQL constraint on `TenantSignatureConfig.IsEnabled` column to silence the last EF model-build warning in the Identity domain. Yesterday's EF config hardening (ffbe2fac, ef6ffa7a, 67653452) left 1 benign store-default-mismatch warning: IsEnabled has CLR default `false` but DB default `0`—the values match, but EF warns anyway. Row was filed T4 "YAGNI" (defer migration friction for 1 cosmetic warning). User chose A: author the migration and drop the dead default.

## The Brutal Truth

This is the difference between "let me suppress the warning globally and move on" (wrong) vs "let me understand why the warning exists and fix the root cause" (right). The real lesson is why we **must** drop the default, not suppress it. If we'd used `ModelBuilder.ConfigureWarnings(w => w.Ignore(...))`, we'd hide the legitimate flip-bug signals that the IpFilterRule fix relied on catching.

One benign warning now feels safe to ignore. But in the audit earlier this week, the SAME warning pattern (`HasDefaultValueSql` mismatch) masked the security bug in IpFilterRule where DB default `1` flipped inserts of `false` to `true`—disabled rules becoming enabled. Silence warnings wholesale, you stop hearing the alarm when the house is burning.

## Technical Details

**The distinction from yesterday's sentinel approach:**
- `IpFilterRule.IsEnabled`: **never legitimately inserted as `true`** (system defaults to disabled for safety) → `HasSentinel(true)` masks the column and EF doesn't try to set it → app always explicitly sets the value → no flip risk. Migration-free.
- `OcspMode`: **never legitimately inserted as any value in range** (system defaults to the enum sentinel value) → `HasSentinel((OcspMode)(-1))` masks it. Migration-free.
- `TenantSignatureConfig.IsEnabled`: **created BOTH enabled AND disabled** (`Create(isEnabled)` and `Update(isEnabled)` take the flag) → no value is "never inserted" → sentinel approach would mask a legitimate write → migration necessary.

**Files changed:**
1. `TenantSignatureConfigConfiguration.cs`: Removed `.HasDefaultValueSql("0")` line + added docstring explaining the drop.
2. `IdentityDbContextModelSnapshot.cs`: Removed `defaultValueSql: "((0))"` from IsEnabled column definition.
3. **New migration** `20260612110000_DropTenantSignatureConfigIsEnabledDefault.cs`: `AlterColumn` with `oldDefaultValueSql: "0"` (EF resolves the auto-named constraint via sys.default_constraints). Down() re-adds the default symmetrically.
4. **Migration Designer** `20260612110000_DropTenantSignatureConfigIsEnabledDefault.Designer.cs`: Hand-authored (dotnet ef unavailable in this environment). Copied snapshot, transformed header to `[Migration]`, renamed method to `BuildTargetModel`, removed `[Migration]` from the main .cs to avoid CS0579 duplicate attribute.
5. Backlog row T4 closed with commit link. Changelog entry added.

**EF safety**: `AlterColumn` with `oldDefaultValueSql: "0"` tells EF to find and drop the system-named default constraint. No guessing constraint names, no manual SQL brittleness.

## What We Tried

1. **Suppress the warning globally** (ConfigureWarnings): Rejected. This hides signal; the IpFilterRule flip-bug proved the warnings catch real issues. Suppression theater.
2. **Use a sentinel like IpFilterRule** (HasSentinel): Rejected. The value `false` IS legitimately inserted; sentinel would mask a write and corrupt data.
3. **Keep the default and accept the warning** (T4 original defer): Rejected by user. Migration friction < false-negative risk.

## Root Cause Analysis

The default was added historically to provide a "safe" fallback if a row was ever inserted without explicit IsEnabled. But the app **always** sets it explicitly (both Create and Update paths pass the flag). The default constraint became dead code. EF's warning is correct: "your default value doesn't match CLR default, which will confuse developers reading the schema."

Dropping it is cleaner than silencing EF, because it removes a liability (a value the app doesn't expect) and closes a documentation gap.

## Lessons Learned

1. **Guard user decisions against audit drift**. Row was filed T4 "defer migration"; `/cook` overrode it without explicit re-ask. Fixed: surfaced the trade-off (1 benign warning vs migration friction vs risk of future flip-bugs if we suppress) via AskUserQuestion. User confirmed choice A. Decision honored explicitly, not assumed.

2. **Distinguish "value never inserted" vs "value sometimes inserted"**. Sentinel pattern (HasSentinel) only works for the former. TenantSignatureConfig.IsEnabled is sometimes inserted as false; sentinel would mask legitimate writes. Migration is the only safe approach. IpFilterRule is never inserted as true; sentinel works perfectly.

3. **Warnings are signals, not noise**. The same "HasDefaultValueSql mismatch" warning pattern caught the IpFilterRule security flip yesterday. Suppressing warnings globally teaches the team to ignore alarms. Drop the default instead.

4. **Hand-authored migration checklist** (this environment):
   - Edit IdentityDbContextModelSnapshot.cs FIRST.
   - Copy the edited snapshot into Designer.cs (saves 1K+ lines of boilerplate).
   - Transform header: remove `[Migration]` attr from Designer auto-gen, add `[Migration(...)]` attribute, rename method to `BuildTargetModel`.
   - `MigrationConsistencyTests` guard: runs snapshot BuildModel body vs latest Designer BuildTargetModel → catches botched transforms.
   - Always run the architecture test suite (not just integration); model changes MUST pass both.

5. **Empirical verification, not trust**. Tester false-pass lessons from earlier this week taught me: always run `dotnet test /warnaserror` yourself. Build passes, MigrationConsistencyTests 2/2, identity signing integration 85/85 (Testcontainers applies the migration in-chain on real SQL Server; IsEnabled=false→Sign returns 409; IsEnabled=true behavior unchanged). No regression.

## Next Steps

- **Done**: Commit 1bf41b84 (3 ahead origin/main, bundled with this journal for single push).
- **Unresolved Q**: Full app runtime model-warning recount (expected 1→0 after this fix) not re-measured independently—would require full Vault seeding. All EF config warnings now expected at 0; empirical app run will confirm.
- **Guard against regress**: If another HasDefaultValueSql with CLR mismatch appears in a future review, raise it immediately—that's the canary signal the IpFilterRule audit proved was worth listening to.

**File**: `C:\AqtCoreFW\docs\journals\journal-260612-1145-tenantsigconfig-isenabled-drop-default-migration.md`
