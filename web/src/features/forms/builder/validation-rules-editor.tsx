// validation-rules-editor.tsx — guided sub-form for field validation rules
// Renders type-specific inputs; parent form serializes to validationRulesJson on save

import { Form, InputNumber, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FormFieldType } from '../form-types';

const { Title } = Typography;

interface ValidationRulesEditorProps {
  fieldType: FormFieldType;
  disabled?: boolean;
}

/**
 * Renders validation inputs inside an Ant Design Form context.
 * Form.Item names follow the flat structure that gets serialized to JSON:
 *   { minLength, maxLength, min, max, pattern }
 */
export function ValidationRulesEditor({ fieldType, disabled }: ValidationRulesEditorProps) {
  const { t } = useTranslation();

  const showLength = fieldType === 'Text' || fieldType === 'Textarea';
  const showNumRange = fieldType === 'Number';
  const showDateRange = fieldType === 'Date';
  const showPattern = fieldType === 'Text';
  const showFileRules = fieldType === 'File';

  if (!showLength && !showNumRange && !showDateRange && !showPattern && !showFileRules) {
    return null;
  }

  return (
    <>
      <Title level={5} style={{ marginTop: 16, marginBottom: 8 }}>
        {t('forms.builder.properties.validationSection')}
      </Title>

      {showLength && (
        <>
          <Form.Item
            name={['validationRules', 'minLength']}
            label={t('forms.builder.validation.minLength')}
          >
            <InputNumber min={0} style={{ width: '100%' }} disabled={disabled} />
          </Form.Item>
          <Form.Item
            name={['validationRules', 'maxLength']}
            label={t('forms.builder.validation.maxLength')}
          >
            <InputNumber min={0} style={{ width: '100%' }} disabled={disabled} />
          </Form.Item>
        </>
      )}

      {(showNumRange || showDateRange) && (
        <>
          <Form.Item
            name={['validationRules', 'min']}
            label={t('forms.builder.validation.min')}
          >
            <InputNumber style={{ width: '100%' }} disabled={disabled} />
          </Form.Item>
          <Form.Item
            name={['validationRules', 'max']}
            label={t('forms.builder.validation.max')}
          >
            <InputNumber style={{ width: '100%' }} disabled={disabled} />
          </Form.Item>
        </>
      )}

      {showPattern && (
        <Form.Item
          name={['validationRules', 'pattern']}
          label={t('forms.builder.validation.pattern')}
          rules={[
            {
              validator: (_, val) => {
                if (!val) return Promise.resolve();
                try {
                  new RegExp(val);
                  return Promise.resolve();
                } catch {
                  return Promise.reject(t('forms.builder.validation.patternInvalid'));
                }
              },
            },
          ]}
        >
          <Input
            placeholder="^[A-Z].*"
            style={{ fontFamily: 'monospace' }}
            disabled={disabled}
          />
        </Form.Item>
      )}

      {showFileRules && (
        <>
          <Form.Item
            name={['validationRules', 'maxSize']}
            label={t('forms.builder.validation.maxSizeMb')}
          >
            <InputNumber min={0} addonAfter="MB" style={{ width: '100%' }} disabled={disabled} />
          </Form.Item>
          <Form.Item
            name={['validationRules', 'allowedExtensions']}
            label={t('forms.builder.validation.allowedExtensions')}
          >
            <Input placeholder=".pdf,.docx" disabled={disabled} />
          </Form.Item>
        </>
      )}
    </>
  );
}
