// security-health-api.ts — TanStack Query hooks for QĐ 742 D7 Security Health endpoints.
// All routes under GET /api/v1/admin/security/health/*
// apiClient interceptor unwraps {success,data,errors} envelope → access res.data directly (NOT res.data.data).
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  LoginPolicyStatusDto,
  PasswordPolicyStatusDto,
  AuditRetentionStatusDto,
  AuditIntegrityStatusDto,
  KeyRotationStatusDto,
  BackupHistoryItemDto,
  PagedResult,
} from './security-health-types';

// ── Query key factory ─────────────────────────────────────────────────────────

export const securityHealthKeys = {
  all: ['security-health'] as const,
  loginPolicy:    ['security-health', 'login-policy'] as const,
  passwordPolicy: ['security-health', 'password-policy'] as const,
  auditRetention: ['security-health', 'audit-retention'] as const,
  auditIntegrity: ['security-health', 'audit-integrity'] as const,
  keyRotation:    ['security-health', 'key-rotation'] as const,
  backupHistory:  (page: number, pageSize: number) =>
    ['security-health', 'backup-history', page, pageSize] as const,
};

// Auto-refresh interval for health pages (30 seconds).
const REFETCH_INTERVAL_MS = 30_000;

// ── §1.4.b/1.4.d — Login policy ──────────────────────────────────────────────

/** GET /api/v1/admin/security/health/login-policy */
export function useLoginPolicyStatus() {
  return useQuery({
    queryKey: securityHealthKeys.loginPolicy,
    queryFn: () =>
      apiClient
        .get<LoginPolicyStatusDto>('/admin/security/health/login-policy')
        .then((r) => r.data),
    refetchInterval: REFETCH_INTERVAL_MS,
  });
}

// ── §1.5.a/1.5.b — Password policy ───────────────────────────────────────────

/** GET /api/v1/admin/security/health/password-policy */
export function usePasswordPolicyStatus() {
  return useQuery({
    queryKey: securityHealthKeys.passwordPolicy,
    queryFn: () =>
      apiClient
        .get<PasswordPolicyStatusDto>('/admin/security/health/password-policy')
        .then((r) => r.data),
    refetchInterval: REFETCH_INTERVAL_MS,
  });
}

// ── §3.2.a/3.2.b — Audit retention ───────────────────────────────────────────

/** GET /api/v1/admin/security/health/audit-retention */
export function useAuditRetentionStatus() {
  return useQuery({
    queryKey: securityHealthKeys.auditRetention,
    queryFn: () =>
      apiClient
        .get<AuditRetentionStatusDto>('/admin/security/health/audit-retention')
        .then((r) => r.data),
    refetchInterval: REFETCH_INTERVAL_MS,
  });
}

// ── §3.4.a — Audit integrity ──────────────────────────────────────────────────

/** GET /api/v1/admin/security/health/audit-integrity */
export function useAuditIntegrityStatus() {
  return useQuery({
    queryKey: securityHealthKeys.auditIntegrity,
    queryFn: () =>
      apiClient
        .get<AuditIntegrityStatusDto>('/admin/security/health/audit-integrity')
        .then((r) => r.data),
    refetchInterval: REFETCH_INTERVAL_MS,
  });
}

// ── §4.4.a — Key rotation ─────────────────────────────────────────────────────

/** GET /api/v1/admin/security/health/key-rotation */
export function useKeyRotationStatus() {
  return useQuery({
    queryKey: securityHealthKeys.keyRotation,
    queryFn: () =>
      apiClient
        .get<KeyRotationStatusDto>('/admin/security/health/key-rotation')
        .then((r) => r.data),
    refetchInterval: REFETCH_INTERVAL_MS,
  });
}

// ── §6.1.b — Backup history ───────────────────────────────────────────────────

/** GET /api/v1/admin/security/health/backup-history?page=&pageSize= */
export function useBackupHistoryStatus(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: securityHealthKeys.backupHistory(page, pageSize),
    queryFn: () =>
      apiClient
        .get<PagedResult<BackupHistoryItemDto>>('/admin/security/health/backup-history', {
          params: { page, pageSize },
        })
        .then((r) => r.data),
    refetchInterval: REFETCH_INTERVAL_MS,
  });
}
