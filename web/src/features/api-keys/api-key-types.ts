// ApiKeyDto — matches backend ApiKey entity (key value never returned after creation)
export interface ApiKeyDto {
  id: string;
  name: string;
  prefix: string;          // first 8 chars shown in list, rest masked
  scopes: string[];
  createdAt: string;
  expiresAt?: string;
  isActive: boolean;
  lastUsedAt?: string;
}

// CreateApiKeyRequest — POST /api/v1/admin/api-keys
export interface CreateApiKeyRequest {
  name: string;
  scopes: string[];
  expiresAt?: string;      // ISO 8601 or undefined for no expiry
}

// CreateApiKeyResponse — includes the full key shown ONCE
export interface CreateApiKeyResponse extends ApiKeyDto {
  plainTextKey: string;   // shown once; store securely — never returned again
}

// Available API scopes for multiselect
export const API_SCOPES = [
  'read:users',
  'write:users',
  'read:audit',
  'read:reports',
  'write:notifications',
  'admin:full',
] as const;

export type ApiScope = (typeof API_SCOPES)[number];
