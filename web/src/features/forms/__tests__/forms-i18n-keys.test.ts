import vi from '@/core/i18n/locales/vi.json';
import en from '@/core/i18n/locales/en.json';
import { describe, it, expect } from 'vitest';

describe('Forms i18n keys', () => {
  const requiredKeys = [
    'forms.field.requiredMsg', 'forms.field.fileExceedsLimit', 'forms.field.allowedTypes',
    'forms.field.uploaded', 'forms.field.chooseFile',
    'forms.address.loadError', 'forms.address.province', 'forms.address.ward',
    'forms.signature.clear',
    'forms.table.addRow', 'forms.table.maxRows', 'forms.table.keyCol', 'forms.table.valueCol',
    'page.forms.detail.duplicateFailed',
  ];

  it.each(requiredKeys)('vi.json has key: %s', (key) => {
    const parts = key.split('.');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let obj: any = vi;
    for (const part of parts) {
      expect(obj).toHaveProperty(part);
      obj = obj[part];
    }
    expect(obj).toBeTruthy();
  });

  it.each(requiredKeys)('en.json has key: %s', (key) => {
    const parts = key.split('.');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let obj: any = en;
    for (const part of parts) {
      expect(obj).toHaveProperty(part);
      obj = obj[part];
    }
    expect(obj).toBeTruthy();
  });
});
