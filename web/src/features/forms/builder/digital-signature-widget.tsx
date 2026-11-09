/**
 * digital-signature-widget.tsx — interactive widget for the DigitalSignature field type.
 *
 * Distinct from signature-widget.tsx (the PNG hand-drawing for the legacy `Signature` field).
 * This widget collects a CRYPTOGRAPHIC signature: it computes the payload per mode, opens the
 * reusable <SigningPrompt>, and on success stores a SignaturePayloadRequest in antd form state
 * (its own field value). The submit handler lifts that out as the body.signature sibling.
 *
 *  Mode CanonicalJson : payload = utf8(canonicalizeMode1(currentValues, fields))  — byte-identical to BE.
 *  Mode ServerDocHash : payload = base64ToUint8( fetchSignatureDocHash(templateId, currentValues) ).
 *
 * Editing any covered field after signing clears the captured signature (re-hash would differ →
 * BE would reject), forcing a re-sign — friendlier than a silent 400 at submit.
 *
 * FE never decides validity; it only collects. BE re-canonicalizes + verifies at ingest.
 */

import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Form, Space, Tag, Typography } from 'antd';
import { SafetyCertificateOutlined, CheckCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { SigningPrompt } from '@/features/digital-signature/signing-prompt';
import { canonicalizeMode1 } from '@/features/digital-signature/form-data-canonicalizer';
import type { CanonicalField } from '@/features/digital-signature/form-data-canonicalizer';
import { fetchSignatureDocHash } from '@/features/digital-signature/forms-signature-doc-hash-api';
import { base64ToUint8 } from '@/features/digital-signature/base64-utils';
import type { SignResult, SigningProviderInfo } from '@/features/digital-signature/signature-types';
import type { FormFieldDto, SignatureMode, SignaturePayloadRequest } from '../form-types';

const { Text } = Typography;

export interface DigitalSignatureWidgetProps {
  field: FormFieldDto;
  /** All template fields — drives covered-field selection for canonicalization. */
  allFields: FormFieldDto[];
  /** FormTemplate.PublicId — needed for the Mode-2 server doc-hash fetch. */
  templateId: string;
  /** Active provider info (null for public/anonymous or disabled tenant → signing unavailable). */
  signingProvider: SigningProviderInfo | null | undefined;
  /** antd Form-injected control value/onChange. */
  value?: SignaturePayloadRequest | null;
  onChange?: (v: SignaturePayloadRequest | null) => void;
}

/** Read the signing mode from the field's validationRulesJson envelope. Default = CanonicalJson. */
function parseMode(validationRulesJson?: string | null): SignatureMode {
  if (!validationRulesJson) return 'CanonicalJson';
  try {
    const parsed = JSON.parse(validationRulesJson) as { signatureMode?: unknown };
    return parsed?.signatureMode === 'ServerDocHash' ? 'ServerDocHash' : 'CanonicalJson';
  } catch {
    return 'CanonicalJson';
  }
}

const toCanonicalFields = (fields: FormFieldDto[]): CanonicalField[] =>
  fields.map((f) => ({ fieldKey: f.fieldKey, type: f.type, isActive: f.isActive }));

/**
 * Snapshot the covered form data as the canonical Mode-1 string. Used both to detect
 * covered-value edits (invalidate-on-change) and as the Mode-1 payload source. Values are
 * JSON-normalized first so antd value types (dayjs dates) collapse to the same form the BE sees.
 */
function coveredSnapshot(values: Record<string, unknown>, fields: FormFieldDto[]): string {
  const normalized = JSON.parse(JSON.stringify(values ?? {})) as Record<string, unknown>;
  return new TextDecoder().decode(canonicalizeMode1(normalized, toCanonicalFields(fields)));
}

export function DigitalSignatureWidget({
  field,
  allFields,
  templateId,
  signingProvider,
  value,
  onChange,
}: DigitalSignatureWidgetProps) {
  const { t } = useTranslation();
  const form = Form.useFormInstance();
  const allValues = (Form.useWatch([], form) ?? {}) as Record<string, unknown>;

  const mode = parseMode(field.validationRulesJson);
  const providerKey = signingProvider?.isEnabled ? signingProvider.providerKey : null;
  const capabilities = signingProvider?.capabilities ?? '';

  const [signingOpen, setSigningOpen] = useState(false);
  const [payloadBytes, setPayloadBytes] = useState<Uint8Array | null>(null);
  const [preparing, setPreparing] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  // Canonical snapshot captured at sign time — when current data diverges, the signature is stale.
  const signedSnapshot = useRef<string | null>(null);

  const currentSnapshot = coveredSnapshot(allValues, allFields);

  // Invalidate a captured signature once any covered field changes (re-hash would differ → reject).
  useEffect(() => {
    if (value && signedSnapshot.current !== null && signedSnapshot.current !== currentSnapshot) {
      signedSnapshot.current = null;
      onChange?.(null);
    }
  }, [currentSnapshot, value, onChange]);

  async function handleOpenSigning() {
    if (!providerKey) return;
    setErrorMsg(null);
    setPreparing(true);
    try {
      if (mode === 'ServerDocHash') {
        const { hashBase64 } = await fetchSignatureDocHash(
          templateId,
          JSON.parse(JSON.stringify(allValues ?? {})) as Record<string, unknown>,
        );
        setPayloadBytes(base64ToUint8(hashBase64));
      } else {
        const normalized = JSON.parse(JSON.stringify(allValues ?? {})) as Record<string, unknown>;
        setPayloadBytes(canonicalizeMode1(normalized, toCanonicalFields(allFields)));
      }
      setSigningOpen(true);
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : t('digitalSignature.prepareFailed', 'Không chuẩn bị được dữ liệu ký'));
    } finally {
      setPreparing(false);
    }
  }

  function handleSignSuccess(result: SignResult) {
    // Raw layout when the provider returns the signer cert separately (VGCA USB / Viettel);
    // otherwise the signature is a self-contained CMS envelope.
    const isRaw = !!result.signerCertBase64;
    onChange?.({
      signatureBase64: result.signatureBase64,
      signerCertBase64: result.signerCertBase64 ?? null,
      format: isRaw ? 'Raw' : 'Pkcs7',
      hashAlg: result.hashAlg,
      encode: result.encode,
      providerKey: providerKey ?? result.providerKey,
    });
    // Bind the captured signature to the data it was signed over.
    signedSnapshot.current = coveredSnapshot(allValues, allFields);
    setSigningOpen(false);
  }

  if (!providerKey) {
    // Two distinct dead-ends, two distinct messages:
    //  - signingProvider present but disabled/no key = authenticated tenant whose admin
    //    hasn't configured a provider → actionable: contact the admin (warning).
    //  - signingProvider absent (null/undefined) = public/anonymous form (or builder
    //    preview) where signing isn't offered at all → informational only.
    const tenantUnconfigured = signingProvider != null;
    return tenantUnconfigured ? (
      <Alert
        type="warning"
        showIcon
        message={t('digitalSignature.unavailable', 'Ký số chưa khả dụng')}
        description={t(
          'digitalSignature.unavailableContactAdmin',
          'Tổ chức chưa cấu hình nhà cung cấp chữ ký số. Vui lòng liên hệ quản trị viên để bật ký số.',
        )}
      />
    ) : (
      <Alert
        type="info"
        showIcon
        message={t('digitalSignature.unavailable', 'Ký số chưa khả dụng')}
        description={t(
          'digitalSignature.unavailablePublicForm',
          'Biểu mẫu công khai không hỗ trợ ký số.',
        )}
      />
    );
  }

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {value ? (
        <Space>
          <CheckCircleOutlined style={{ color: '#52c41a' }} />
          <Text type="success">{t('digitalSignature.signed', 'Đã ký số')}</Text>
          <Tag color="blue">{value.providerKey ?? providerKey}</Tag>
        </Space>
      ) : (
        <Button
          icon={<SafetyCertificateOutlined />}
          onClick={handleOpenSigning}
          loading={preparing}
        >
          {t('digitalSignature.sign', 'Ký số')}
        </Button>
      )}

      {errorMsg && <Alert type="error" showIcon message={errorMsg} />}

      <SigningPrompt
        open={signingOpen}
        providerKey={providerKey}
        capabilities={capabilities}
        payload={payloadBytes}
        reason={field.labelVi || field.fieldKey}
        onSuccess={handleSignSuccess}
        onCancel={() => setSigningOpen(false)}
      />
    </Space>
  );
}
