/**
 * forms-signature-doc-hash-api.ts — Mode-2 (ServerDocHash) sign-time hash fetch.
 *
 * For a DigitalSignature field configured in ServerDocHash mode, the FE does NOT build the
 * signed bytes locally — it asks the server for the canonical 32-byte document hash, signs that,
 * and submits the signature. The BE recomputes the same hash at ingest and verifies over it
 * (determinism invariant). See canonicalization-spec-mode2.md.
 *
 * Authenticated form-fill only this slice — anonymous public-form signing is deferred
 * (the /sign path is MFA-gated and anonymous USB-local signing is out of scope for now).
 */

import { apiClient } from '@/core/api';

/** Mirrors BE SignatureDocHashDto. */
export interface SignatureDocHashResult {
  hashBase64: string;
  hashAlg: string;
}

/**
 * POST /api/v1/forms/{formTemplatePublicId}/signature-doc-hash
 * Returns the Mode-2 document hash (base64) for the supplied draft data. TenantId is resolved
 * from the JWT server-side. Throws on 404 (no template / no ServerDocHash field) — the caller
 * surfaces it as a signing error.
 */
export async function fetchSignatureDocHash(
  formTemplatePublicId: string,
  data: Record<string, unknown>,
): Promise<SignatureDocHashResult> {
  const r = await apiClient.post<SignatureDocHashResult>(
    `/forms/${formTemplatePublicId}/signature-doc-hash`,
    { data },
  );
  return r.data;
}
