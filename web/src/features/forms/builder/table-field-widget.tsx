// table-field-widget.tsx — editable row grid for TableField type with child field columns.
//
// M1 Phase-07 (2026-08-12): child columns of type EnumRef/InternalRef/ExternalRef render either
// a plain <Select> (static option-set, `dependencies:[]` empty ⇒ backward path) or the row-scoped
// RowRefFieldSelect variant (cascade: options refetch when the row's parent-column value changes).
// The v1 RefFieldSelect cannot be reused as-is because its Form.useWatch source is blind to plain-
// state rows (see lesson `feedback-antd-usewatch-tracks-registered-fields-only`).

import { useState, useCallback, useEffect, useRef } from 'react';
import { Table, Input, InputNumber, Switch, DatePicker, Select, Button, Space } from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { FormFieldDto } from '../form-types';
import { hasCascadeDependencies } from '../cascade-dependencies';
import { RowRefFieldSelect } from './row-ref-field-select';

const MAX_ROWS = 50;

interface TableFieldWidgetProps {
  field: FormFieldDto;
  allFields: FormFieldDto[];
  value?: Record<string, unknown>[];
  onChange?: (rows: Record<string, unknown>[]) => void;
  disabled?: boolean;
  /** Template ID for authed options endpoint (Ref child columns with cascade). */
  templateId?: string;
  /** Anonymous public path when set (public form); authed builder leaves undefined. */
  publicCode?: string;
}

export function TableFieldWidget({
  field,
  allFields,
  value,
  onChange,
  disabled,
  templateId,
  publicCode,
}: TableFieldWidgetProps) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<Record<string, unknown>[]>(value ?? []);
  const isInternalEdit = useRef(false);

  // Sync with external value changes — skip when we caused the change (F10 fix)
  useEffect(() => {
    if (isInternalEdit.current) {
      isInternalEdit.current = false;
      return;
    }
    if (value) setRows(value);
  }, [value]);

  // Child columns are resolved primarily by the BE-tracked parent FK (`parentFieldId === field.id`)
  // - production path, since `AddFormFieldCommandValidator` forbids dots in `fieldKey`, so the legacy
  // prefix convention `parent.child` never appears in real payloads. The prefix path is kept as a
  // strict fallback for test fixtures that predate the DTO addition and still use dotted keys;
  // production data with real `parentFieldId` never hits it.
  const resolvedChildren = allFields
    .filter((f) => {
      if (!f.isActive) return false;
      // BE Ids serialize as JSON number for `long`; FE type-file declared `id: string` (historical
      // drift). Compare via string coercion so a number `parentFieldId` matches a string `id` without
      // any global refactor of the id shape.
      if (f.parentFieldId != null) return String(f.parentFieldId) === String(field.id);
      // Fallback (legacy fixtures only): fieldKey prefix. Prod data never has a dot here.
      return f.fieldKey.startsWith(`${field.fieldKey}.`);
    })
    .sort((a, b) => a.displayOrder - b.displayOrder);

  const updateCell = useCallback(
    (rowIdx: number, key: string, cellValue: unknown) => {
      setRows((prev) => {
        const updated = [...prev];
        updated[rowIdx] = { ...updated[rowIdx], [key]: cellValue };
        isInternalEdit.current = true;
        onChange?.(updated);
        return updated;
      });
    },
    [onChange]
  );

  const addRow = useCallback(() => {
    if (rows.length >= MAX_ROWS) return;
    const newRow: Record<string, unknown> = {};
    resolvedChildren.forEach((cf) => {
      const key = cf.fieldKey.split('.').pop() ?? cf.fieldKey;
      newRow[key] = null;
    });
    const updated = [...rows, newRow];
    setRows(updated);
    isInternalEdit.current = true;
    onChange?.(updated);
  }, [rows, onChange, resolvedChildren]);

  const removeRow = useCallback(
    (idx: number) => {
      const updated = rows.filter((_, i) => i !== idx);
      setRows(updated);
      isInternalEdit.current = true;
      onChange?.(updated);
    },
    [rows, onChange]
  );

  // Build columns from child fields.
  // H-2: hoist `hasDeps` out of the per-render closure — computed once per column build, not per
  // (row × render). Saves an N×M `JSON.parse(dataSourceJson)` for large tables without changing behavior.
  const dataColumns: ColumnsType<Record<string, unknown>> = resolvedChildren.map((cf) => {
    const key = cf.fieldKey.split('.').pop() ?? cf.fieldKey;
    const hasDeps = hasCascadeDependencies(cf);
    return {
      title: cf.labelVi || key,
      dataIndex: key,
      key,
      render: (val: unknown, _record: Record<string, unknown>, rowIdx: number) => {
        if (disabled) return String(val ?? '');
        switch (cf.type) {
          case 'Number':
            return (
              <InputNumber
                size="small"
                value={val as number}
                onChange={(v) => updateCell(rowIdx, key, v)}
              />
            );
          case 'Date':
            return (
              <DatePicker
                size="small"
                value={val ? dayjs(val as string) : null}
                onChange={(d) => updateCell(rowIdx, key, d?.format('YYYY-MM-DD') ?? null)}
              />
            );
          case 'Boolean':
            return (
              <Switch
                size="small"
                checked={Boolean(val)}
                onChange={(v) => updateCell(rowIdx, key, v)}
              />
            );
          case 'EnumRef':
          case 'InternalRef':
          case 'ExternalRef': {
            // The row-scoped cascade wiring: pass the ENTIRE row dict as parent-value source. A child
            // with `dependencies:[]` reads only its declared parents out of it (buildParentValues does
            // the filtering). Non-cascade Ref child (`dependencies:[]` empty) falls through to a plain
            // Select on static options — backward-safe for tables that never used cascade.
            // `hasDeps` is hoisted above (see H-2 comment on `dataColumns`), not recomputed here.
            if (hasDeps) {
              // Row-scoped clear-on-parent-change comes from RowRefFieldSelect's own onChange callback
              // (writes undefined back through updateCell → sibling rows untouched).
              return (
                <RowRefFieldSelect
                  field={cf}
                  templateId={templateId ?? ''}
                  publicCode={publicCode}
                  rowValues={rows[rowIdx] ?? {}}
                  value={val as string | undefined}
                  onChange={(v) => updateCell(rowIdx, key, v)}
                />
              );
            }
            const staticOptions = (cf.options ?? []).map((o) => ({
              value: o.value,
              label: o.labelVi || o.value,
            }));
            return (
              <Select
                size="small"
                value={val as string | undefined}
                onChange={(v) => updateCell(rowIdx, key, v)}
                options={staticOptions}
                allowClear
                showSearch
                filterOption={(input, option) =>
                  (option?.label as string)?.toLowerCase().includes(input.toLowerCase()) ?? false
                }
                placeholder={cf.labelVi || key}
                style={{ minWidth: 140 }}
              />
            );
          }
          default:
            return (
              <Input
                size="small"
                value={val as string}
                onChange={(e) => updateCell(rowIdx, key, e.target.value)}
              />
            );
        }
      },
    };
  });

  // If no child fields found, show a simple key-value table
  const columns: ColumnsType<Record<string, unknown>> = dataColumns.length
    ? [
        ...dataColumns,
        {
          title: '',
          key: '__actions',
          width: 50,
          render: (_: unknown, __: unknown, rowIdx: number) =>
            !disabled && (
              <Button
                type="text"
                danger
                size="small"
                icon={<DeleteOutlined />}
                onClick={() => removeRow(rowIdx)}
              />
            ),
        },
      ]
    : [
        {
          title: t('forms.table.keyCol'),
          dataIndex: '__key',
          render: (val: unknown, _: unknown, rowIdx: number) =>
            disabled ? (
              String(val ?? '')
            ) : (
              <Input
                size="small"
                value={val as string}
                onChange={(e) => updateCell(rowIdx, '__key', e.target.value)}
              />
            ),
        },
        {
          title: t('forms.table.valueCol'),
          dataIndex: '__value',
          render: (val: unknown, _: unknown, rowIdx: number) =>
            disabled ? (
              String(val ?? '')
            ) : (
              <Input
                size="small"
                value={val as string}
                onChange={(e) => updateCell(rowIdx, '__value', e.target.value)}
              />
            ),
        },
        {
          title: '',
          key: '__actions',
          width: 50,
          render: (_: unknown, __: unknown, rowIdx: number) =>
            !disabled && (
              <Button
                type="text"
                danger
                size="small"
                icon={<DeleteOutlined />}
                onClick={() => removeRow(rowIdx)}
              />
            ),
        },
      ];

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Table
        size="small"
        dataSource={rows.map((r, i) => ({ ...r, __rowKey: i }))}
        rowKey="__rowKey"
        columns={columns}
        pagination={false}
        scroll={{ x: true }}
      />
      {!disabled && (
        <Button
          type="dashed"
          size="small"
          icon={<PlusOutlined />}
          onClick={addRow}
          disabled={rows.length >= MAX_ROWS}
          block
        >
          {t('forms.table.addRow')} {rows.length >= MAX_ROWS && t('forms.table.maxRows', { max: MAX_ROWS })}
        </Button>
      )}
    </Space>
  );
}
