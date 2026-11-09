/** DTO matching BE SodViolationLogDto — INNER JOIN ensures codes/severity always present */
export interface SodViolationLogDto {
  id: string;
  detectedAt: string; // ISO datetime
  userId?: string;
  roleId?: string;
  ruleId?: string;
  permissionCodeA: string;
  permissionCodeB: string;
  severity: string;
  actorId: string;
  justification?: string;
}

/** Paged result from BE ListSodViolationLogsQueryResult */
export interface SodViolationLogsPagedResult {
  items: SodViolationLogDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Filter params for list violations query */
export interface SodViolationsFilters {
  userId?: string;
  roleId?: string;
  ruleId?: string;
  from?: string; // ISO date string
  to?: string;   // ISO date string
}
