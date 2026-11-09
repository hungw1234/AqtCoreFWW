// security-health-types.ts — TypeScript interfaces for QĐ 742 D7 Security Health endpoints.
// Maps 1-to-1 with BE SecurityHealth query DTOs (camelCase via ASP.NET Core default serialization).

// ── §1.4.b/1.4.d — Login / lockout policy ────────────────────────────────────

export interface LoginPolicyStatusDto {
  policyId: number | null;
  policyName: string | null;
  maxFailedAttempts: number;
  lockoutMinutes: number;
  showRemainingAttempts: boolean;
  /** Consecutive lockout count before permanent account disable (QĐ 742 1.4.d). */
  escalationLockoutCount: number;
  /** Rolling window in hours for escalation count (QĐ 742 1.4.d). */
  escalationWindowHours: number;
  /** False when no default CredentialPolicy is configured for the tenant. */
  hasDefaultPolicy: boolean;
}

// ── §1.5.a/1.5.b — Password complexity + rotation policy ─────────────────────

export interface PasswordPolicyStatusDto {
  // Framework (ASP.NET Identity defaults)
  frameworkRequireDigit: boolean;
  frameworkRequireLowercase: boolean;
  frameworkRequireUppercase: boolean;
  frameworkRequireNonAlphanumeric: boolean;
  frameworkRequiredLength: number;
  // Project CredentialPolicy override (null fields when hasProjectPolicy=false)
  hasProjectPolicy: boolean;
  policyId: number | null;
  policyName: string | null;
  minLength: number;
  maxLength: number;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireDigit: boolean;
  requireSpecialChar: boolean;
  /** Days until mandatory rotation (0 = never). */
  rotationDays: number;
  /** Number of previous passwords blocked for reuse. */
  passwordHistoryCount: number;
}

// ── §3.2.a/3.2.b — Audit retention configuration ─────────────────────────────

export interface RetentionPolicyLineDto {
  id: number;
  tenantId: string | null;
  /** Null means global-default row (all categories). */
  categoryId: number | null;
  retentionDays: number;
  archiveBeforePurge: boolean;
  archivePath: string | null;
}

export interface AuditRetentionStatusDto {
  policies: RetentionPolicyLineDto[];
  hotRowCount: number;
  /** Reserved: null until archive sink is provisioned. */
  archiveRowCount: number | null;
}

// ── §3.4.a — Audit log immutability / integrity ───────────────────────────────

export interface AuditIntegrityStatusDto {
  /** True when tr_AuditLog_NoMutate trigger exists and is enabled in sys.triggers. */
  immutabilityTriggerActive: boolean;
  triggerName: string;
  totalLogEntries: number;
}

// ── §4.4.a — Encryption key rotation ─────────────────────────────────────────

export interface KeyRotationStatusDto {
  currentKeyVersion: number;
  /** Null until IKeyRotationMetadataReader is available (backlog). */
  lastRotationAt: string | null;
  /** Null for the same reason as lastRotationAt. */
  nextScheduledRotationAt: string | null;
}

// ── §6.1.b — Backup / restore-drill history ──────────────────────────────────

export interface BackupHistoryItemDto {
  id: string;
  /** "Backup" | "RestoreDrill" */
  type: string;
  /** "Pending" | "InProgress" | "Completed" | "Failed" */
  status: string;
  filePath: string | null;
  fileSizeBytes: number | null;
  errorMessage: string | null;
  triggeredBy: string;
  startedAt: string;
  completedAt: string | null;
}

/** Paginated wrapper matching BE PagedResult<T> + PaginationMeta shape. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number | null;
  meta: {
    page: number | null;
    pageSize: number | null;
    totalPages: number | null;
    nextCursor: string | null;
    prevCursor: string | null;
    hasNextPage: boolean;
  };
}
