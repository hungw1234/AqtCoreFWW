// data-source-editor.tsx — guided form replacing raw JSON textarea for DataSource config
// Renders different fields based on fieldType: InternalRef vs ExternalRef.
// Value/onChange interface matches Form.Item controlled component contract (string JSON).

import { Form, Select, Input, InputNumber, Typography, Divider } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FormFieldType, FormFieldDto } from '../form-types';
import { CascadeDependenciesEditor } from './cascade-dependencies-editor';

const { Text } = Typography;

/** Internal module options — maps to backend service identifiers */
const INTERNAL_MODULE_OPTIONS = [
  { value: 'identity',     label: 'Identity' },
  { value: 'organization', label: 'Organization' },
  { value: 'masterdata',   label: 'Master Data' },
  { value: 'cases',        label: 'Cases' },
];

const HTTP_METHOD_OPTIONS = [
  { value: 'GET',  label: 'GET' },
  { value: 'POST', label: 'POST' },
];

interface DataSourceEditorProps {
  fieldType: FormFieldType;
  /** Controlled value — JSON string */
  value?: string;
  /** Emit updated JSON string on any field change */
  onChange?: (json: string) => void;
  disabled?: boolean;
  /** Cascade v1: the field being edited + all template fields, for the dependencies editor. */
  field?: FormFieldDto;
  allFields?: FormFieldDto[];
}

/** Parse the JSON string into an object, returning empty object on failure */
function parseJson(value?: string): Record<string, unknown> {
  if (!value) return {};
  try { return JSON.parse(value) as Record<string, unknown>; } catch { return {}; }
}

/** Merge a partial update into parsed JSON and emit the serialized result */
function emitChange(current: string | undefined, patch: Record<string, unknown>, onChange?: (json: string) => void) {
  const merged = { ...parseJson(current), ...patch };
  onChange?.(JSON.stringify(merged));
}

/** Validate URL: no localhost, no private IPs */
function isUnsafeUrl(raw: string): boolean {
  if (!raw) return false;
  try {
    const url = new URL(`https://${raw}`);
    const h = url.hostname.toLowerCase();
    if (['localhost', '127.0.0.1', '0.0.0.0', '[::1]'].includes(h)) return true;
    if (/^(10\.|172\.(1[6-9]|2\d|3[01])\.|192\.168\.)/.test(h)) return true;
    return false;
  } catch { return true; }
}

export function DataSourceEditor({
  fieldType,
  value,
  onChange,
  disabled,
  field,
  allFields,
}: DataSourceEditorProps) {
  const { t } = useTranslation();
  const parsed = parseJson(value);

  // Cascade v1: the dependencies editor applies to EVERY option-backed ref type (Enum/Internal/External).
  // Rendered only when the caller supplies field + allFields (older call-sites without them stay unchanged).
  const cascadeEditor =
    field && allFields ? (
      <>
        <Divider style={{ margin: '8px 0' }} />
        <CascadeDependenciesEditor
          field={field}
          allFields={allFields}
          value={value}
          onChange={onChange}
          disabled={disabled}
        />
      </>
    ) : null;

  // EnumRef shares the hasDataSource flag in registry but uses OptionsEditor for its static list. The
  // cascade editor authors both the `dependencies[]` AND (for EnumRef) the keyed-Enum `parentFilterMap`
  // (parentValue → allowed enum values), since enum values have no natural parent key. An EnumRef with
  // dependencies[] but an empty map resolves to a fail-safe EMPTY list on the BE (never leaks the full
  // set). Other non-ref types have no data-source editor.
  if (fieldType !== 'InternalRef' && fieldType !== 'ExternalRef') {
    if (fieldType === 'EnumRef' && cascadeEditor) return cascadeEditor;
    return (
      <Text type="secondary" style={{ fontSize: 11 }}>
        {t('forms.builder.properties.dataSourceNotApplicable', { defaultValue: 'No data source editor for this type.' })}
      </Text>
    );
  }

  if (fieldType === 'InternalRef') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <Form.Item
          label={t('forms.builder.properties.dataSourceModule', { defaultValue: 'Module' })}
          style={{ marginBottom: 0 }}
        >
          <Select
            size="small"
            disabled={disabled}
            value={(parsed.module as string) || undefined}
            options={INTERNAL_MODULE_OPTIONS}
            placeholder={t('forms.builder.properties.dataSourceModulePlaceholder', { defaultValue: 'Select module' })}
            onChange={(v) => emitChange(value, { module: v }, onChange)}
          />
        </Form.Item>

        <Form.Item
          label={t('forms.builder.properties.dataSourceEntity', { defaultValue: 'Entity' })}
          style={{ marginBottom: 0 }}
        >
          <Input
            size="small"
            disabled={disabled}
            value={(parsed.entity as string) || ''}
            placeholder="e.g. User"
            onChange={(e) => emitChange(value, { entity: e.target.value }, onChange)}
          />
        </Form.Item>

        <Form.Item
          label={t('forms.builder.properties.dataSourceLabelField', { defaultValue: 'Label field' })}
          style={{ marginBottom: 0 }}
        >
          <Input
            size="small"
            disabled={disabled}
            value={(parsed.labelField as string) || ''}
            placeholder="e.g. fullName"
            onChange={(e) => emitChange(value, { labelField: e.target.value }, onChange)}
          />
        </Form.Item>

        <Form.Item
          label={t('forms.builder.properties.dataSourceValueField', { defaultValue: 'Value field' })}
          style={{ marginBottom: 0 }}
        >
          <Input
            size="small"
            disabled={disabled}
            value={(parsed.valueField as string) || ''}
            placeholder="e.g. id"
            onChange={(e) => emitChange(value, { valueField: e.target.value }, onChange)}
          />
        </Form.Item>

        {cascadeEditor}
      </div>
    );
  }

  // ExternalRef — URL, method, cache TTL
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <Form.Item
        label={t('forms.builder.properties.dataSourceUrl', { defaultValue: 'URL' })}
        style={{ marginBottom: 0 }}
      >
        <Input
          size="small"
          disabled={disabled}
          addonBefore="https://"
          value={(parsed.url as string)?.replace(/^https?:\/\//, '') || ''}
          placeholder="api.example.com/options"
          status={isUnsafeUrl((parsed.url as string)?.replace(/^https?:\/\//, '') || '') ? 'error' : undefined}
          onChange={(e) => emitChange(value, { url: `https://${e.target.value}` }, onChange)}
        />
        {isUnsafeUrl((parsed.url as string)?.replace(/^https?:\/\//, '') || '') && (
          <Text type="danger" style={{ fontSize: 11 }}>
            {t('forms.builder.properties.unsafeUrl', { defaultValue: 'Invalid URL or private network address' })}
          </Text>
        )}
      </Form.Item>

      <Form.Item
        label={t('forms.builder.properties.dataSourceMethod', { defaultValue: 'Method' })}
        style={{ marginBottom: 0 }}
      >
        <Select
          size="small"
          disabled={disabled}
          value={(parsed.method as string) || 'GET'}
          options={HTTP_METHOD_OPTIONS}
          onChange={(v) => emitChange(value, { method: v }, onChange)}
        />
      </Form.Item>

      <Form.Item
        label={t('forms.builder.properties.dataSourceCacheTtl', { defaultValue: 'Cache TTL (seconds)' })}
        style={{ marginBottom: 0 }}
      >
        <InputNumber
          size="small"
          disabled={disabled}
          min={0}
          max={86400}
          defaultValue={300}
          value={(parsed.cacheTtl as number) ?? 300}
          style={{ width: '100%' }}
          onChange={(v) => emitChange(value, { cacheTtl: v ?? 300 }, onChange)}
        />
      </Form.Item>

      {cascadeEditor}
    </div>
  );
}
