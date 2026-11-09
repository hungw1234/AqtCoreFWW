// MfaFactor flags enum — mirrors AqtCoreFW.Identity.Domain.Entities.MfaFactor
// Values: Totp = 1, EmailOtp = 2 (flags: can combine with bitwise OR)
export const MfaFactor = {
  None: 0,
  Totp: 1,
  EmailOtp: 2,
  All: 3, // Totp | EmailOtp
} as const;
export type MfaFactor = (typeof MfaFactor)[keyof typeof MfaFactor];

// MfaPolicyDto — read model returned by GET /api/v1/admin/security/mfa-policies
export interface MfaPolicyDto {
  id: string;
  role: string;
  isMandatory: boolean;
  graceDays: number;
  allowedFactors: MfaFactor;
  createdAt: string; // ISO 8601
  updatedAt: string | null;
}

// CreateMfaPolicyRequest — POST /api/v1/admin/security/mfa-policies
export interface CreateMfaPolicyRequest {
  role: string;
  isMandatory: boolean;
  graceDays: number;
  allowedFactors: MfaFactor;
}

// UpdateMfaPolicyRequest — PUT /api/v1/admin/security/mfa-policies/{id}
export interface UpdateMfaPolicyRequest {
  isMandatory: boolean;
  graceDays: number;
  allowedFactors: MfaFactor;
}

// ApiResponse wrapper matching AqtCoreFW.SharedKernel.Api.ApiResponse<T>
export interface ApiResponse<T> {
  data: T;
  success: boolean;
  errors: string[];
}
