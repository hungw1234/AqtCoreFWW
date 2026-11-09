// Types for the admin overview dashboard
export interface AdminDashboardStats {
  healthStatus: 'healthy' | 'degraded' | 'unhealthy';
  healthChecks: { name: string; status: string; durationMs?: number }[];
  totalUsers: number;
  activeSessions: number;
  pendingJobs: number;
  failedJobs: number;
}

// Matches BE AuditLogDto shape
export interface AdminActivityEntry {
  id: string;
  userName: string;
  action: string;
  moduleName: string;
  resourceType: string;
  resourceId?: string;
  occurredAt: string;
}

// Backend response shapes (minimal — only fields we consume)
export interface HealthApiResponse {
  status: string;
  checks?: { name: string; status: string; durationMs?: number }[];
  totalDurationMs?: number;
}

export interface PagedApiResponse<T> {
  items: T[];
  totalCount: number;
}

export interface JobItem {
  id: string;
  name: string;
  status: string;
  createdAt?: string;
}
