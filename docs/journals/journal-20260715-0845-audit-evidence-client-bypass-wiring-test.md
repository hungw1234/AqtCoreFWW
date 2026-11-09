# Journal — Client-Level Admin-Bypass Wiring Proof for InProcessAuditEvidenceClient

**Date:** 2026-07-15 · **Session start:** ~08:30 ICT
**Track:** Microservices transformation (🥇 #1) — monolith `[audit]` read-isolation arc
**Ships:** 1 (test-only hardening) + 1 housekeeping push
**Plan:** none (single-slice, scouted directly per `/aqt-next` recommendation)

---

## What & why

`/aqt-next` surfaced two items: (1) `51602201` (ISO-NONAUDIT prod health-check, T4) sat **unpushed**; (2) the only code-able item left on the `[audit]` read-isolation arc was an *optional* non-`db_owner` integration test on the 2 new `InProcessAuditEvidenceClient` methods. Everything else on the microservices critical path is gated on **FU-A** — a user→auditor action, not code. User chose: push, then write the test.

**The gap the test closes.** `AuditRlsEnforcementTests` already proves the RLS *primitive* — the `audit_admin_bypass` SESSION_CONTEXT flag reveals NULL-tenant (system/global) audit rows under a non-`db_owner` login. But nothing proved the **client wiring**: that `GetIntegrityStatusAsync` / `GetRetentionPolicyStatusAsync(null)` actually route their row COUNTs through the bypass opener (`QuerySingleWithAuditAdminBypassAsync`) rather than the plain opener. A silent refactor to `QuerySingleAsync` would drop NULL-tenant rows from the D7 security-health dashboard totals (QĐ 742 §3.4.a integrity / §3.2 retention) — a compliance under-count with green primitive tests.

## What shipped

- **`AuditEvidenceClientAdminBypassWiringTests`** (new, 2 tests) — drives the real client through a hand-constructed `DapperReadDbConnection` pinned to the non-`db_owner` `rls_test_app` login with a **null-tenant** `TestTenantContext` (so the bypass opener sets *only* `audit_admin_bypass`, exposing NULL-tenant rows and nothing else). Each test seeds one NULL-tenant audit row via `sa`, then:
  - **A/B non-vacuity:** reads the identical COUNT on the *plain* opener → asserts `== 0` (without bypass, the RLS FILTER hides every row on this principal);
  - **wiring:** calls the client method → asserts count `>= 1`.
  The two legs together **mechanically force RED** on any plain-opener refactor (the method would return the same `0`) and **self-guard against a `db_owner` false-green** (a `db_owner` would make the plain leg `>= 1`, failing the `== 0` assertion — the [[feedback-sa-dbowner-false-greens-rls-assertions]] pattern).
- **`RlsPolicyPresenceFixture`** — one idempotent `GRANT SELECT ON [audit].AuditRetentionPolicies TO rls_test_app` (the retention method reads that table on the plain opener before its bypass count; without the grant the test would throw 229). Additive, idempotent, zero blast-radius on sibling tests (verified: no sibling asserts a denial for `rls_test_app` on that table).

## Verification

- Build **0W/0E** (TenantIsolation.Tests).
- `AuditEvidenceClientAdminBypassWiringTests` **2/2 pass** (warm local SQL, `TEST_USE_LOCAL_SQL=true`, 347 ms).
- Code-review **APPROVE_WITH_NITS** — all 6 acceptance criteria CONFIRMED (non-vacuous & RED-on-refactor, genuine non-`db_owner`, pollution-robust on the shared warm DB via `>= 1`/`== 0` not absolute counts, grant additive/idempotent, conventions match sibling, no stable-artifact violations). Two cosmetic nits applied in-slice: `isSystemAdmin: true` (models the admin-only D7 caller, inert to the DB proof) + fixture class-doc grant-list drift note.
- Housekeeping: `51602201` pushed `f73de457..51602201 main -> main` (0 ahead / 0 behind after).

## Decisions & scope calls

- **Covered both methods** (not just integrity) — the stated scope was "the 2 new methods", and the retention method's global-view `@TenantId IS NULL` branch is a distinct SQL path worth pinning. Cost: one additive fixture GRANT.
- **Skipped a mutation run** to prove non-vacuity — the `plainCount == 0` leg *is* the mechanical proof (bypass is the only variable), so a temporary source mutation was unnecessary and riskier.
- **Light docs footprint** — test-only hardening changes no user-facing behavior/contract, so changelog + completed.md entries only (no roadmap/dependency-map churn).

## State after

Monolith `[audit]` read-isolation arc now has **no code-able work remaining**. The only open follow-up is **FU-A** (residual GOV auditor-delta question — `docs/microservices-compliance-isolation-auditor-questions.md`, pending user→auditor), which reopens physical extraction (S1/S2); **FU-B** re-sequencing is gated on it. Both are non-code.

## Lessons reinforced

- [[feedback-sa-dbowner-false-greens-rls-assertions]] — the `== 0` plain leg doubles as the non-`db_owner` guard; a false-green principal fails it.
- [[feedback-test-new-wiring-through-real-registration]] — primitive-level tests don't prove client wiring; drive the real method.
- [[feedback-test-local-sql-warm-reuse-and-false-red-modes]] — designed assertions as `>= 1` / `== 0` (pollution-robust), not absolute counts, for the shared warm DB.

## Unresolved questions

- None blocking. (Informational, out of this slice: the test proves bypass *usage*, not admin-authz *gating* — D7 admin authorization is enforced at the SecurityHealth handler/endpoint, a pre-existing property of the client.)
