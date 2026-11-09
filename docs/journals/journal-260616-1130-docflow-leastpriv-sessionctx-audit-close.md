# Journal — DocFlow least-priv SESSION_CONTEXT row: audit-close

**Date:** 2026-06-16 11:30 ICT · **Slice:** `SIGN-DOCFLOW-LEASTPRIV-SESSIONCTX` (backlog T3) · **Mode:** `/cook` · **Type:** audit-only (docs, no code change)

## What

`/cook` opened a row labelled `blocked-on:prod-least-priv-cutover`. Audit-at-slice-start
(INVARIANT 260522-A) found the label over-broad: 3 of the row's 4 asks are already-satisfied or
moot, leaving only a thin genuinely-cutover-gated remainder. Closed the row, carried the remainder.

## The four asks, walked against ground-truth

| Ask | Ground-truth | Verdict |
|-----|--------------|---------|
| 1. callback sets SESSION_CONTEXT from JWT so RLS engages | `SetTenantSessionContextAsync(jwtTenant)` in both endpoints (`FileUploadHandlerController.cs:78`/`:196`, impl `:352-365`) | shipped (C2 `7cd044bc`) |
| 2. open-guard | `if (conn.State != Open) OpenConnectionAsync` `:359` | shipped |
| 3. clear-on-return | unconditional pool reset (`sp_reset_connection`) clears SESSION_CONTEXT, documented `:356-357` | covered, not cutover-dependent |
| 4. confirm prod `Connection Reset` posture | project uses `Microsoft.Data.SqlClient` 6.1.1 (`Directory.Packages.props:91`) | MOOT |

## Headline lesson — a backlog ask can be moot because of the library in use, not because the work is done

The row asked to "confirm the prod connection-string `Connection Reset` posture when least-priv
lands." That phrasing is pure legacy `System.Data.SqlClient`, where `Connection Reset=false` was a
valid keyword that could disable the per-reuse reset — so an operator *could* misconfigure prod into
leaking SESSION_CONTEXT across pooled borrowers. But `Microsoft.Data.SqlClient` (the client EF Core
binds here) **removed that keyword**: pool-reset is unconditional and cannot be turned off, and
`sp_reset_connection` resets SESSION_CONTEXT every reuse. So the cross-request tenant-bleed the row
worried about is structurally impossible under the in-use client. There is no posture to confirm.

The reusable move: when a row says "confirm X is configured safely", first check whether X is even
*configurable* in the stack actually in use. A concern inherited from an older API surface can be
dead on arrival — verifying the library version is faster than designing a guard for a knob that no
longer exists.

## The belt-and-suspenders fork (declined)

The alternative was to add an explicit `sp_set_session_context N'TenantId', NULL` after each
endpoint's work, so the no-bleed guarantee is *local and self-evident* rather than relying on a
global pool behaviour a future maintainer might not know about. Declined as YAGNI: it adds a
round-trip per request to guard a bleed the in-use client already makes impossible, and the path is
defended in depth anyway (signed-JWT tenant assertion is the real control; RLS + SESSION_CONTEXT is
the future-least-priv backstop). User chose document-and-close. The trade is reversible — if the
team ever swaps SqlClient or sets a custom pool-reset policy, the explicit clear becomes worth it.

## What genuinely remains (carried)

`SIGN-DOCFLOW-LEASTPRIV-E2E-TEST` (T3, still blocked) — an E2E test proving the per-tenant pin
engages RLS under a **real non-db_owner login**. Untestable today: the runtime login is db_owner so
RLS is bypassed; there is no least-priv principal to test against. The pin's correctness is reasoned,
not yet exercised. Wire it with least-priv login provisioning in the RLS-principal runbook (ties to
`AUDITADMIN-CONN-PROD-WIRING` / `RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING`). This is `(c) real-blocked`,
re-affirmed with evidence rather than left as a vague label.

## Verification

Read-only audit — no source, test, or contract change. Empirical basis: `FileUploadHandlerController.cs`
(endpoints + `SetTenantSessionContextAsync`), `Directory.Packages.props:91` (SqlClient version).
