-- 260612-ipfilterrule-insert-flip-remediation.sql
-- Version: v2.137 (2026-06-12)
-- Purpose: Detect + remediate IpFilterRule rows whose IsEnabled / IsBlock flags were
--          silently flipped on INSERT before the sentinel fix `ffbe2fac`.
--
-- Background:
--   IsEnabled / IsBlock carry a DB DEFAULT of 1 while the CLR default is `false`.
--   Before the fix, EF treated a `false` value as "unset" and OMITTED the column on
--   INSERT, so the DB default persisted the row as enabled / block — inverting the
--   intended security semantics:
--     - a rule created DISABLED (IsEnabled=false) was stored ENABLED  (IsEnabled=1)
--     - a rule created ALLOW    (IsBlock=false)   was stored BLOCK    (IsBlock=1)
--   The fix only stops FUTURE inserts. Rows inserted before the fix reached a given
--   environment may already be wrong in that environment's DB.
--
-- !! The flip is VALUE-INDISTINGUISHABLE. A flipped row reads IsEnabled=1 / IsBlock=1,
--    which is identical to a LEGITIMATE enabled/block rule. You CANNOT tell corruption
--    apart by column value alone. This is why the naive `WHERE IsBlock=0 OR IsEnabled=0`
--    is WRONG — it returns the rows that survived correctly (explicit false), not the
--    flipped ones. Detection narrows to a SUSPECT set (time-windowed, never-updated);
--    each suspect MUST be reconciled against the admin's intended config before any
--    remediation. There is NO safe blanket UPDATE.
--
-- Audience: Ops engineers + tenant DBAs of downstream projects that adopted AqtCoreFW
--           BEFORE `ffbe2fac`. AqtCoreFW itself is a template repo with no live prod
--           (see docs/ops/ipfilterrule-flip-data-audit-runbook.md).
--
-- Required cleanup AFTER any remediation UPDATE (direct DB write bypasses the command
-- handlers that normally invalidate the IP rule cache):
--   - Flush cache key `system:ip-rules`, e.g.:
--       docker exec docker-redis-1 redis-cli DEL system:ip-rules
--     OR simply wait out the 5-minute cache TTL (IpFilterMiddleware auto-reloads).
--
-- All timestamps are UTC (datetime2(3) convention).

SET NOCOUNT ON;

-- ── Step 0: set the fix-deploy boundary FOR THIS ENVIRONMENT ──────────────────────
-- This is the UTC instant `ffbe2fac` was DEPLOYED to the environment you are auditing,
-- NOT the commit-author time. Rows created at/after this instant cannot be flipped.
-- Default below = commit-author time (2026-06-11 18:48:48 +07 = 11:48:48 UTC). OVERRIDE
-- it with this env's actual deploy time.
DECLARE @FixDeployUtc datetime2(3) = '2026-06-11T11:48:48';

-- ── Step 1: sanity — total rows + flag distribution ──────────────────────────────
SELECT COUNT(*) AS TotalRows FROM [identity].[IpFilterRules];

SELECT IsEnabled, IsBlock, COUNT(*) AS Cnt
FROM [identity].[IpFilterRules]
GROUP BY IsEnabled, IsBlock
ORDER BY IsEnabled, IsBlock;

-- ── Step 2: SUSPECT detection ─────────────────────────────────────────────────────
-- Suspect = ANY row created before the fix reached this env (CreatedAt < @FixDeployUtc).
-- Why this wide net: pre-fix, EVERY inserted row stored IsEnabled=1 AND IsBlock=1
-- (intended-false was flipped to 1; intended-true stayed 1), so the flag values carry
-- NO information about the original intent — `CreatedAt` is the only real discriminator.
--
-- `UpdatedAt` is shown as a TRIAGE hint, NOT a filter. An UPDATE that *changed* a flag
-- (true->false) re-emitted it and healed that row; but an edit that resubmitted the
-- displayed (flipped) value (true->true) is NOT emitted by EF, leaving the row wrong yet
-- stamped UpdatedAt. Excluding UpdatedAt IS NOT NULL would therefore MISS still-wrong
-- rows — unacceptable for a security control (a wrongly-blocked admin CIDR = lockout).
-- Prioritise UpdatedAt IS NULL (definitely untouched) but reconcile updated rows too.
--
-- A suspect is NOT proven corrupt — a 1/1 row may be a legitimate enabled block rule.
-- Reconcile EACH against intended config (audit log / tickets / admin) before any fix.
SELECT Id, TenantId, Cidr, Description, IsEnabled, IsBlock, CreatedAt, UpdatedAt, CreatedBy
FROM [identity].[IpFilterRules]
WHERE CreatedAt < @FixDeployUtc
ORDER BY UpdatedAt, CreatedAt;   -- UpdatedAt IS NULL (untouched) surfaces first

-- ── Step 3: remediation TEMPLATE (per confirmed-wrong row, NOT a blanket UPDATE) ──
-- After reconciling a suspect and confirming the admin INTENDED a different value,
-- restore it inside an explicit transaction. Fill @Id + the intended flags per row.
-- Re-running is safe (idempotent): a row already at the intended value is a no-op.
--
-- BEGIN TRANSACTION;
--   DECLARE @Id bigint = <confirmed-row-id>;
--   UPDATE [identity].[IpFilterRules]
--   SET IsEnabled = <intended 0|1>,
--       IsBlock   = <intended 0|1>,
--       UpdatedAt = SYSUTCDATETIME()
--   WHERE Id = @Id
--     AND (IsEnabled <> <intended 0|1> OR IsBlock <> <intended 0|1>);  -- idempotent guard
--   -- review @@ROWCOUNT (expect 0 or 1) before committing:
--   SELECT @@ROWCOUNT AS RowsFixed;
-- COMMIT TRANSACTION;   -- or ROLLBACK if @@ROWCOUNT is unexpected
--
-- Then flush the IP rule cache (see header "Required cleanup").
