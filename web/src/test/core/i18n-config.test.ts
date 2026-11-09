import { describe, it, expect, beforeAll } from 'vitest';

// TC-FE-I18N-001: Default locale is vi
// TC-FE-I18N-002: Loads English translations

// Import i18n instance after mocks — i18n-config initialises on import
import i18n, { SUPPORTED_LANGUAGES, LANGUAGE_LABELS } from '@/core/i18n/i18n-config';

beforeAll(async () => {
  // Ensure i18n is fully initialised before assertions
  if (!i18n.isInitialized) {
    await new Promise<void>((resolve) => {
      i18n.on('initialized', resolve);
    });
  }
});

describe('i18n config — TC-FE-I18N-001: default locale is vi', () => {
  it('has vi as a supported language', () => {
    expect(SUPPORTED_LANGUAGES).toContain('vi');
  });

  it('has en as a supported language', () => {
    expect(SUPPORTED_LANGUAGES).toContain('en');
  });

  it('has exactly two supported languages', () => {
    expect(SUPPORTED_LANGUAGES).toHaveLength(2);
  });

  it('fallbackLng is vi', () => {
    // i18next normalises fallbackLng to an array internally
    const fallback = i18n.options.fallbackLng;
    const langs = Array.isArray(fallback) ? fallback : [fallback];
    expect(langs).toContain('vi');
  });

  it('translates a known vi key correctly', () => {
    i18n.changeLanguage('vi');
    expect(i18n.t('nav.dashboard')).toBe('Tổng quan');
  });

  it('LANGUAGE_LABELS has correct Vietnamese label', () => {
    expect(LANGUAGE_LABELS.vi).toBe('Tiếng Việt');
  });
});

describe('i18n config — TC-FE-I18N-002: loads English translations', () => {
  it('translates nav.dashboard to English when locale is en', () => {
    i18n.changeLanguage('en');
    expect(i18n.t('nav.dashboard')).toBe('Dashboard');
  });

  it('translates nav.inbox to English', () => {
    i18n.changeLanguage('en');
    // nav.inbox relabelled to "My Dossiers" after 2-tab page conversion
    expect(i18n.t('nav.inbox')).toBe('My Dossiers');
  });

  it('LANGUAGE_LABELS has correct English label', () => {
    expect(LANGUAGE_LABELS.en).toBe('English');
  });

  it('restores vi translations after switching back', () => {
    i18n.changeLanguage('vi');
    expect(i18n.t('nav.inbox')).toBeTruthy();
  });
});
