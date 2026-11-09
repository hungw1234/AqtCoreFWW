// TypeScript interfaces for IP Filter Rule — matches IpRuleDto from BE
// T1 entity: addressed by long Id (no PublicId), no RowVersion (last-write-wins on update)

export interface IpRuleDto {
  id: number; // long maps to number in TS (safe: admin UI, not bigint edge-case domain)
  tenantId?: string;
  cidr: string;
  description: string;
  isEnabled: boolean;
  isBlock: boolean; // true=block, false=allow (whitelist mode)
  createdAt: string; // ISO datetime
  updatedAt?: string;
}

export interface CreateIpRuleRequest {
  tenantId?: string;
  cidr: string;
  description?: string;
  isEnabled: boolean;
  isBlock: boolean;
}

export interface UpdateIpRuleRequest {
  cidr: string;
  description?: string;
  isEnabled: boolean;
  isBlock: boolean;
}
