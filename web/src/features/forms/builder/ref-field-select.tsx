// ref-field-select.tsx — Select with dynamic options from data source API.
// Cascade v1 (spike §2): when the field declares dependencies[], its options are filtered by the
// selected values of its parent fields. It watches those parents, fetches filtered options only when
// ALL parents are set, and clears its own value when a parent changes (clear-propagation — a stale
// child value like "ward of HN" must not survive a switch to province HCM).

import { useEffect, useMemo, useRef } from 'react';
import { Form, Select } from 'antd';
import { useFieldOptions, usePublicFieldOptions } from '../form-api';
import { parseCascadeDependencies, buildParentValues } from '../cascade-dependencies';
import type { FormFieldDto } from '../form-types';

interface RefFieldSelectProps {
  field: FormFieldDto;
  templateId: string;
  value?: string;
  onChange?: (value: string) => void;
  /**
   * When set, resolve options through the ANONYMOUS public path
   * (`GET /public/forms/{publicCode}/fields/{fieldKey}/options`, no Bearer) instead of the authed
   * template path. A public form (anonymous visitor) passes its form code here; the authed builder
   * leaves it undefined and keeps the template-id path unchanged.
   */
  publicCode?: string;
}

export function RefFieldSelect({ field, templateId, value, onChange, publicCode }: RefFieldSelectProps) {
  const deps = useMemo(() => parseCascadeDependencies(field.dataSourceJson), [field.dataSourceJson]);
  const hasDeps = deps.length > 0;

  // Watch all form values (precedent: use-formula-values / form-submit-modal use Form.useWatch([], form)).
  // A cascade child reads only its own parents out of the snapshot; a non-cascade field never uses it.
  const form = Form.useFormInstance();
  const watchedValues = Form.useWatch([], form) as Record<string, unknown> | undefined;

  const { parentValues, allParentsSet } = useMemo(
    () => buildParentValues(deps, watchedValues ?? {}),
    [deps, watchedValues],
  );

  // Clear-propagation: whenever the parent selection changes for a cascade child, drop this field's
  // current value so a stale option (belonging to the previous parent) can't be submitted. Because this
  // field may itself be a parent of a deeper child, clearing here cascades the reset down the chain.
  //
  // First-watch guard: Form.useWatch([], form) is undefined on the first render and only populated on
  // the next. On a form PRE-LOADED with saved parent+child values (edit/draft-resume), that initial
  // undefined→snapshot transition would otherwise look like a "parent change" and wipe the loaded child
  // value. We therefore only SEED the signature on the first settled (non-undefined) watch and start
  // clearing on genuine changes AFTER that. (Blank-start flows are unaffected — snapshot is {} then.)
  const parentSignature = JSON.stringify(parentValues);
  const prevSignature = useRef<string | null>(null);
  useEffect(() => {
    if (!hasDeps) return;
    // Wait for the first settled watch before arming; seed without clearing.
    if (prevSignature.current === null) {
      if (watchedValues !== undefined) prevSignature.current = parentSignature;
      return;
    }
    if (prevSignature.current !== parentSignature) {
      prevSignature.current = parentSignature;
      if (value !== undefined && value !== null && value !== '') {
        onChange?.(undefined as unknown as string);
        form.setFieldValue(field.fieldKey, undefined);
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [parentSignature, hasDeps, watchedValues]);

  // Fetch: for a cascade child, only once all parents are set (avoids an unfiltered full-list fetch).
  const enabled = !hasDeps || allParentsSet;

  // Route selection: a public form (publicCode set) resolves options anonymously by {code}/{fieldKey};
  // the authed builder resolves by {templateId}/{fieldId}. Both hooks are called unconditionally
  // (rules-of-hooks) with mutually-exclusive `enabled`, so only the active path ever fetches.
  const isPublic = Boolean(publicCode);
  const authedQuery = useFieldOptions(
    templateId,
    field.id,
    hasDeps ? parentValues : undefined,
    enabled && !isPublic,
  );
  const publicQuery = usePublicFieldOptions(
    publicCode ?? '',
    field.fieldKey,
    hasDeps ? parentValues : undefined,
    enabled && isPublic,
  );
  const { data: dynamicOptions, isLoading } = isPublic ? publicQuery : authedQuery;

  // Fallback to static options if no dynamic options available
  const options = dynamicOptions?.length
    ? dynamicOptions.map((o) => ({ value: o.value, label: o.labelVi || o.label }))
    : (field.options ?? []).map((o) => ({ value: o.value, label: o.labelVi || o.value }));

  return (
    <Select
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
    />
  );
}
