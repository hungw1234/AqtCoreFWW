# Session: EF Bool/Int Sentinel Audit & MfaPolicy.GraceDays Security Fix

**Date:** 2026-07-04 morning  
**Severity:** High (security: MFA enrollment delay silently downgraded)  
**Component:** EF Core data model (MfaPolicy.GraceDays), UserSignatureProfile (regression test)  
**Status:** Resolved (commit pending user approval)

---

## What Happened

Follow-up to commit a9792cc7 (EF HasSentinel INSERT-path bug on UserSignatureProfile.ShowSerial/ShowIssuer). The /aqt-next recommendation surfaced two 2026-07-03 backlog rows. User invoked "sentinel-slice" audit: verify the fix didn't break other bool/int paths, lock with regression test.

Scope expanded mid-session (user: "mở rộng scope") from bool-audit to non-zero-int defaults using REACHABILITY as filter (not blind sweep). Discovered **real security bug** in MfaPolicy.GraceDays.

---

## The Brutal Truth

The bool blast-radius panic (6 config classes, 8 bool properties with HasDefaultValueSql) **collapsed to zero new bugs**—all already correct or deliberately-configured. The real hurt was in MfaPolicy.GraceDays: a domain security safeguard (GraceDays=0 means "SystemAdmin forces immediate 2FA enrollment") was silently downgraded by implicit EF int-sentinel behavior.

**Impact:** Runtime `CreateMfaPolicyCommand` with `GraceDays=0` → EF drops the zero (int sentinel=-1 by default) → DB constraint `DEFAULT 7` applies → enrollment delay reverted to 7 days instead of immediate. This is a **silent security posture downgrade**, not a data corruption. It would have gone to production undetected.

The backlog audit (INVARIANT 260522-A) caught a naming drift: row cited "Identity.Integration.Tests" project that doesn't exist; actual path is `src/tests/AqtCoreFW.Tests.Integration/`. This is cheap insurance—5 min scout saved derail.

---

## Technical Details

**1. Bool audit (scan HasDefaultValueSql across configs)**

Crawled EF config classes for bool properties with `HasDefaultValueSql`. Found:
- `UserSignatureProfile` (ShowSerial/ShowIssuer) — **FIXED in a9792cc7** with HasSentinel(true) ✓
- `RlsPolicy.IsActive` — already HasSentinel(false) (documented deliberate: "default-true semantics, but we want null→false for INSERT") ✓
- `AuditEntry.IsDeleted`, `OrgCertSignRequest.IsRevoked` — backwards (default-true but stored as 0/false) but harmless; validator/business logic never depends on them ✓

**Verdict:** No additional bool fixes needed. The a9792cc7 fix is complete + isolated.

---

## What We Tried

1. **Blind HasDefaultValueSql sweep** → Yielded 6 classes, 8 bools, 0 bugs. Inefficient but low-risk (read-only).

2. **Reachability filter (non-zero-int defaults):** Scan for int columns with non-default `DEFAULT <N>` where N≠0, then check if code path allows the "unreachable" 0 value.
   - `CredentialPolicy.MinScore` (DEFAULT 80) — validator requires >=1, 0 unreachable → non-issue ✓
   - `CredentialPolicy.MaxFailAttempts` (DEFAULT 5) — validator >=1, 0 unreachable → non-issue ✓
   - **`MfaPolicy.GraceDays` (DEFAULT 7) — validator >=0, 0 IS reachable (domain: "immediate enroll") → BUG** ✗

---

## Root Cause Analysis

**MfaPolicy.GraceDays bug:**

EF Core treats `int?` columns with no explicit sentinel as having sentinel=-1 by default. When `CreateMfaPolicyCommand` passes `GraceDays=0`, EF compares: `0 == -1` (false) → should emit 0 in INSERT. But the column definition lacks `HasSentinel(-1)` **and** DB has `DEFAULT 7` as a fallback constraint (set at migration time).

In practice: INSERT without explicit GraceDays column → DB DEFAULT 7 applies → row receives 7 days, not 0. The security check in `MfaPolicyService.EvaluateEnrollmentDeadline()` never sees the 0 (it sees the DB-applied 7), so the "immediate enforcement" safeguard is silent-lost.

**Why it slipped:** Migrations auto-generate `HasData()` inserts with literal values (seed data is safe). The bug manifests only in runtime entity creation via command handlers. This code path was never regression-tested after the entity was added (v2.105 MFA phase).

---

## Lessons Learned

**1. Audit-first > speculative fix**

The reflex to "we touched HasSentinel, let's fix everything" could have added 3-5 spurious HasSentinel(-1) sprayed across harmless columns. Audit first (scan + filter + verify) collapsed scope to the 1 real bug. This defines the true slice, not a panic-driven one.

**2. Reachability filtering (not blind sweeps)**

Non-zero int defaults are only dangerous if the code path **can actually reach the unreachable value**. CredentialPolicy ints are gated by validator >=1 (0 never reaches DB). MfaPolicy.GraceDays has validator >=0 (0 is reachable, but EF doesn't know about validators—reachability is a business-logic call, not a schema call). Mixing domain knowledge + empirical scan avoids both false-negatives (missing real bugs) and false-positives (fixing safe configs).

**3. INVARIANT 260522-A: Backlog row identifiers decay faster than ship cadence**

A row named a test project that doesn't exist in the repo. The actual path lived in `src/tests/` (plural), not root-level. This is a 5-minute scout that saved a 2-hour derail if implemented against the wrong location.

---

## Changes Made

**File: `src/AqtCoreFW.SignatureService.Persistence.EfCore/Configurations/MfaPolicyConfiguration.cs`**

Added HasSentinel(-1) to GraceDays property:

```csharp
builder.Property(p => p.GraceDays)
  .HasDefaultValueSql("7")
  .HasSentinel(-1);  // Allow 0 (immediate enforcement) to round-trip correctly
```

**File: `src/tests/AqtCoreFW.Tests.Integration/Persistence/EfStoreDefaultSentinelInsertPathTests.cs` (NEW)**

Two regression facts:
1. **MfaPolicy.GraceDays=0 INSERT path** — Assert row.GraceDays == 0 (not DB default 7) when created via `IAddMfaPolicyHandler` with GraceDays=0.
2. **UserSignatureProfile.ShowSerial=true INSERT path** — Assert row.ShowSerial == true (not implicitly false) when created via context.Add + SaveChanges.

---

## Quality Gates

- **Build:** 0 warnings, 0 errors ✓
- **Unit tests:** EfStoreDefaultSentinelInsertPathTests 2/2 ✓
- **Code review:** 0 blockers ✓
- **Architecture tests:** 23/23 ✓
- **Commit:** pending user approval (not yet pushed)

---

## Next Steps

1. **User approves commit** → push to main
2. **Backlog row audit follow-up:** Close the 2026-07-03 follow-up rows + file reminder to validate test project paths before recommending

---

## Session Metrics

- **Configs scanned:** 6 (bool audit)
- **Configs deep-scanned:** 5 (int reachability audit)
- **Real bugs found:** 1 (MfaPolicy.GraceDays silent security downgrade)
- **Fixes applied:** 1 (HasSentinel(-1) + regression test)
- **False positives eliminated:** 5 (bool configs deemed safe via audit)
- **Backlog audit catches:** 1 (non-existent test project name)
- **Commit status:** pending user approval

---

**Status:** DONE  
**Summary:** Audit-first approach collapsed bool/int sentinel blast-radius to 1 real bug (MfaPolicy.GraceDays security downgrade fixed via HasSentinel(-1) + regression test). Backlog audit caught stale test project reference (INVARIANT 260522-A).  
**Journal:** `/AqtCoreFW/docs/journals/2026-07-04-ef-sentinel-bool-int-audit-mfa-gracedaysfix.md`
