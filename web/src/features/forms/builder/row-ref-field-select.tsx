// row-ref-field-select.tsx — RefFieldSelect variant for rows inside a TableField widget.
//
// Why a separate component from RefFieldSelect (top-level cascade v1):
//   The v1 RefFieldSelect uses `Form.useWatch([], form)` to read parent values, but a TableField
//   widget keeps its rows in plain React state (not antd Form.List), so those row values are INVISIBLE
//   to Form.useWatch. See lesson `feedback-antd-usewatch-tracks-registered-fields-only`.
//
// Solution: the parent widget builds a per-row `rowValues` dict from its own state and passes it as
// a direct prop. Everything else (options fetching, clear-on-change signature guard, static fallback)
// mirrors RefFieldSelect exactly — copied verbatim so a regression here can only affect in-row.
//
// Clear-propagation is scoped to THIS row instance: onChange(undefined) writes back through the
// parent's `updateCell(rowIdx, key, undefined)` callback, so sibling rows are untouched.

import { useEffect, useMemo, useRef } from 'react';
import { Select } from 'antd';
import { useFieldOptions, usePublicFieldOptions } from '../form-api';
import { parseCascadeDependencies, buildParentValues } from '../cascade-dependencies';
import type { FormFieldDto } from '../form-types';

interface RowRefFieldSelectProps {
  /** The child column field (Ref-typed). */
  field: FormFieldDto;
  /** Template ID for the authed options endpoint (ignored when publicCode is set). */
  templateId: string;
  /**
   * The current row's cell values keyed by the child column's SHORT key (last segment after '.').
   * This is what a cascade child's `dependsOnFieldKey` resolves against — sibling columns in the
   * same row, NOT top-level form fields.
   */
  rowValues: Record<string, unknown>;
  value?: string;
  onChange?: (value: string | undefined) => void;
  /** Anonymous public path when set (public form path); authed builder leaves undefined. */
  publicCode?: string;
}

export function RowRefFieldSelect({
  field,
  templateId,
  rowValues,
  value,
  onChange,
  publicCode,
}: RowRefFieldSelectProps) {
  const deps = useMemo(() => parseCascadeDependencies(field.dataSourceJson), [field.dataSourceJson]);
  const hasDeps = deps.length > 0;

  // Cascade in v1 is row-scoped inside a TableField: `dependsOnFieldKey` refers to a SIBLING column
  // in the same row (validator enforces this at publish time). buildParentValues is dict-generic and
  // does not care whether the source is antd form values or plain row state.
  const { parentValues, allParentsSet } = useMemo(
    () => buildParentValues(deps, rowValues),
    [deps, rowValues],
  );

  // Clear-propagation: when the parent selection in THIS row changes, drop this cell's value so a
  // stale option ("ward of HN" after switching to province HCM) cannot be submitted. First-watch
  // guard mirrors RefFieldSelect: on a form pre-loaded with saved parent+child (edit/draft-resume)
  // the initial signature is seeded WITHOUT clearing; only genuine subsequent changes clear.
  //
  // Row-scope guarantee: onChange(undefined) writes back through the parent widget's per-rowIdx
  // updateCell callback, so sibling rows keep their values untouched.
  const parentSignature = JSON.stringify(parentValues);
  const prevSignature = useRef<string | null>(null);
  useEffect(() => {
    if (!hasDeps) return;
    if (prevSignature.current === null) {
      prevSignature.current = parentSignature;
      return;
    }
    if (prevSignature.current !== parentSignature) {
      prevSignature.current = parentSignature;
      if (value !== undefined && value !== null && value !== '') {
        onChange?.(undefined);
      }
    }
    // Deliberate deps list: only re-run on a genuine parent-signature change. `value` and `onChange`
    // are read via the fresh closure at each render, so omitting them from deps avoids resetting the
    // signature ref on every onChange thrash without losing correctness.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [parentSignature, hasDeps]);

  // Only fetch a filtered set once all parents are set — matches RefFieldSelect / top-level cascade
  // to avoid an unfiltered full-list fetch flashing before the parent is picked.
  const enabled = !hasDeps || allParentsSet;
  const isPublic = Boolean(publicCode);

  // Defensive templateId gate: a legacy caller that omits `templateId` on the authed path would emit
  // GET /templates//fields/... — the router either 404s or, worse, matches a wildcard. Only fire the
  // authed fetch when we actually have a templateId. Public path uses `publicCode` and is unaffected.
  const authedQuery = useFieldOptions(
    templateId,
    field.id,
    hasDeps ? parentValues : undefined,
    enabled && !isPublic && Boolean(templateId),
  );
  const publicQuery = usePublicFieldOptions(
    publicCode ?? '',
    field.fieldKey,
    hasDeps ? parentValues : undefined,
    enabled && isPublic,
  );
  const { data: dynamicOptions, isLoading } = isPublic ? publicQuery : authedQuery;

  // Fallback to static options if the dynamic fetch is empty/disabled — mirrors RefFieldSelect.
  const options = dynamicOptions?.length
    ? dynamicOptions.map((o) => ({ value: o.value, label: o.labelVi || o.label }))
    : (field.options ?? []).map((o) => ({ value: o.value, label: o.labelVi || o.value }));

  return (
    <Select
      size="small"
      value={value}
      onChange={onChange}
      loading={isLoading}
      options={options}
      showSearch
      filterOption={(input, option) =>
        (option?.label as string)?.toLowerCase().includes(input.toLowerCase()) ?? false
      }
      placeholder={field.labelVi || field.fieldKey}
      allowClear
      style={{ minWidth: 140 }}
    />
  );
}
