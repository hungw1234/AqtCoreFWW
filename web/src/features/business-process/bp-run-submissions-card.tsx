// bp-run-submissions-card.tsx — read-only view of the form data already entered in a BP-Run.
// One Descriptions block per form (intake first, then state forms), assembled server-side with
// snapshot labels (GET /business-processes/runs/{id}/submissions, gated BP.View). Distinct from
// FormTabsContainer, which renders BLANK interactive inputs for the current state.

import type { ReactNode } from 'react';
import { Descriptions, Empty, Spin, Typography, Alert, Image, Tag, Tooltip } from 'antd';
import { SafetyCertificateOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { formatDateTime } from '@/shared/utils/format-date';
import { FieldValueWithAttachment } from '@/features/forms/form-attachment-preview';
import { useBpRunSubmissions } from './business-process-run-api';
import type { BpRunSubmission, BpRunSubmissionField } from './business-process-run-api';

const { Text } = Typography;

type TFunc = ReturnType<typeof useTranslation>['t'];

// Compact "digitally signed" indicator. The cryptographic proof is submission-level (one signature
// covers the whole form), so the same badge is reused in the form header and each DigitalSignature cell.
function SignedBadge({ s, t }: { s: BpRunSubmission; t: TFunc }) {
  const verifiedAt = s.signatureVerifiedAt ? formatDateTime(s.signatureVerifiedAt) : null;
  const signer = s.signerName?.trim() || null;
  const label = t('page.businessProcessRun.submissions.signed', { defaultValue: 'Đã ký số' });
  const tag = (
    <Tag icon={<SafetyCertificateOutlined />} color="green" style={{ marginInlineEnd: 0 }}>
      {label}
      {signer ? ` — ${signer}` : ''}
    </Tag>
  );
  const tooltip = [signer, verifiedAt].filter(Boolean).join(' • ');
  return tooltip ? <Tooltip title={tooltip}>{tag}</Tooltip> : tag;
}

interface BpRunSubmissionsCardProps {
  workflowInstanceId: string;
}

// Render a submitted value for display. Values arrive as native CLR → JSON (string/number/bool/
// array/object) since GetFullSubmissionAsync normalises types at the module boundary.
function formatValue(value: unknown, emptyLabel: string): string {
  if (value === null || value === undefined || value === '') return emptyLabel;
  if (typeof value === 'boolean') return value ? '✓' : '✗';
  if (Array.isArray(value)) return value.length ? value.map((v) => String(v)).join(', ') : emptyLabel;
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

// Type-aware read-only rendering. The submitted value alone is ambiguous (a Signature is a base64
// PNG data-URL, a File is a GUID file id) — the snapshot field type disambiguates so values render
// as an image / download link instead of a raw-text dump.
function renderFieldValue(f: BpRunSubmissionField, s: BpRunSubmission, emptyLabel: string, t: TFunc): ReactNode {
  const { type, value } = f;

  // DigitalSignature fields store NO per-field value — the cryptographic proof is submission-level
  // (FormSubmission evidence columns). Surface the verified status here instead of a blank cell.
  if (type === 'DigitalSignature') {
    if (s.isSigned) return <SignedBadge s={s} t={t} />;
    return <Text type="secondary">{emptyLabel}</Text>;
  }

  // Drawn-signature widgets store a PNG data-URL — render the image, not the raw base64 text.
  if (type === 'Signature') {
    if (typeof value === 'string' && value.startsWith('data:image/')) {
      return <Image src={value} alt="" height={96} style={{ objectFit: 'contain', border: '1px solid #eee', borderRadius: 4 }} />;
    }
    return <Text type="secondary">{formatValue(value, emptyLabel)}</Text>;
  }

  // File fields store a file id (or are empty) — reuse the Forms attachment preview (thumbnail / link).
  if (type === 'File') {
    return <FieldValueWithAttachment fieldKey={f.key} value={value} />;
  }

  return <Text>{formatValue(value, emptyLabel)}</Text>;
}

function formLabel(s: BpRunSubmission, t: ReturnType<typeof useTranslation>['t']): string {
  return s.workflowStateId === null
    ? t('page.businessProcessRun.submissions.intakeForm', { defaultValue: 'Biểu mẫu đăng ký' })
    : t('page.businessProcessRun.submissions.stateForm', { defaultValue: 'Biểu mẫu trạng thái' });
}

export function BpRunSubmissionsCard({ workflowInstanceId }: BpRunSubmissionsCardProps) {
  const { t } = useTranslation();
  const { data: submissions, isLoading, isError } = useBpRunSubmissions(workflowInstanceId);

  if (isLoading) {
    return <Spin />;
  }

  if (isError) {
    return (
      <Alert
        type="warning"
        message={t('page.businessProcessRun.submissions.loadError', {
          defaultValue: 'Không tải được dữ liệu biểu mẫu đã nhập.',
        })}
      />
    );
  }

  if (!submissions || submissions.length === 0) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={t('page.businessProcessRun.submissions.none', {
          defaultValue: 'Chưa có biểu mẫu nào được nhập',
        })}
      />
    );
  }

  const emptyLabel = t('page.businessProcessRun.submissions.emptyValue', { defaultValue: '—' });

  return (
    <>
      {submissions.map((s) => (
        <div key={`${s.formTemplatePublicId}-${s.workflowStateId ?? 'intake'}`} style={{ marginBottom: 20 }}>
          <Descriptions
            title={
              <span>
                {formLabel(s, t)}{' '}
                <Text type="secondary" style={{ fontSize: 12, fontWeight: 'normal', marginInlineStart: 8 }}>
                  {formatDateTime(s.submittedAt)}
                </Text>
                {s.isSigned ? (
                  <span style={{ marginInlineStart: 8 }}>
                    <SignedBadge s={s} t={t} />
                  </span>
                ) : null}
              </span>
            }
            bordered
            size="small"
            column={{ xs: 1, sm: 1, md: 2 }}
          >
            {s.fields.length === 0 ? (
              <Descriptions.Item
                label={t('page.businessProcessRun.submissions.noFields', { defaultValue: 'Trường' })}
              >
                <Text type="secondary">{emptyLabel}</Text>
              </Descriptions.Item>
            ) : (
              s.fields.map((f: BpRunSubmissionField) => (
                <Descriptions.Item key={f.key} label={f.labelVi || f.labelEn || f.key}>
                  {renderFieldValue(f, s, emptyLabel, t)}
                </Descriptions.Item>
              ))
            )}
          </Descriptions>
        </div>
      ))}
    </>
  );
}
