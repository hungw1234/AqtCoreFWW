import vi from '@/core/i18n/locales/vi.json';
import en from '@/core/i18n/locales/en.json';
import { describe, it, expect } from 'vitest';

describe('RBAC i18n keys', () => {
  const requiredKeys = [
    'roles.col.name', 'roles.col.code', 'roles.col.type', 'roles.col.status', 'roles.col.actions',
    'roles.active', 'roles.inactive', 'roles.create', 'roles.permissions',
    'roles.disable', 'roles.activate', 'roles.disableConfirm', 'roles.activateConfirm',
    'roles.typeSystem', 'roles.typeBusiness', 'roles.editTitle', 'roles.createTitle',
    'roles.form.name', 'roles.form.code', 'roles.form.description', 'roles.form.type',
    'roles.form.nameRequired', 'roles.form.codeRequired', 'roles.form.maxChars',
    'roles.permTitle', 'roles.sensitive', 'roles.sodWarning', 'roles.permSaved',
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
