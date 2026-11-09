import type { PaginationParams } from '@/shared/types/api';

// AuditLogEntry — matches backend AuditLog entity
export interface AuditLogEntry {
  id: string;
  occurredAt: string;      // ISO 8601 — BE field name (was incorrectly "timestamp")
  userId: string;
  userName: string;
  action: string;          // e.g., "Create", "Update", "Delete", "Login"
  moduleName: string;      // e.g., "Users", "Cases", "Audit"
  resourceType: string;    // e.g., "Case", "User"
  resourceId?: string;     // nullable — not all actions have a resource ID
  ipAddress: string;
  tenantId: string;
  details?: string;        // JSON string — before/after diff
  correlationId?: string;
}

// AuditLogParams — query params for GET /api/v1/audit/logs
export interface AuditLogParams extends PaginationParams {
  tenantId?: string;
  from?: string;           // ISO 8601
  to?: string;             // ISO 8601
  userId?: string;
  action?: string;
  moduleName?: string;
  resourceType?: string;
  resourceId?: string;
  page?: number;
  pageSize?: number;
}

// AuditStatistics — matches backend GetAuditStatisticsQuery result
export interface AuditStatistics {
  today: number;
  thisWeek: number;
  thisMonth: number;
  byAction: Record<string, number>;
  byModule: Record<string, number>;
}

// LoginAuditEntry — matches backend LoginAuditLog DTO
export interface LoginAuditEntry {
  id: string;
  attemptedAt: string;     // BE field: AttemptedAt
  email: string;           // BE field: Email (user identifier for login attempts)
  ipAddress: string;
  userAgent: string;
  success: boolean;
  failureReason?: string;
  tenantId: string;
}

// LoginAuditParams — query params for GET /api/v1/admin/login-audit
export interface LoginAuditParams extends PaginationParams {
  tenantId?: string;
  from?: string;
  to?: string;
  userName?: string;
  success?: boolean;
}

// SecurityIncidentEntry — matches backend SecurityIncident DTO
export interface SecurityIncidentEntry {
  id: string;
  occurredAt: string;      // BE field: OccurredAt (was incorrectly "timestamp")
  type: string;
  severity: 'Low' | 'Medium' | 'High' | 'Critical';
  description: string;
  status: 'Open' | 'Investigating' | 'Resolved' | 'Closed';
  affectedUserId?: string;
  affectedUserName?: string;
  ipAddress?: string;
  tenantId: string;
}

// SecurityIncidentParams — query params for GET /api/v1/admin/incidents
export interface SecurityIncidentParams extends PaginationParams {
  tenantId?: string;
  from?: string;
  to?: string;
  severity?: 'Low' | 'Medium' | 'High' | 'Critical';
  status?: 'Open' | 'Investigating' | 'Resolved' | 'Closed';
}

// Common audit action values for filter dropdowns
export const AUDIT_ACTIONS = [
  'Create',
  'Update',
  'Delete',
  'Login',
  'Logout',
  'Export',
  'Import',
  'View',
  'Approve',
  'Reject',
] as const;

export type AuditAction = (typeof AUDIT_ACTIONS)[number];
