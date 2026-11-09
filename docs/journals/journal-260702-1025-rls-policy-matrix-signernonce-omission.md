# RLS Policy Matrix Missing SignerEnrollmentNonces Entry

**Date**: 2026-07-02 10:25  
**Severity**: Medium  
**Component**: RLS / TenantIsolation.Tests  
**Status**: Resolved

## What Happened

`RlsPolicyPresenceTests.RlsPolicyCount_Equals_Expected` was RED since 2026-06-25: empirically found 50 policies, expected 49. Migration `20260625033442_AddSignerEnrollmentNonce` created `pol_identity_SignerEnrollmentNonces_Tenant` RLS policy but the test's `ExpectedPolicies` matrix (49 entries) was never updated.

## Technical Details

- Missing entry: `identity.SignerEnrollmentNonces` (row-level security policy on new enrollment-nonce table)
- Test file: `tests/integration/AqtCoreFW.TenantIsolation.Tests/RlsPolicyPresenceTests.cs`
- Fix: added matrix entry + bumped counts: 49→50 total, identity 9→10
- No production/migration/RLS logic changed; test-only

## Root Cause

Migration shipped without corresponding test-matrix update. Test-matrix maintenance burden shifted entirely to manual discipline after CI parked (since April).

## Lessons

**CRITICAL:** Migrations adding RLS policies MUST update `RlsPolicyPresenceTests.ExpectedPolicies` in the same change. RLS-coverage assertions only work when kept in sync at commit time. Parked CI masks this slip.

**REINFORCED:** Manual discipline on structural tests (RLS, dependency contracts, entity tiers) is fragile when automated gates are offline. Budget enforced-touch points into migrations.

## Next Steps

- Re-enable CI to catch matrix drifts automatically (owner: infrastructure)
- Document RLS-policy + test-matrix sync rule in code-standards.md

---

**Status**: RESOLVED (test suite 2/2 green, code-reviewer APPROVE, no findings)
