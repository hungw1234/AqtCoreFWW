/**
 * docflow-usb-meta-form.tsx — DocFlow stamp-metadata form for the USB demo card.
 *
 * Extracted from docflow-usb-card.tsx to keep the card focused on orchestration. These three fields
 * (signType / docNumber / issuedDate) are stamped into the visible VGCA signature and are required by the
 * DocFlow ceremony — mirrors the same form in web/src/features/files/sign-uploaded-file-modal.tsx.
 */

import { DatePicker, Form, Input, Select } from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { DocFlowSignTypeName } from './signature-types';

const SIGN_TYPE_OPTIONS: Array<{ value: DocFlowSignTypeName; labelKey: string; labelFallback: string }> = [
  { value: 'Issued', labelKey: 'digitalSignature.signType.Issued', labelFallback: 'Phát hành' },
  { value: 'Approved', labelKey: 'digitalSignature.signType.Approved', labelFallback: 'Phê duyệt' },
  { value: 'Income', labelKey: 'digitalSignature.signType.Income', labelFallback: 'Công văn đến' },
  { value: 'Comment', labelKey: 'digitalSignature.signType.Comment', labelFallback: 'Góp ý' },
  { value: 'Appendix', labelKey: 'digitalSignature.signType.Appendix', labelFallback: 'Phụ lục' },
  { value: 'Copy', labelKey: 'digitalSignature.signType.Copy', labelFallback: 'Bản sao' },
];

export interface DocFlowUsbMetaFormProps {
  signType: DocFlowSignTypeName;
  docNumber: string;
  issuedDate: Dayjs;
  /** Placeholder for the docNumber field — typically the uploaded file name without extension. */
  docNumberPlaceholder: string;
  /** Disable all fields while a ceremony is in flight. */
  disabled: boolean;
  onSignType: (v: DocFlowSignTypeName) => void;
  onDocNumber: (v: string) => void;
  onIssuedDate: (v: Dayjs) => void;
}

export function DocFlowUsbMetaForm({
  signType,
  docNumber,
  issuedDate,
  docNumberPlaceholder,
  disabled,
  onSignType,
  onDocNumber,
  onIssuedDate,
}: DocFlowUsbMetaFormProps) {
  const { t } = useTranslation();
  return (
    <Form layout="vertical" size="small">
      <Form.Item label={t('digitalSignature.signType', 'Loại ký')} style={{ marginBottom: 8 }}>
        <Select
          value={signType}
          disabled={disabled}
          onChange={(v) => onSignType(v as DocFlowSignTypeName)}
          options={SIGN_TYPE_OPTIONS.map((o) => ({ value: o.value, label: t(o.labelKey, o.labelFallback) }))}
        />
      </Form.Item>
      <Form.Item label={t('digitalSignature.docNumber', 'Số hiệu văn bản')} style={{ marginBottom: 8 }}>
        <Input
          value={docNumber}
          disabled={disabled}
          maxLength={100}
          placeholder={docNumberPlaceholder}
          onChange={(e) => onDocNumber(e.target.value)}
        />
      </Form.Item>
      <Form.Item label={t('digitalSignature.issuedDate', 'Ngày ban hành')} style={{ marginBottom: 0 }}>
        <DatePicker
          value={issuedDate}
          disabled={disabled}
          format="DD/MM/YYYY"
          style={{ width: '100%' }}
          onChange={(v) => onIssuedDate(v ?? issuedDate)}
        />
      </Form.Item>
    </Form>
  );
}
