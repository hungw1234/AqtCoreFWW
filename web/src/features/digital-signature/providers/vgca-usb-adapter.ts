/**
 * vgca-usb-adapter.ts — VGCA USB Token adapter (W1 rewrite).
 *
 * VGCA vgca_sign_msg real contract (proven from test-bc.htm + vgcaplugin.js source):
 *   Input:  JSON.stringify({ HashAlg: 'SHA256', HashValue: <base64-sha256>, Encode: 'RSA' })
 *   Output: { Status: 0, Signature: <base64 raw sig>, CertBase64: <base64 DER cert> }
 *
 * Prior broken implementation passed raw payload base64 and expected Status:200 + SignData —
 * that was wrong. Real SDK always uses Status:0 for success.
 *
 * vgca_get_version(cb) is the canonical availability probe (1-arg, no sender).
 * vgca_get_certinfo(cb) fetches current user cert info (1-arg, no sender).
 *
 * Globals declared on window (obfuscated but exported by vgcaplugin.js):
 *   vgca_sign_msg(sender, dataToSign, cb)  — 3-arg
 *   vgca_get_version(cb)                   — 1-arg
 *   vgca_get_certinfo(cb)                  — 1-arg
 */

import { registerAdapter } from '../registry';
import type { CertPreview, SignJsonOpts, SignOpts, SignResult, SignXmlOpts } from '../signature-types';
import { uint8ToBase64 } from '../base64-utils';
import { probeVgcaAgent } from './vgca-availability-probe';
import { ensureVgcaPluginLoaded } from './vgca-script-loader';
import { sha256Base64 } from './vgca-hash';
import {
  signIssued,
  signApproved,
  signIncome,
  comment,
  signAppendix,
  signCopy,
  signFiles,
} from './vgca-docflow-methods';

const SIGN_TIMEOUT_MS = 30_000;
const CERT_INFO_TIMEOUT_MS = 5_000;

// ─── Sign implementation ──────────────────────────────────────────────────────

async function sign(payload: Uint8Array, opts: SignOpts): Promise<SignResult> {
  await ensureVgcaPluginLoaded();

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const vgcaSign = (window as any)['vgca_sign_msg'] as
    | ((sender: unknown, data: string, cb: (sender: unknown, response: string) => void) => void)
    | undefined;

  if (typeof vgcaSign !== 'function') {
    throw new Error('vgca_sign_msg not available — VGCA plugin not loaded');
  }

  // Pre-compute SHA-256 hash of payload — VGCA expects HashValue, not raw bytes
  const hashValue = await sha256Base64(payload);
  const encode = opts.encode ?? 'RSA';

  const data = JSON.stringify({
    HashAlg: 'SHA256',
    HashValue: hashValue,
    Encode: encode,
  });

  const sender = crypto.randomUUID();

  return new Promise<SignResult>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error('VGCA sign timed out after 30s — check that VGCASignService is running'));
    }, SIGN_TIMEOUT_MS);

    vgcaSign(sender, data, (_returnedSender: unknown, response: string) => {
      clearTimeout(timer);
      try {
        const parsed = JSON.parse(response) as Record<string, unknown>;
        const status = parsed['Status'];
        const signature = parsed['Signature'];
        const certBase64 = parsed['CertBase64'];
        const message = parsed['Message'] ?? parsed['Error'] ?? response;

        if (status !== 0 || typeof signature !== 'string') {
          reject(new Error(`VGCA sign failed (Status=${status}): ${message}`));
          return;
        }

        resolve({
          signatureBase64: signature as string,
          signerCertBase64: typeof certBase64 === 'string' ? certBase64 : undefined,
          providerKey: 'usb-vgca',
          isPending: false,
          transactionId: sender,
          hashAlg: 'SHA256',
          encode,
        });
      } catch {
        reject(new Error(`VGCA response parse error: ${response}`));
      }
    });
  });
}

// ─── signXml (W2 Tier-2) ─────────────────────────────────────────────────────

/**
 * Sign XML content via vgca_sign_xml — W2 Tier-2 XMLDSig.
 *
 * SDK contract (from vgcaplugin.js + DefaultECC.htm sample):
 *   vgca_sign_xml(sender, paramsJson, cb)  — 3-arg
 *   params: { Base64Content, ReturnSignatureOnly, HashAlg, XmlDsigForm }
 *   callback: cb(sender, responseJson)
 *   response success: { Status: 0, Signature: <base64 signed XML> }
 *   response failure: { Status: <nonzero>, Error: <msg> }
 *
 * SDK_VERIFICATION_PENDING: DefaultECC.htm uses XmlDsigForm:"false" and HashAlg:"Sha256".
 * Task spec uses form="Enveloped" and hashAlg="SHA256". Using task spec values until hardware verified.
 *
 * Returns base64-encoded signed XML bytes (suitable for POST to /digital-signature/verify-xml).
 */
async function signXml(xmlContent: string, opts?: SignXmlOpts): Promise<string> {
  await ensureVgcaPluginLoaded();

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const vgcaSignXml = (window as any)['vgca_sign_xml'] as
    | ((sender: unknown, params: string, cb: (sender: unknown, response: string) => void) => void)
    | undefined;

  if (typeof vgcaSignXml !== 'function') {
    throw new Error('vgca_sign_xml not available — VGCA plugin not loaded');
  }

  // Encode XML as base64 using TextEncoder (not btoa) — handles Vietnamese diacritics correctly
  const xmlBytes = new TextEncoder().encode(xmlContent);
  const base64Content = uint8ToBase64(xmlBytes);

  const params = JSON.stringify({
    Base64Content: base64Content,
    // SDK_VERIFICATION_PENDING: sample uses "false" string; spec uses "Enveloped"
    ReturnSignatureOnly: opts?.signatureOnly === true ? 'true' : 'false',
    HashAlg: opts?.hashAlg ?? 'SHA256',
    // SDK_VERIFICATION_PENDING: sample uses "false" string; spec uses form parameter
    XmlDsigForm: opts?.form ?? 'Enveloped',
  });

  const sender = crypto.randomUUID();

  return new Promise<string>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error('vgca_sign_xml timed out after 30s — check that VGCASignService is running'));
    }, SIGN_TIMEOUT_MS);

    vgcaSignXml(sender, params, (_returnedSender: unknown, response: string) => {
      clearTimeout(timer);
      try {
        const parsed = JSON.parse(response) as Record<string, unknown>;
        const status = parsed['Status'];
        const signature = parsed['Signature'];
        const message = parsed['Error'] ?? parsed['Message'] ?? response;

        if (status !== 0 || typeof signature !== 'string') {
          reject(new Error(`vgca_sign_xml failed (Status=${status}): ${message}`));
          return;
        }
        resolve(signature as string);
      } catch {
        reject(new Error(`vgca_sign_xml response parse error: ${response}`));
      }
    });
  });
}

// ─── signJson (W2 Tier-2) ─────────────────────────────────────────────────────

/**
 * Sign JSON content via vgca_sign_json — W2 Tier-2 JWS compact.
 *
 * SDK contract (from vgcaplugin.js + DefaultECC.htm sample):
 *   vgca_sign_json(sender, paramsJson, cb)  — 3-arg
 *   params: { JsonContent, DetachPayload, IsPss }
 *   callback: cb(sender, responseJson)
 *   response success: { Status: 0, Signature: <base64 JWS compact> }
 *   response failure: { Status: <nonzero>, Message: <msg> }
 *
 * SDK_VERIFICATION_PENDING: DefaultECC.htm uses IsPss:"true" string; task spec uses boolean.
 * Using boolean value pending hardware verification.
 *
 * Returns base64-encoded JWS compact bytes (suitable for POST to /digital-signature/verify-jws).
 */
async function signJson(jsonContent: string, opts?: SignJsonOpts): Promise<string> {
  await ensureVgcaPluginLoaded();

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const vgcaSignJson = (window as any)['vgca_sign_json'] as
    | ((sender: unknown, params: string, cb: (sender: unknown, response: string) => void) => void)
    | undefined;

  if (typeof vgcaSignJson !== 'function') {
    throw new Error('vgca_sign_json not available — VGCA plugin not loaded');
  }

  const params = JSON.stringify({
    JsonContent: jsonContent,
    DetachPayload: opts?.detach === true ? 'true' : 'false',
    // SDK_VERIFICATION_PENDING: sample uses string "true"; task spec says boolean
    IsPss: opts?.usePss === true ? 'true' : 'false',
  });

  const sender = crypto.randomUUID();

  return new Promise<string>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error('vgca_sign_json timed out after 30s — check that VGCASignService is running'));
    }, SIGN_TIMEOUT_MS);

    vgcaSignJson(sender, params, (_returnedSender: unknown, response: string) => {
      clearTimeout(timer);
      try {
        const parsed = JSON.parse(response) as Record<string, unknown>;
        const status = parsed['Status'];
        const signature = parsed['Signature'];
        const message = parsed['Message'] ?? parsed['Error'] ?? response;

        if (status !== 0 || typeof signature !== 'string') {
          reject(new Error(`vgca_sign_json failed (Status=${status}): ${message}`));
          return;
        }
        resolve(signature as string);
      } catch {
        reject(new Error(`vgca_sign_json response parse error: ${response}`));
      }
    });
  });
}

// ─── Cert info ────────────────────────────────────────────────────────────────

/**
 * Fetch the end-user's signing certificate info from the VGCA agent.
 * Uses vgca_get_certinfo(cb) — 1-arg, no sender.
 * Response JSON fields (from VGCA SignService backend, not in public docs):
 *   Status, Subject, Issuer, ValidFrom, ValidTo, SerialNumber, CertBase64
 * ValidFrom/ValidTo are in GeneralizedTime format YYYYMMDDHHMMSSZ.
 */
async function getCertInfo(): Promise<CertPreview | null> {
  try {
    await ensureVgcaPluginLoaded();
  } catch {
    return null;
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const vgcaGetCertInfo = (window as any)['vgca_get_certinfo'] as
    | ((cb: (rv: string) => void) => void)
    | undefined;

  if (typeof vgcaGetCertInfo !== 'function') return null;

  return new Promise<CertPreview | null>((resolve) => {
    let settled = false;

    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        resolve(null);
      }
    }, CERT_INFO_TIMEOUT_MS);

    try {
      vgcaGetCertInfo((rv: string) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);

        try {
          const parsed = JSON.parse(rv) as Record<string, unknown>;
          if (parsed['Status'] !== 0) {
            resolve(null);
            return;
          }

          resolve({
            subject: String(parsed['Subject'] ?? ''),
            issuer: String(parsed['Issuer'] ?? ''),
            validFrom: parseGeneralizedTime(String(parsed['ValidFrom'] ?? '')),
            validTo: parseGeneralizedTime(String(parsed['ValidTo'] ?? '')),
            serialNumber: String(parsed['SerialNumber'] ?? ''),
            certBase64: String(parsed['CertBase64'] ?? ''),
          });
        } catch {
          resolve(null);
        }
      });
    } catch {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        resolve(null);
      }
    }
  });
}

/**
 * Parse ASN.1 GeneralizedTime string (YYYYMMDDHHMMSSZ) to a Date.
 * VGCA cert dates use this format in the vgca_get_certinfo callback.
 * Falls back to Date(NaN) on malformed input — callers should guard with isNaN(date.getTime()).
 */
function parseGeneralizedTime(s: string): Date {
  // Format: YYYYMMDDHHMMSSZ  (15 chars + Z)
  if (s.length < 14) return new Date(NaN);
  const year = parseInt(s.slice(0, 4), 10);
  const month = parseInt(s.slice(4, 6), 10) - 1; // Date.UTC months are 0-indexed
  const day = parseInt(s.slice(6, 8), 10);
  const hours = parseInt(s.slice(8, 10), 10);
  const minutes = parseInt(s.slice(10, 12), 10);
  const seconds = parseInt(s.slice(12, 14), 10);
  return new Date(Date.UTC(year, month, day, hours, minutes, seconds));
}

// ─── Register adapter ─────────────────────────────────────────────────────────

registerAdapter('usb-vgca', {
  sign,
  probeAgentStatus: probeVgcaAgent,
  getCertInfo,
  signXml,
  signJson,
  // Tier-3 doc-flow methods (W3)
  signIssued,
  signApproved,
  signIncome,
  comment,
  signAppendix,
  signCopy,
  signFiles,
});
