// enum-parent-filter-map-editor.tsx — keyed-Enum parentFilterMap authoring (P7-FU, review #1).
// EnumRef options have no natural parent key, so the cascade filter is a config-driven map:
//   parentValue → [allowed enum values]
// This edits that map on the field's DataSourceJson. Each row = one parent value + its allowed enum
// values (free tag input, since enum values are admin-known). Empty rows are dropped on serialize;
// an empty map removes the key entirely (EnumRef with deps but no map → BE fail-safe empty list).

import { Select, Input, Button, Typography, Space } from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { getParentFilterMap, withParentFilterMap, type ParentFilterMap } from '../cascade-builder';

const { Text } = Typography;

interface EnumParentFilterMapEditorProps {
  /** Controlled DataSourceJson string. */
  value?: string;
  onChange?: (json: string) => void;
  disabled?: boolean;
}

export function EnumParentFilterMapEditor({ value, onChange, disabled }: EnumParentFilterMapEditorProps) {
  const { t } = useTranslation();
  const map = getParentFilterMap(value);
  const rows = Object.entries(map);

  const emit = (next: ParentFilterMap) => onChange?.(withParentFilterMap(value, next));

  const renameRow = (oldKey: string, newKey: string) => {
    const next: ParentFilterMap = {};
    for (const [k, v] of Object.entries(map)) next[k === oldKey ? newKey : k] = v;
    emit(next);
  };

  const setAllowed = (parentValue: string, allowed: string[]) => {
    emit({ ...map, [parentValue]: allowed });
  };

  const removeRow = (parentValue: string) => {
    const next = { ...map };
    delete next[parentValue];
    emit(next);
  };

  const addRow = () => {
    // Add a placeholder row keyed by an unused name so the admin can type over it.
    let key = 'value';
    let i = 1;
    while (key in map) key = `value${i++}`;
    emit({ ...map, [key]: [] });
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <Text strong style={{ fontSize: 12 }}>
        {t('forms.builder.properties.enumFilterMap', { defaultValue: 'Parent → allowed values map' })}
      </Text>
      <Text type="secondary" style={{ fontSize: 11 }}>
        {t('forms.builder.properties.enumFilterMapHint', {
          defaultValue:
            'For each parent value, list the enum values allowed when the parent has that value. No map ⇒ empty list.',
        })}
      </Text>

      {rows.map(([parentValue, allowed]) => (
        <Space key={parentValue} align="baseline" style={{ display: 'flex' }}>
          <Input
            size="small"
            disabled={disabled}
            style={{ width: 120 }}
            placeholder={t('forms.builder.properties.enumFilterParentValue', { defaultValue: 'parent value' })}
            defaultValue={parentValue}
            onBlur={(e) => {
              const nv = e.target.value.trim();
              if (nv && nv !== parentValue) renameRow(parentValue, nv);
            }}
          />
          <Select
            size="small"
            mode="tags"
            disabled={disabled}
            style={{ minWidth: 180 }}
            placeholder={t('forms.builder.properties.enumFilterAllowed', { defaultValue: 'allowed values' })}
            value={allowed}
            onChange={(vals) => setAllowed(parentValue, vals)}
          />
          <Button
            size="small"
            type="text"
            danger
            disabled={disabled}
            icon={<DeleteOutlined />}
            aria-label="remove-filter-row"
            onClick={() => removeRow(parentValue)}
          />
        </Space>
      ))}

      <Button size="small" type="dashed" disabled={disabled} icon={<PlusOutlined />} onClick={addRow}>
        {t('forms.builder.properties.enumFilterAddRow', { defaultValue: 'Add mapping' })}
      </Button>
    </div>
  );
}
