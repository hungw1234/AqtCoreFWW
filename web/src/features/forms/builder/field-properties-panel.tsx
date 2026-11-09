// field-properties-panel.tsx — right sidebar: edit properties of the selected field
// Save uses useUpdateField (PUT endpoint); falls back to deactivate+re-add if needed

import { useEffect } from 'react';
import {
  Form, Input, Switch, Button, Space, Typography, Divider, theme, Alert, Select,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { FIELD_TYPE_REGISTRY } from './field-type-registry';
import { ValidationRulesEditor } from './validation-rules-editor';
import { OptionsEditor } from './options-editor';
import { VisibilityRulesEditor } from './visibility-rules-editor';
import { RequiredIfEditor } from './required-if-editor';
import { DataSourceEditor } from './data-source-editor';
import type { FormFieldDto, UpdateFieldPayload, FieldOptionPayload, VisibilityRules } from '../form-types';

const { Title, Text } = Typography;

interface FieldPropertiesPanelProps {
  field: FormFieldDto | null;
  templateId: string;
  /** All active fields in template — used by Formula editor for references */
  allFields: FormFieldDto[];
  /** True for non-Draft templates — disables all editing */
  disabled: boolean;
  onSave: (fieldId: string, payload: UpdateFieldPayload) => void;
  onDelete: (fieldId: string) => void;
}

/** Parse validationRulesJson string → object for form initialValues */
function parseValidationRules(json?: string | null): Record<string, unknown> {
  if (!json) return {};
  try { return JSON.parse(json) as Record<string, unknown>; } catch { return {}; }
}

/** Parse visibilityRulesJson → { match, conditions } for Form.List */
function parseVisibilityRules(json?: string | null): VisibilityRules {
  if (!json) return { match: 'all', conditions: [] };
  try { return JSON.parse(json) as VisibilityRules; } catch { return { match: 'all', conditions: [] }; }
}

/** Serialize visibility rules back to JSON string (null if empty) */
function serializeVisibilityRules(rules?: VisibilityRules | null): string | null {
  if (!rules || !rules.conditions?.length) return null;
  return JSON.stringify(rules);
}

/** Serialize validation form values → JSON string */
function serializeValidationRules(rules: Record<string, unknown> | undefined): string | null {
  if (!rules) return null;
  const cleaned = Object.fromEntries(
    Object.entries(rules).filter(([, v]) => v !== null && v !== undefined && v !== '')
  );
  return Object.keys(cleaned).length ? JSON.stringify(cleaned) : null;
}

export function FieldPropertiesPanel({
  field,
  templateId: _templateId,
  allFields,
  disabled,
  onSave,
  onDelete,
}: FieldPropertiesPanelProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const [form] = Form.useForm();

  // Reset form whenever selected field changes
  useEffect(() => {
    if (!field) { form.resetFields(); return; }
    form.setFieldsValue({
      labelVi: field.labelVi,
      labelEn: field.labelEn,
      required: field.required,
      isPii: field.isPii ?? false,
      dataSourceJson: field.dataSourceJson ?? '',
      formulaJson: field.formulaJson ?? '',
      validationRules: parseValidationRules(field.validationRulesJson),
      // DigitalSignature stores its signing mode in the same validationRulesJson envelope.
      signatureMode: (parseValidationRules(field.validationRulesJson).signatureMode as string) ?? 'CanonicalJson',
      options: (field.options ?? []) as FieldOptionPayload[],
      visibilityRules: parseVisibilityRules(field.visibilityRulesJson),
      requiredIf: (() => { try { return field.requiredIfJson ? JSON.parse(field.requiredIfJson) : []; } catch { return []; } })(),
    });
  }, [field?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!field) {
    return (
      <div
        style={{
          width: 320,
          minWidth: 320,
          borderLeft: `1px solid ${token.colorBorderSecondary}`,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 24,
          background: token.colorBgLayout,
        }}
      >
        <Text type="secondary" style={{ textAlign: 'center' }}>
          {t('forms.builder.properties.emptyHint')}
        </Text>
      </div>
    );
  }

  const config = FIELD_TYPE_REGISTRY[field.type];

  async function handleSave() {
    try {
      const values = await form.validateFields();
      const payload: UpdateFieldPayload = {
        labelVi: values.labelVi ?? null,
        labelEn: values.labelEn ?? null,
        required: values.required ?? null,
        isPii: values.isPii ?? null,
        validationRulesJson: config.type === 'DigitalSignature'
          ? JSON.stringify({ signatureMode: (values.signatureMode as string) ?? 'CanonicalJson' })
          : config.hasValidation
            ? serializeValidationRules(values.validationRules as Record<string, unknown>)
            : null,
        dataSourceJson: config.hasDataSource ? (values.dataSourceJson || null) : null,
        formulaJson: config.hasFormula ? (values.formulaJson || null) : null,
        visibilityRulesJson: serializeVisibilityRules(values.visibilityRules as VisibilityRules),
        requiredIfJson: (values.requiredIf as unknown[])?.length
          ? JSON.stringify(values.requiredIf)
          : null,
      };
      if (!field) return;
      onSave(field.id, payload);
    } catch {
      // Ant Design form validation — errors shown inline
    }
  }

  return (
    <div
      style={{
        width: 320,
        minWidth: 320,
        borderLeft: `1px solid ${token.colorBorderSecondary}`,
        overflowY: 'auto',
        padding: '12px 16px',
        background: token.colorBgLayout,
        display: 'flex',
        flexDirection: 'column',
        gap: 0,
      }}
    >
      <Title level={5} style={{ margin: '0 0 4px' }}>
        {t('forms.builder.properties.title')}
      </Title>
      <Text type="secondary" style={{ fontSize: 11 }}>
        {field.type} · <code style={{ fontSize: 10 }}>{field.fieldKey}</code>
      </Text>

      {disabled && (
        <Alert
          type="info"
          banner
          message={t('forms.builder.properties.readOnlyHint')}
          style={{ marginTop: 8 }}
        />
      )}

      <Divider style={{ margin: '10px 0' }} />

      <Form form={form} layout="vertical" size="small" disabled={disabled}>
        {/* Basic section */}
        <Form.Item
          name="labelVi"
          label={t('forms.builder.properties.labelVi')}
          rules={[{ required: true, message: t('forms.builder.properties.labelViRequired') }]}
        >
          <Input />
        </Form.Item>

        <Form.Item
          name="labelEn"
          label={t('forms.builder.properties.labelEn')}
        >
          <Input />
        </Form.Item>

        <Form.Item name="required" label={t('forms.builder.properties.required')} valuePropName="checked">
          <Switch />
        </Form.Item>

        <Form.Item name="isPii" label={t('forms.builder.properties.isPii')} valuePropName="checked">
          <Switch />
        </Form.Item>

        {/* Validation section — conditional by type */}
        {config.hasValidation && (
          <ValidationRulesEditor fieldType={field.type} disabled={disabled} />
        )}

        {/* DigitalSignature signing mode — persisted into validationRulesJson */}
        {field.type === 'DigitalSignature' && (
          <Form.Item
            name="signatureMode"
            label={t('forms.builder.properties.signatureMode', { defaultValue: 'Chế độ ký số' })}
            tooltip={t('forms.builder.properties.signatureModeHint', {
              defaultValue:
                'CanonicalJson: ký trực tiếp dữ liệu biểu mẫu. ServerDocHash: ký mã băm tài liệu do máy chủ tạo.',
            })}
          >
            <Select
              options={[
                { value: 'CanonicalJson', label: 'CanonicalJson' },
                { value: 'ServerDocHash', label: 'ServerDocHash' },
              ]}
            />
          </Form.Item>
        )}

        {/* Options editor — EnumRef only */}
        {config.hasOptions && <OptionsEditor disabled={disabled} />}

        {/* DataSource guided editor — Ref types (replaces raw JSON textarea) */}
        {config.hasDataSource && (
          <>
            <Title level={5} style={{ marginTop: 16, marginBottom: 8 }}>
              {t('forms.builder.properties.dataSourceSection')}
            </Title>
            <Form.Item name="dataSourceJson">
              <DataSourceEditor
                fieldType={field.type}
                field={field}
                allFields={allFields}
                disabled={disabled}
              />
            </Form.Item>
          </>
        )}

        {/* Formula editor */}
        {config.hasFormula && (
          <>
            <Title level={5} style={{ marginTop: 16, marginBottom: 8 }}>
              {t('forms.builder.properties.formulaSection')}
            </Title>
            <Text type="secondary" style={{ fontSize: 11, display: 'block', marginBottom: 6 }}>
              {t('forms.builder.properties.availableFields')}:{' '}
              {allFields
                .filter((f) => f.id !== field.id)
                .map((f) => `[${f.fieldKey}]`)
                .join(', ') || '—'}
            </Text>
            <Form.Item name="formulaJson">
              <Input.TextArea
                rows={4}
                style={{ fontFamily: 'monospace', fontSize: 11 }}
                placeholder="[field1] + [field2]"
              />
            </Form.Item>
          </>
        )}

        {/* Visibility rules — skip for layout-only fields */}
        {!['Section', 'Label', 'Divider'].includes(field.type) && (
          <VisibilityRulesEditor
            form={form}
            otherFields={allFields.filter((f) => f.id !== field.id)}
            disabled={disabled}
          />
        )}

        {/* Required-if conditions — skip for layout-only fields */}
        {!['Section', 'Label', 'Divider'].includes(field.type) && (
          <RequiredIfEditor
            form={form}
            otherFields={allFields.filter(
              (f) => f.id !== field.id && !['Section', 'Label', 'Divider'].includes(f.type)
            )}
            disabled={disabled}
          />
        )}
      </Form>

      {/* Actions */}
      {!disabled && (
        <Space style={{ marginTop: 'auto', paddingTop: 12, justifyContent: 'flex-end' }}>
          <Button size="small" onClick={() => form.resetFields()}>
            {t('common.cancel')}
          </Button>
          <Button
            size="small"
            danger
            onClick={() => onDelete(field.id)}
          >
            {t('common.delete')}
          </Button>
          <Button size="small" type="primary" onClick={handleSave}>
            {t('common.save')}
          </Button>
        </Space>
      )}
    </div>
  );
}
