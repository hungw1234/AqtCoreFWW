// options-editor.tsx — CRUD list for EnumRef static options
// Used inside FieldPropertiesPanel; controlled via Form.List

import { Button, Form, Input, InputNumber, Space, Typography } from 'antd';
import { PlusOutlined, DeleteOutlined, ArrowUpOutlined, ArrowDownOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { FieldOptionPayload } from '../form-types';

const { Title } = Typography;

interface OptionsEditorProps {
  disabled?: boolean;
}

/** Moves item at index by delta (-1 = up, +1 = down) in a Form.List */
function swapItems<T>(list: T[], idx: number, delta: number): T[] {
  const next = [...list];
  const target = idx + delta;
  if (target < 0 || target >= next.length) return next;
  [next[idx], next[target]] = [next[target], next[idx]];
  return next;
}

/**
 * Renders inside a parent <Form> component.
 * Parent form field name: 'options' (array of FieldOptionPayload).
 */
export function OptionsEditor({ disabled }: OptionsEditorProps) {
  const { t } = useTranslation();

  return (
    <>
      <Title level={5} style={{ marginTop: 16, marginBottom: 8 }}>
        {t('forms.builder.properties.optionsSection')}
      </Title>

      <Form.List name="options">
        {(fields, { add, remove }, { errors }) => (
          <>
            {fields.map(({ key, name }, idx) => (
              <Space
                key={key}
                align="start"
                style={{
                  display: 'flex',
                  marginBottom: 6,
                  background: 'var(--ant-color-bg-layout, #f5f5f5)',
                  padding: '6px 8px',
                  borderRadius: 6,
                }}
              >
                {/* Value */}
                <Form.Item
                  name={[name, 'value']}
                  rules={[{ required: true, message: t('forms.builder.options.valueRequired') }]}
                  style={{ marginBottom: 0, width: 90 }}
                >
                  <Input size="small" placeholder={t('forms.builder.options.value')} disabled={disabled} />
                </Form.Item>

                {/* LabelVi */}
                <Form.Item
                  name={[name, 'labelVi']}
                  rules={[{ required: true, message: t('forms.builder.options.labelViRequired') }]}
                  style={{ marginBottom: 0, width: 100 }}
                >
                  <Input size="small" placeholder={t('forms.builder.options.labelVi')} disabled={disabled} />
                </Form.Item>

                {/* LabelEn */}
                <Form.Item
                  name={[name, 'labelEn']}
                  style={{ marginBottom: 0, width: 100 }}
                >
                  <Input size="small" placeholder={t('forms.builder.options.labelEn')} disabled={disabled} />
                </Form.Item>

                {/* DisplayOrder */}
                <Form.Item
                  name={[name, 'displayOrder']}
                  style={{ marginBottom: 0, width: 60 }}
                >
                  <InputNumber size="small" min={0} disabled={disabled} />
                </Form.Item>

                {/* Reorder / delete actions */}
                {!disabled && (
                  <Form.Item noStyle shouldUpdate>
                    {({ getFieldValue, setFieldValue }) => (
                      <Space size={2}>
                        <Button
                          size="small"
                          icon={<ArrowUpOutlined />}
                          disabled={idx === 0}
                          onClick={() => {
                            const opts: FieldOptionPayload[] = getFieldValue('options') ?? [];
                            setFieldValue('options', swapItems(opts, idx, -1));
                          }}
                        />
                        <Button
                          size="small"
                          icon={<ArrowDownOutlined />}
                          disabled={idx === fields.length - 1}
                          onClick={() => {
                            const opts: FieldOptionPayload[] = getFieldValue('options') ?? [];
                            setFieldValue('options', swapItems(opts, idx, 1));
                          }}
                        />
                        <Button
                          size="small"
                          danger
                          icon={<DeleteOutlined />}
                          onClick={() => remove(name)}
                        />
                      </Space>
                    )}
                  </Form.Item>
                )}
              </Space>
            ))}

            {!disabled && (
              <Button
                type="dashed"
                size="small"
                icon={<PlusOutlined />}
                onClick={() =>
                  add({ value: '', labelVi: '', labelEn: '', displayOrder: fields.length })
                }
              >
                {t('forms.builder.options.addOption')}
              </Button>
            )}
            <Form.ErrorList errors={errors} />
          </>
        )}
      </Form.List>
    </>
  );
}
