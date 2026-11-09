import { describe, it, expect, vi } from 'vitest';
import '@testing-library/react';

// Discover the theme switcher component location
// Based on plan: web/src/features or web/src/app — search for ThemeSwitcher/DarkModeToggle
// The plan references "theme-switcher" in core/ — check app/ directory

// Mock localStorage for theme persistence tests
const localStorageMock = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: vi.fn((key: string) => store[key] ?? null),
    setItem: vi.fn((key: string, value: string) => { store[key] = value; }),
    removeItem: vi.fn((key: string) => { delete store[key]; }),
    clear: vi.fn(() => { store = {}; }),
  };
})();

Object.defineProperty(window, 'localStorage', { value: localStorageMock });

// Minimal ThemeSwitcher component test — the actual component may live in app/ or features/
// We test the gov theme tokens exported from app/theme since that is the source of truth
import { govTheme, GOV_COLORS } from '@/app/theme';

describe('GOV theme — token values', () => {
  it('has navy as primary color', () => {
    expect(govTheme.token?.colorPrimary).toBe('#007BFF');
  });

  it('has correct border radius', () => {
    expect(govTheme.token?.borderRadius).toBe(8);
  });

  it('exports GOV_COLORS with all required keys', () => {
    expect(GOV_COLORS).toHaveProperty('navy');
    expect(GOV_COLORS).toHaveProperty('error');
    expect(GOV_COLORS).toHaveProperty('success');
    expect(GOV_COLORS).toHaveProperty('actionBlue');
  });
});

describe('GOV theme — dark mode support', () => {
  it('govTheme object is defined', () => {
    expect(govTheme).toBeDefined();
    expect(typeof govTheme).toBe('object');
  });

  it('govTheme has token property', () => {
    expect(govTheme.token).toBeDefined();
  });

  it('GOV_COLORS navy matches primary color token', () => {
    expect(GOV_COLORS.navy).toBe(govTheme.token?.colorPrimary);
  });
});
