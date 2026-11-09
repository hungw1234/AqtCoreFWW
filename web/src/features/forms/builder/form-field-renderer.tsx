// form-field-renderer.tsx — maps each FormFieldType to its interactive preview widget.
// Pure switch orchestrator: every field-type widget lives in its own file under builder/.

import type { ReactNode } from 'react';
import { Form, Input, InputNumber, Switch, Select, DatePicker, Typography, Divider } from 'antd';
import type { Rule } from 'antd/es/form';
import type { TFunction } from 'i18next';
import type { FormFieldDto } from '../form-types';
import { RefFieldSelect } from './ref-field-select';
import { hasCascadeDependencies } from '../cascade-dependencies';
import { AddressFieldWidget } from './address-field-widget';
import { RichTextWidget } from './rich-text-widget';
import { SignatureWidget } from '@/shared/components/signature-widget';
import { DigitalSignatureWidget } from './digital-signature-widget';
import { TableFieldWidget } from './table-field-widget';
import { FileFieldWidget } from './file-field-widget';
import type { SigningProviderInfo } from '@/features/digital-signature/signature-types';

const { Title, Text } = Typography;

export interface RenderFieldProps {
  field: FormFieldDto;
  /** Called when a File field completes upload */
  onFileUploaded?: (fieldKey: string, fileId: string) => void;
  /** Template ID for dynamic options (Ref fields) */
  templateId?: string;
  /** All template fields — needed for TableField child columns */
  allFields?: FormFieldDto[];
  /** Extra validation rules merged with default required rule */
  extraRules?: Rule[];
  /** Active signing provider info — required for DigitalSignature fields; null disables signing. */
  signingProvider?: SigningProviderInfo | null;
  /** BE-computed Formula values keyed by fieldKey (display-only). Undefined in builder preview. */
  computedValues?: Record<string, unknown>;
  /**
   * When set, reference fields (EnumRef/InternalRef/ExternalRef) resolve their options through the
   * ANONYMOUS public path keyed by this form code, instead of the authed template path. The public form
   * renderer passes its code; the authed builder/preview leaves it undefined (path unchanged).
   */
  publicCode?: string;
  /** Translation function — callers must pass their own t from useTranslation() */
  t: TFunction;
}

/** Returns an interactive Ant Design widget for the given field */
export function renderInteractiveField(
  { field, onFileUploaded, templateId, allFields, extraRules, signingProvider, computedValues, publicCode, t }: RenderFieldProps
): ReactNode {
  // Defense-in-depth: a TableField child column (parentFieldId != null) rendered as a top-level
  // Form.Item would double-render (also rendered inside TableFieldWidget's per-row Table) AND
  // collide on antd Form.Item `name`. The 3 top-level renderers filter `activeFields` upstream; this
  // early-return catches any future caller that forgets. Legacy fixtures without parentFieldId pass
  // through unchanged.
  if (field.parentFieldId != null) return null;

  const label = field.labelVi || field.fieldKey;
  const required = field.required;

  // Build rules array: required rule + any extra rules from caller
  const rules: Rule[] = [];
  if (required) rules.push({ required: true, message: t('forms.field.requiredMsg', { label }) });
  if (extraRules?.length) rules.push(...extraRules);

  switch (field.type) {
    case 'Text':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <Input />
        </Form.Item>
      );
    case 'Number':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <InputNumber style={{ width: '100%' }} />
        </Form.Item>
      );
    case 'Date':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <DatePicker style={{ width: '100%' }} />
        </Form.Item>
      );
    case 'DateRange':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <DatePicker.RangePicker style={{ width: '100%' }} />
        </Form.Item>
      );
    case 'Textarea':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <Input.TextArea rows={3} />
        </Form.Item>
      );
    case 'RichText':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <RichTextWidget />
        </Form.Item>
      );
    case 'Boolean':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} valuePropName="checked">
          <Switch />
        </Form.Item>
      );
    case 'File':
      return (
        <FileFieldWidget
          key={field.id}
          field={field}
          onUploaded={(fileId) => onFileUploaded?.(field.fieldKey, fileId)}
          rules={rules}
        />
      );
    case 'Signature':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <SignatureWidget />
        </Form.Item>
      );
    case 'DigitalSignature':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <DigitalSignatureWidget
            field={field}
            allFields={allFields ?? []}
            templateId={templateId ?? ''}
            signingProvider={signingProvider}
          />
        </Form.Item>
      );
    case 'EnumRef':
      // Cascade v1: an EnumRef declaring dependencies[] must resolve its options through the dynamic
      // path (RefFieldSelect handles watch/refetch/clear + the keyed-enum parentFilterMap on the BE).
      // A plain EnumRef with no dependencies keeps the cheap inline static Select — no behavior change.
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          {hasCascadeDependencies(field) ? (
            <RefFieldSelect field={field} templateId={templateId ?? ''} publicCode={publicCode} />
          ) : (
            <Select
              options={(field.options ?? []).map((o) => ({
                value: o.value,
                label: o.labelVi || o.value,
              }))}
            />
          )}
        </Form.Item>
      );
    case 'InternalRef':
    case 'ExternalRef':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <RefFieldSelect field={field} templateId={templateId ?? ''} publicCode={publicCode} />
        </Form.Item>
      );
    case 'Formula':
      // Display-only, and deliberately NOT bound via `name`: an antd Form.Item with `name` becomes
      // the value source and overrides a child's `value=` prop, so the BE-computed value would never
      // show. Formula is already stripped from submit data by cleanFormData (UI_ONLY_TYPES) and the
      // BE re-computes at submit-time — so binding contributes nothing but a feedback-loop surface.
      // Rendering the value ONLY through `value=` (never form.setFieldValue) means it can never feed
      // the useWatch→recompute loop. Builder preview passes no computedValues → same "(computed)".
      return (
        <Form.Item key={field.id} label={label}>
          <Input
            disabled
            value={
              computedValues?.[field.fieldKey] != null
                ? String(computedValues[field.fieldKey])
                : ''
            }
            placeholder="(computed)"
          />
        </Form.Item>
      );
    case 'TableField':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label}>
          <TableFieldWidget
            field={field}
            allFields={allFields ?? []}
            templateId={templateId}
            publicCode={publicCode}
          />
        </Form.Item>
      );
    case 'AddressField':
      return (
        <Form.Item key={field.id} name={field.fieldKey} label={label} rules={rules}>
          <AddressFieldWidget />
        </Form.Item>
      );
    case 'Section':
      return (
        <Divider key={field.id} orientation="left">
          <Title level={5} style={{ margin: 0 }}>{label}</Title>
        </Divider>
      );
    case 'Label':
      return (
        <div key={field.id} style={{ marginBottom: 12 }}>
          <Text>{label}</Text>
        </div>
      );
    case 'Divider':
      return <Divider key={field.id} />;
    default:
      return null;
  }
}
