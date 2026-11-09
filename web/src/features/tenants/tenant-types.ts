// TenantStatus matches BE Identity.Domain.ValueObjects.TenantStatus int values
// (Active=1, Suspended=2, Archived=3) — the API serializes the enum name.
export type TenantStatus = 'Active' | 'Suspended' | 'Archived';

// TenantAdminDto — matches BE TenantAdminDto (list rows).
export interface TenantAdminDto {
  id: string;
  name: string;
  code: string;
  contactEmail: string | null;
  status: TenantStatus;
  createdAt: string;
  userCount: number;
}

// TenantDetailDto — matches BE TenantDetailDto (single tenant detail).
export interface TenantDetailDto {
  id: string;
  name: string;
  code: string;
  contactEmail: string | null;
  status: TenantStatus;
  createdAt: string;
  updatedAt: string | null;
  createdBy: string;
  modifiedBy: string | null;
}

// CreateTenantRequest — POST /api/v1/admin/tenants.
export interface CreateTenantRequest {
  name: string;
  code: string;
  contactEmail?: string | null;
}

// UpdateTenantRequest — PUT /api/v1/admin/tenants/{id}. Code is immutable post-create.
export interface UpdateTenantRequest {
  name: string;
  contactEmail?: string | null;
}

// ChangeTenantStatusRequest — POST /api/v1/admin/tenants/{id}/status.
export interface ChangeTenantStatusRequest {
  status: TenantStatus;
}

// TenantListParams — query params for GET /api/v1/admin/tenants.
export interface TenantListParams {
  search?: string;
  status?: TenantStatus;
  page?: number;
  pageSize?: number;
}

// PagedResult shape returned by BE ListTenantsAdminQuery (SharedKernel.PagedResult).
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
