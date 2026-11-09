# IpFilterRule Insert-Flip Data Audit Runbook

**Purpose:** One-time per-environment audit + remediation of `IpFilterRule` rows whose `IsEnabled` / `IsBlock` flags were silently flipped on INSERT before the sentinel fix (`ffbe2fac`).

**Compliance:** QĐ 742 cấp 4 §2.2.a — quản lý IP quản trị từ xa (remote-admin IP whitelist/blocklist). A flipped rule mis-enforces this control.

**Audience:** Ops engineers + tenant DBAs of downstream projects that adopted AqtCoreFW **before** `ffbe2fac`.

**Template-repo note:** AqtCoreFW is a template repository with **no live production environment**. Its dev DB is clean (0 `IpFilterRules` rows, verified 2026-06-12). This runbook is the pattern downstream adopters must run on each environment whose DB was seeded with IP rules before they picked up the fix. There is nothing to execute against AqtCoreFW itself.

**Companion script:** `scripts/260612-ipfilterrule-insert-flip-remediation.sql` (parameterized detection + remediation template).

---

## 1. The bug, precisely

`IsEnabled` and `IsBlock` carry a **DB `DEFAULT 1`** while their **CLR default is `false`**. Before the fix, EF treated a `false` value as "unset" and **omitted the column on INSERT**, so the DB default persisted it as `1`:

| Intended on create | Stored before fix | Effect |
|--------------------|-------------------|--------|
| `IsEnabled = false` (disabled, soft-off) | `IsEnabled = 1` (enabled) | A rule meant to be inert is **live**. |
| `IsBlock = false` (allow / whitelist override) | `IsBlock = 1` (block) | A whitelist entry becomes a **deny** — can lock out legitimate admin IPs. |

The fix (`HasSentinel(true)` on both columns) forces EF to emit the explicit `false`. **It only stops future inserts** — rows already written wrong stay wrong until remediated.

The `disabled → enabled` flip is **not cosmetic**: the middleware loads its rule set via `ListAllEnabledAsync` (filtered on `IsEnabled`), so a rule the admin staged as inert (`IsEnabled=false`) is flipped on and **actively enforced**. Combined with the `allow → block` flip, a staged-disabled whitelist override can silently become a live deny rule.

## 2. Why the naive query is WRONG

The originally-filed audit query was `SELECT * FROM [identity].IpFilterRules WHERE IsBlock=0 OR IsEnabled=0`.

That is **inverted**. The flip only pushes `false → true`, so:

- A **flipped** (corrupted) row reads `IsEnabled=1` / `IsBlock=1`.
- `WHERE IsBlock=0 OR IsEnabled=0` returns the rows whose explicit `false` **survived** — i.e. the rows created *after* the fix, or later UPDATEd. Those are exactly the **non-corrupted** rows.

Run as-is, the naive query returns the healthy rows and **zero** flipped ones → a false "all clean" signal.

**Corollary — corruption is value-indistinguishable.** A flipped `IsEnabled=1, IsBlock=1` row is byte-identical to a legitimately enabled block rule. No SQL predicate on the row alone can separate them. Detection can only narrow to a **suspect set**; confirming corruption requires reconciling each suspect against what the admin *intended*.

## 3. Detection procedure

Run `scripts/260612-ipfilterrule-insert-flip-remediation.sql` Steps 0–2 against the target environment's DB (read-only).

**Step 0 — set the boundary.** `@FixDeployUtc` = the UTC instant the fix was **deployed to this environment** (not commit-author time). Rows created at/after this instant cannot be flipped.

**Steps 1–2 — narrow to suspects.** The suspect set is **every row created before the fix**: `CreatedAt < @FixDeployUtc`. The flag values can't narrow it further — pre-fix, *every* inserted row stored `IsEnabled=1 AND IsBlock=1` (intended-`false` flipped to 1; intended-`true` stayed 1), so the columns carry no information about original intent. `CreatedAt` is the only real discriminator.

`UpdatedAt` is a **triage hint, not a filter**:

| `UpdatedAt` | Meaning | Priority |
|-------------|---------|----------|
| `NULL` | Never edited since the flipped insert — definitely still as-stored. | **Highest** — reconcile first. |
| not `NULL` | Edited at least once. If the edit *changed* a flag (`true→false`) EF re-emitted it and **healed** the row; but an edit that resubmitted the displayed flipped value (`true→true`) is **not** emitted by EF, so the row stays wrong despite the stamp. | Lower, but **still reconcile** — do not exclude. |

> Excluding `UpdatedAt IS NOT NULL` would silently miss still-wrong rows that were edited without correcting the flag. For a security control (a wrongly-blocked admin CIDR = lockout), that false-negative is unacceptable, so the detection keeps the wide net.

If the suspect set is empty → this environment is clean, **done**. Record the result.

## 4. Reconciliation

For each suspect, decide whether it was *intended* the way it is stored. Sources of intended config, in order of reliability:

1. **Audit log** — IP rule create/update events (the Audit module records admin actions). Compare the recorded request payload's `isEnabled` / `isBlock` to the stored row.
2. **Change tickets / IaC / seed scripts** that defined the original rule set.
3. **Admin confirmation** — for `IsBlock`, ask: was this CIDR meant to **block** or to **allow (whitelist)**? A wrongly-blocked admin CIDR is the highest-impact case (lockout risk).

Flag a suspect as **confirmed-wrong** only when the intended value differs from the stored value.

## 5. Remediation

There is **no safe blanket UPDATE** — that would clobber legitimately enabled/block rules. Fix per confirmed-wrong row using the Step 3 template in the companion script:

- Wrap in an explicit transaction; review `@@ROWCOUNT` (expect 0 or 1) before `COMMIT`.
- The idempotent guard (`WHERE ... IsEnabled <> @intended OR IsBlock <> @intended`) makes re-runs safe.
- Set `UpdatedAt = SYSUTCDATETIME()` so the row is excluded from future suspect scans.

## 6. Post-remediation — flush the rule cache (MANDATORY)

`IpFilterMiddleware` caches the compiled rule set under key **`system:ip-rules`** with a **5-minute TTL**, invalidated only by the Create/Update/Delete **command handlers**. A direct DB UPDATE **bypasses** that invalidation, so the middleware keeps enforcing the stale (still-flipped) rules for up to 5 minutes.

After any remediation UPDATE, do one of:

- **Flush explicitly** (preferred): `docker exec docker-redis-1 redis-cli DEL system:ip-rules` (adjust container/host for your cache backend).
- **Wait out the TTL**: 5 minutes, after which the middleware reloads from DB on the next request.

## 7. Verification

1. Re-run Steps 1–2 → confirmed-wrong rows no longer appear (now `UpdatedAt` is set / flags corrected).
2. Confirm the cache was flushed: a request matching a remediated allow-rule is no longer blocked (or a remediated disabled-rule no longer fires).
3. Record per-environment outcome (clean / N rows fixed) in the change log.

---

## Appendix — affected schema

- Table: `[identity].[IpFilterRules]` (SQL Server).
- Flag columns: `IsEnabled bit NOT NULL DEFAULT 1`, `IsBlock bit NOT NULL DEFAULT 1`.
- Timestamps: `CreatedAt datetime2(3) NOT NULL` (UTC), `UpdatedAt datetime2(3) NULL` (UTC).
- Code anchors (for context, not part of the procedure): `IpFilterRuleConfiguration.cs` (sentinel fix), `IpFilterMiddleware.cs` (cache key + TTL), `IpRuleSetLoader.cs` (DB load).
