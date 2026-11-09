// field-type-registry.ts — static config map for all 16 FormFieldType values
// Used by FieldPalette (drag sources) and FieldPropertiesPanel (conditional sections)

import type { FormFieldType } from '../form-types';

export type FieldCategory = 'basic' | 'reference' | 'complex' | 'computed' | 'layout';

export interface FieldTypeConfig {
  type: FormFieldType;
  category: FieldCategory;
  /** Ant Design icon component name (string key, resolved in palette) */
  icon: string;
  /** i18n key under forms.builder.fieldTypes.* */
  labelKey: string;
  hasOptions: boolean;      // shows FieldOptionEditor (EnumRef)
  hasDataSource: boolean;   // shows DataSource JSON editor (Ref types)
  hasFormula: boolean;      // shows Formula expression editor
  hasValidation: boolean;   // shows FieldValidationEditor
}

export const FIELD_TYPE_REGISTRY: Record<FormFieldType, FieldTypeConfig> = {
  // --- Basic (7) ---
  Text: {
    type: 'Text', category: 'basic', icon: 'FontSizeOutlined',
    labelKey: 'forms.builder.fieldTypes.Text',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: true,
  },
  Number: {
    type: 'Number', category: 'basic', icon: 'NumberOutlined',
    labelKey: 'forms.builder.fieldTypes.Number',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: true,
  },
  Date: {
    type: 'Date', category: 'basic', icon: 'CalendarOutlined',
    labelKey: 'forms.builder.fieldTypes.Date',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: true,
  },
  Textarea: {
    type: 'Textarea', category: 'basic', icon: 'AlignLeftOutlined',
    labelKey: 'forms.builder.fieldTypes.Textarea',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: true,
  },
  Boolean: {
    type: 'Boolean', category: 'basic', icon: 'CheckSquareOutlined',
    labelKey: 'forms.builder.fieldTypes.Boolean',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  File: {
    type: 'File', category: 'basic', icon: 'PaperClipOutlined',
    labelKey: 'forms.builder.fieldTypes.File',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: true,
  },
  Signature: {
    type: 'Signature', category: 'basic', icon: 'EditOutlined',
    labelKey: 'forms.builder.fieldTypes.Signature',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  RichText: {
    type: 'RichText', category: 'basic', icon: 'FileTextOutlined',
    labelKey: 'forms.builder.fieldTypes.RichText',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: true,
  },
  DigitalSignature: {
    // Mode config (CanonicalJson | ServerDocHash) is edited via a dedicated selector in the
    // properties panel and persisted into validationRulesJson — NOT the min/max/pattern editor,
    // so hasValidation stays false.
    type: 'DigitalSignature', category: 'basic', icon: 'SafetyCertificateOutlined',
    labelKey: 'forms.builder.fieldTypes.DigitalSignature',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  // --- Reference (3) ---
  EnumRef: {
    type: 'EnumRef', category: 'reference', icon: 'UnorderedListOutlined',
    labelKey: 'forms.builder.fieldTypes.EnumRef',
    hasOptions: true, hasDataSource: true, hasFormula: false, hasValidation: false,
  },
  InternalRef: {
    type: 'InternalRef', category: 'reference', icon: 'LinkOutlined',
    labelKey: 'forms.builder.fieldTypes.InternalRef',
    hasOptions: false, hasDataSource: true, hasFormula: false, hasValidation: false,
  },
  ExternalRef: {
    type: 'ExternalRef', category: 'reference', icon: 'GlobalOutlined',
    labelKey: 'forms.builder.fieldTypes.ExternalRef',
    hasOptions: false, hasDataSource: true, hasFormula: false, hasValidation: false,
  },
  // --- Complex (3) ---
  TableField: {
    type: 'TableField', category: 'complex', icon: 'TableOutlined',
    labelKey: 'forms.builder.fieldTypes.TableField',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  AddressField: {
    type: 'AddressField', category: 'complex', icon: 'EnvironmentOutlined',
    labelKey: 'forms.builder.fieldTypes.AddressField',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  DateRange: {
    type: 'DateRange', category: 'complex', icon: 'FieldTimeOutlined',
    labelKey: 'forms.builder.fieldTypes.DateRange',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  // --- Computed (1) ---
  Formula: {
    type: 'Formula', category: 'computed', icon: 'FunctionOutlined',
    labelKey: 'forms.builder.fieldTypes.Formula',
    hasOptions: false, hasDataSource: false, hasFormula: true, hasValidation: false,
  },
  // --- Layout (3) ---
  Section: {
    type: 'Section', category: 'layout', icon: 'BarsOutlined',
    labelKey: 'forms.builder.fieldTypes.Section',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  Label: {
    type: 'Label', category: 'layout', icon: 'TagOutlined',
    labelKey: 'forms.builder.fieldTypes.Label',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
  Divider: {
    type: 'Divider', category: 'layout', icon: 'MinusOutlined',
    labelKey: 'forms.builder.fieldTypes.Divider',
    hasOptions: false, hasDataSource: false, hasFormula: false, hasValidation: false,
  },
};

/** Ordered category list for palette display */
export const FIELD_CATEGORIES: FieldCategory[] = [
  'basic', 'reference', 'complex', 'computed', 'layout',
];

/** All configs grouped by category — used by FieldPalette */
export function getByCategory(cat: FieldCategory): FieldTypeConfig[] {
  return Object.values(FIELD_TYPE_REGISTRY).filter((c) => c.category === cat);
}
