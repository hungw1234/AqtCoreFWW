// visibility-rules-editor.tsx — form sub-editor for field visibility conditions
// Renders inside FieldPropertiesPanel when a field is selected.
// Stores state as a JSON string compatible with VisibilityRules type.

import { Form, Select, Input, Button, Space, Typography, Divider } from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { FormInstance } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FormFieldDto, VisibilityCondition } from '../form-types';

const { Text } = Typography;

const OPERATOR_OPTIONS: { value: VisibilityCondition['operator']; label: string }[] = [
  { value: 'eq',       label: '= equals' },
  { value: 'neq',      label: '≠ not equals' },
  { value: 'gt',       label: '> greater than' },
  { value: 'lt',       label: '< less than' },
  { value: 'contains', label: 'contains' },
  { value: 'empty',    label: 'is empty' },
  { value: 'notEmpty', label: 'is not empty' },
];

/** Operators that don't need a value input */
const NO_VALUE_OPS = new Set<VisibilityCondition['operator']>(['empty', 'notEmpty']);

interface VisibilityRulesEditorProps {
  /** The antd Form instance from parent panel — we nest under "visibilityRules" path */
  form: FormInstance;
  /** All other fields in the template — used to populate the "field" dropdown */
  otherFields: FormFieldDto[];
  disabled?: boolean;
}

export function VisibilityRulesEditor({ form, otherFields, disabled }: VisibilityRulesEditorProps) {
  const { t } = useTranslation();

  // Field options exclude layout-only types
  const fieldOptions = otherFields
    .filter((f) => !['Section', 'Label', 'Divider'].includes(f.type))
    .map((f) => ({ value: f.fieldKey, label: f.labelVi || f.fieldKey }));

  return (
    <>
      <Divider style={{ margin: '12px 0 8px' }} />
      <Text strong style={{ fontSize: 12, display: 'block', marginBottom: 8 }}>
        {t('forms.builder.properties.visibilitySection', { defaultValue: 'Visibility conditions' })}
      </Text>

      {/* Match mode — all / any */}
      <Form.Item
        name={['visibilityRules', 'match']}
        label={t('forms.builder.properties.visibilityMatch', { defaultValue: 'Show when' })}
        initialValue="all"
      >
        <Select
          disabled={disabled}
          size="small"
          options={[
            { value: 'all', label: t('forms.builder.properties.visibilityAll', { defaultValue: 'All conditions match' }) },
            { value: 'any', label: t('forms.builder.properties.visibilityAny', { defaultValue: 'Any condition matches' }) },
          ]}
        />
      </Form.Item>

      {/* Conditions list */}
      <Form.List name={['visibilityRules', 'conditions']}>
        {(conditionFields, { add, remove }) => (
          <>
            {conditionFields.map(({ key, name }) => {
              // Read the current operator to decide whether to show value input
              const op: VisibilityCondition['operator'] =
                form.getFieldValue(['visibilityRules', 'conditions', name, 'operator']);
              const showValue = !NO_VALUE_OPS.has(op);

              return (
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
                      style={{ width: 110 }}
                      disabled={disabled}
                      options={OPERATOR_OPTIONS}
                      onChange={() => {
                        // Force re-render to show/hide value input
                        form.setFieldsValue({});
                      }}
                    />
                  </Form.Item>

                  {/* Value input — hidden for empty/notEmpty */}
                  {showValue && (
                    <Form.Item name={[name, 'value']} noStyle>
                      <Input size="small" placeholder="value" style={{ width: 80 }} disabled={disabled} />
                    </Form.Item>
                  )}

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
              );
            })}

            {!disabled && (
              <Button
                type="dashed"
                size="small"
                icon={<PlusOutlined />}
                onClick={() => add({ fieldKey: '', operator: 'eq', value: '' })}
                style={{ width: '100%', marginTop: 4 }}
              >
                {t('forms.builder.properties.addCondition', { defaultValue: 'Add condition' })}
              </Button>
            )}
          </>
        )}
      </Form.List>
    </>
  );
}
