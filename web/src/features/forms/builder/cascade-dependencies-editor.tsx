// cascade-dependencies-editor.tsx — admin UI to configure a field's cascade dependencies (P7).
// Each row maps a PARENT field (picked from eligible earlier fields) to a filterParamName. Persists
// into DataSourceJson.dependencies via the controlled value/onChange (JSON string) contract. Shows a
// SOFT client-side cycle warning; the BE publish gate (P2) is the authoritative reject.

import { useMemo } from 'react';
import { Select, Input, Button, Typography, Alert, Space, Divider } from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  getDependencies,
  withDependencies,
  upsertDependency,
  removeDependency,
  wouldCreateCycle,
  buildAdjacency,
} from '../cascade-builder';
import { EnumParentFilterMapEditor } from './enum-parent-filter-map-editor';
import type { FormFieldDto } from '../form-types';

const { Text } = Typography;

interface CascadeDependenciesEditorProps {
  /** This field (the cascade CHILD being configured). */
  field: FormFieldDto;
  /** All template fields — the parent picker lists the others. */
  allFields: FormFieldDto[];
  /** Controlled DataSourceJson string. */
  value?: string;
  onChange?: (json: string) => void;
  disabled?: boolean;
}

export function CascadeDependenciesEditor({
  field,
  allFields,
  value,
  onChange,
  disabled,
}: CascadeDependenciesEditorProps) {
  const { t } = useTranslation();
  const deps = getDependencies(value);

  // Eligible parents: any OTHER field (BE enforces order/existence; picker just excludes self).
  const parentOptions = useMemo(
    () =>
      allFields
        .filter((f) => f.id !== field.id)
        .map((f) => ({ value: f.fieldKey, label: `${f.labelVi || f.fieldKey} (${f.fieldKey})` })),
    [allFields, field.id],
  );

  // Soft cycle check: build adjacency from the whole template + the edits currently held on THIS field.
  const cycleParent = useMemo(() => {
    const others = allFields.filter((f) => f.id !== field.id);
    const adjacency = buildAdjacency([
      ...others,
      { fieldKey: field.fieldKey, dataSourceJson: withDependencies(value, deps) },
    ]);
    return deps.find((d) => wouldCreateCycle(adjacency, field.fieldKey, d.dependsOnFieldKey))
      ?.dependsOnFieldKey;
  }, [allFields, field.id, field.fieldKey, deps, value]);

  const emit = (nextDeps: typeof deps) => onChange?.(withDependencies(value, nextDeps));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <Text strong style={{ fontSize: 12 }}>
        {t('forms.builder.properties.cascadeDependencies', { defaultValue: 'Cascade dependencies' })}
      </Text>
      <Text type="secondary" style={{ fontSize: 11 }}>
        {t('forms.builder.properties.cascadeDependenciesHint', {
          defaultValue:
            "This field's options are filtered by the selected value of each parent field below.",
        })}
      </Text>

      {cycleParent && (
        <Alert
          type="warning"
          showIcon
          message={t('forms.builder.properties.cascadeCycleWarning', {
            defaultValue: `Dependency on "${cycleParent}" would create a cycle — publishing will be rejected.`,
          })}
        />
      )}

      {deps.map((dep) => (
        <Space key={dep.dependsOnFieldKey} align="baseline" style={{ display: 'flex' }}>
          <Select
            size="small"
            disabled={disabled}
            style={{ minWidth: 160 }}
            value={dep.dependsOnFieldKey}
            options={parentOptions}
            onChange={(newParent) => {
              // Rename the parent of this row: remove old, upsert new (keep the param name).
              const withoutOld = removeDependency(deps, dep.dependsOnFieldKey);
              emit(upsertDependency(withoutOld, {
                dependsOnFieldKey: newParent,
                filterParamName: dep.filterParamName,
              }));
            }}
          />
          <Input
            size="small"
            disabled={disabled}
            style={{ width: 140 }}
            placeholder={t('forms.builder.properties.cascadeParamName', { defaultValue: 'param name' })}
            value={dep.filterParamName}
            onChange={(e) =>
              emit(upsertDependency(deps, {
                dependsOnFieldKey: dep.dependsOnFieldKey,
                filterParamName: e.target.value,
              }))
            }
          />
          <Button
            size="small"
            type="text"
            danger
            disabled={disabled}
            icon={<DeleteOutlined />}
            aria-label="remove-dependency"
            onClick={() => emit(removeDependency(deps, dep.dependsOnFieldKey))}
          />
        </Space>
      ))}

      <Button
        size="small"
        type="dashed"
        disabled={disabled || parentOptions.length === 0}
        icon={<PlusOutlined />}
        onClick={() => {
          // Add a row for the first not-yet-used parent.
          const used = new Set(deps.map((d) => d.dependsOnFieldKey));
          const nextParent = parentOptions.find((o) => !used.has(o.value));
          if (nextParent) {
            emit(upsertDependency(deps, {
              dependsOnFieldKey: nextParent.value,
              filterParamName: nextParent.value,
            }));
          }
        }}
      >
        {t('forms.builder.properties.cascadeAddDependency', { defaultValue: 'Add dependency' })}
      </Button>

      {/* keyed-Enum: EnumRef options have no natural parent key → author the parentValue→[allowed values]
          map here (P7-FU). Only shown for EnumRef with ≥1 dependency; External/Internal filter server-side. */}
      {field.type === 'EnumRef' && deps.length > 0 && (
        <>
          <Divider style={{ margin: '8px 0' }} />
          <EnumParentFilterMapEditor value={value} onChange={onChange} disabled={disabled} />
        </>
      )}
    </div>
  );
}
