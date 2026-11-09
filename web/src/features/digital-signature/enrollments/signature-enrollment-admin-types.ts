/**
 * signature-enrollment-admin-types.ts — FE DTO mirror for the admin enrollment-approval page
 * (Phase 6 Slice C). Verified against BE source 2026-06-30:
 *   GET  /api/v1/admin/signer-cert-enrollments/pending?page&pageSize
 *   POST /api/v1/admin/signer-cert-enrollments/{publicId}/approve   (204)
 *   POST /api/v1/admin/signer-cert-enrollments/{publicId}/reject    (204) { reason }
 *
 * Tenant scope: STRICTLY the JWT-claim tenant — the controller (H4) deliberately ignores the
 * SystemAdmin X-Tenant-Id override to prevent cross-tenant approval. The page surfaces this.
 *
 * IMPORTANT: the pending endpoint returns the SharedKernel `PagedResult<T>` (nested `meta`),
 * NOT the flat `PaginatedResult<T>` in @/shared/types/api used by /admin/users. Do not swap them —
 * page/pageSize/totalPages live under `meta`, only `items`/`totalCount` are top-level.
 */

// Mirrors BE enum EnrollmentKind { RemoteViettelMysign = 1, UsbTokenVgca = 2 } (string-serialized).
// Duplicated (not imported from profile/signatures) to keep the admin feature decoupled from the
// self-service profile feature — both independently mirror the same BE enum.
export type EnrollmentKind = 'RemoteViettelMysign' | 'UsbTokenVgca';

// Mirrors BE enum CertSubjectType { Personal = 0, Organization = 1 } (JsonStringEnumConverter → string name).
// Duplicated here for the same admin-decoupling reason as EnrollmentKind above.
export type CertSubjectType = 'Personal' | 'Organization';

/** Mirrors BE PendingEnrollmentDto (the admin review-queue row). */
export interface PendingEnrollmentDto {
  publicId: string;
  userId: string;
  kind: EnrollmentKind;
  serialNumber: string;
  issuer: string;
  enrolledAt: string;
  /** Phase 4: Personal = natural-person cert; Organization = org cert. */
  subjectType: CertSubjectType;
  /** Phase 4: display name from cert subject DN. Null for pre-Phase-4 rows. */
  displayName: string | null;
}

/** Mirrors BE PaginationMeta (nested under PagedResult). */
export interface PagedResultMeta {
  page: number | null;
  pageSize: number | null;
  totalPages: number | null;
  nextCursor: string | null;
  prevCursor: string | null;
  hasNextPage: boolean;
}

/** Mirrors BE PagedResult<PendingEnrollmentDto> (nested-meta shape — see header note). */
export interface PendingEnrollmentsPage {
  items: PendingEnrollmentDto[];
  totalCount: number | null;
  meta: PagedResultMeta;
}

/** Mirrors BE RejectEnrollmentRequest { Reason }. */
export interface RejectEnrollmentRequest {
  reason: string;
}
