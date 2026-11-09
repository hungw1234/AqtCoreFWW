// required-if-editor.tsx — form sub-editor for conditional required rules
// A field becomes required when ALL listed conditions are satisfied (implicit AND).
// Mirrors the structure of visibility-rules-editor.tsx but without match-mode selector.

import { Form, Select, Input, Button, Space, Typography, Divider } from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { FormInstance } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FormFieldDto } from '../form-types';

const { Text } = Typography;

/** Operators supported for required-if conditions — includes gte/lte/in vs visibility */
const OPERATORS = [
  { value: 'eq',       label: '= equals' },
  { value: 'neq',      label: '≠ not equals' },
  { value: 'gt',       label: '> greater than' },
  { value: 'lt',       label: '< less than' },
  { value: 'gte',      label: '≥ greater or equal' },
  { value: 'lte',      label: '≤ less or equal' },
  { value: 'contains', label: 'contains' },
  { value: 'in',       label: 'in (comma-separated)' },
];

// All operators require a value — no NO_VALUE_OPS for required-if conditions

interface RequiredIfEditorProps {
  /** The antd Form instance from parent panel — conditions stored under "requiredIf" path */
  form: FormInstance;
  /** Other fields available as condition sources — layout-only types already excluded by caller */
  otherFields: FormFieldDto[];
  disabled: boolean;
}

export function RequiredIfEditor({ form: _form, otherFields, disabled }: RequiredIfEditorProps) {
  const { t } = useTranslation();

  const fieldOptions = otherFields.map((f) => ({
    value: f.fieldKey,
    label: f.labelVi || f.fieldKey,
  }));

  return (
    <>
      <Divider style={{ margin: '12px 0 8px' }} />
      <Text strong style={{ fontSize: 12, display: 'block', marginBottom: 8 }}>
        {t('forms.builder.properties.requiredIfSection', { defaultValue: 'Conditional required' })}
      </Text>
      <Text type="secondary" style={{ fontSize: 11, display: 'block', marginBottom: 8 }}>
        {t('forms.builder.properties.requiredIfHint', {
          defaultValue: 'Field becomes required when ALL conditions below are met.',
        })}
      </Text>

      {/* Conditions list — stored as a flat array (no match-mode wrapper) */}
      <Form.List name={['requiredIf']}>
        {(conditionFields, { add, remove }) => (
          <>
            {conditionFields.map(({ key, name }) => (
              <Space key={key} align="start" size={4} style={{ display: 'flex', marginBottom: 6 }}>
                {/* Field selector */}
                <Form.Item name={[name, 'fieldKey']} noStyle rules={[{ required: true }]}>
                  <Select
                    size="small"
                    placeholder="field"
                    style={{ width: 100 }}
                    disabled={disabled}
                    options={fieldOptions}
                  />
                </Form.Item>

                {/* Operator selector */}
                <Form.Item name={[name, 'operator']} noStyle rules={[{ required: true }]} initialValue="eq">
                  <Select
                    size="small"
                    style={{ width: 130 }}
                    disabled={disabled}
                    options={OPERATORS}
                  />
                </Form.Item>

                {/* Value input — all operators require a value */}
                <Form.Item name={[name, 'value']} noStyle rules={[{ required: true, message: ' ' }]}>
                  <Input size="small" placeholder="value" style={{ width: 80 }} disabled={disabled} />
                </Form.Item>

                {!disabled && (
                  <Button
                    type="text"
                    danger
                    size="small"
                    icon={<DeleteOutlined />}
                    onClick={() => remove(name)}
                  />
                )}
              </Space>
            ))}

            {!disabled && (
              <Button
                type="dashed"
                size="small"
                icon={<PlusOutlined />}
                onClick={() => add({ fieldKey: '', operator: 'eq', value: '' })}
                style={{ width: '100%', marginTop: 4 }}
              >
                {t('forms.builder.properties.addRequiredIfCondition', { defaultValue: 'Add condition' })}
              </Button>
            )}
          </>
        )}
      </Form.List>
    </>
  );
}
