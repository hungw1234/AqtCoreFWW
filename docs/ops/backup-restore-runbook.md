# Backup & Restore Runbook

Disaster-recovery (DR) backup for the AqtCoreFW SQL Server database. This is **distinct from
the NĐ53 archive jobs** — see [Backup ≠ Archive](#backup--archive) below.

## Summary

| Property | Value |
|----------|-------|
| **RPO** (max data loss) | ≈ 24h — a full backup runs daily at **02:00** |
| **RTO** (recovery time) | Restore-drill-verified — bounded by DB size + `RESTORE DATABASE` time |
| **Schedule** | Daily 02:00 (`scheduled-backup` Hangfire recurring job) |
| **Retention** | `Backup:RetentionDays` (default **30 days**) — expired `.bak` files + records auto-pruned |
| **Storage** | `Backup:Directory` (default `/backups`) — **MUST be a persistent volume** (see below) |

## What runs

- **`scheduled-backup`** (Hangfire recurring, cron `0 2 * * *`) → `DatabaseBackupService.RunScheduledBackupAsync()`:
  1. creates a `BackupRecord` (Type `ScheduledBackup`, no user),
  2. runs `BACKUP DATABASE [{db}] TO DISK ... WITH FORMAT, INIT, COMPRESSION` into `Backup:Directory`,
  3. prunes any completed backups older than `Backup:RetentionDays` (file + record).
- **Manual trigger** (unchanged): `POST /api/v1/admin/backup` (SystemAdmin + MFA) → one-off backup.
- **Restore drill** (unchanged): `POST /api/v1/admin/backup/restore-drill` → backup → restore to a temp DB → verify table count → drop temp DB. Use this to validate recoverability.

Backup status is tracked in `[backup].[BackupRecords]` (`GET /api/v1/admin/backup` to list).

## Configuration

`appsettings.json` (defaults) / `appsettings.Production.json`:

```json
"Backup": {
  "Directory": "/backups",
  "CronExpression": "0 2 * * *",
  "RetentionDays": 30
}
```

- `Directory` and `RetentionDays` are read at runtime.
- `CronExpression` is **documented here but the schedule is currently a code literal** (`0 2 * * *`
  in `HangfireRegistration.RegisterRecurringJobs()`) — cron rarely changes; edit the literal +
  redeploy to change it. The config key is retained for documentation/parity.

## Persistent volume (MANDATORY in K8s)

`Backup:Directory` defaults to `/backups`, which is **ephemeral inside a container** — backups
written there **vanish on pod restart**. In Kubernetes you MUST mount a persistent volume (PVC)
at `Backup:Directory`, or point `Backup:Directory` at a mounted network share.

> **Helm gap (follow-up):** `infra/.../values.yaml` does not yet declare a PVC + volumeMount for
> `/backups`. Until added, a K8s deployment has no durable backup target. Track as a separate ops task.

## Backup login permission

The `Default` connection login needs `BACKUP DATABASE` rights (`db_owner`, `dbcreator`, or the
least-privilege `db_backupoperator` role). The stable-monolith app login is `db_owner`-capable
today, so backups work as-is. If the app is later moved to a least-privilege login, grant it
`db_backupoperator` or backups will be marked `Failed` (the failure is recorded on the
`BackupRecord`, it does **not** crash the host).

## Backup ≠ Archive

Do not conflate these — they solve different problems:

| | `scheduled-backup` (this runbook) | `archive-audit` / `archive-cases` (NĐ53) |
|--|--|--|
| Purpose | Full-DB **disaster recovery** restore point | Move **aged rows** to cold storage for compliance retention |
| Output | `.bak` file (whole database) | Rows relocated within the DB / cold store |
| Schedule | Daily 02:00 | Monthly, 1st at 03:00 |
| Restores the DB? | Yes | No |

## Manual restore (real recovery)

1. Identify the `.bak` to restore (latest good `Completed` record in `[backup].[BackupRecords]`, or
   newest file in `Backup:Directory`).
2. For a **verified dry-run**, use the built-in restore drill (`POST /api/v1/admin/backup/restore-drill`)
   which restores to a temp DB, verifies, and drops it — proves the backup is recoverable without
   touching the live DB.
3. For an **actual recovery** (destructive — overwrites the live DB), with the app stopped:
   ```sql
   RESTORE DATABASE [<database>] FROM DISK = '<path-to-.bak>' WITH REPLACE;
   ```
   Then restart the app and confirm startup migrations report no pending changes.

## Rollback (of the backup automation itself)

The recurring registration is idempotent (`AddOrUpdate`). To stop scheduled backups without a
redeploy: remove the job via the Hangfire dashboard, or
`RecurringJob.RemoveIfExists("scheduled-backup")`. No schema/migration change is involved.
