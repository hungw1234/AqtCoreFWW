import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { ThemeModeProvider, useThemeMode } from '@/core/theme/use-theme-mode';

// TC-FE-HOOK-003: useThemeMode toggles light/dark
// TC-FE-HOOK-004: useThemeMode persists to localStorage

function wrapper({ children }: { children: ReactNode }) {
  return createElement(ThemeModeProvider, null, children);
}

beforeEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

describe('useThemeMode — TC-FE-HOOK-003: toggles light/dark', () => {
  it('starts in light mode (isDark=false) when localStorage is empty', () => {
    const { result } = renderHook(() => useThemeMode(), { wrapper });
    expect(result.current.isDark).toBe(false);
  });

  it('toggles to dark mode when toggle(true) is called', () => {
    const { result } = renderHook(() => useThemeMode(), { wrapper });

    act(() => {
      result.current.toggle(true);
    });

    expect(result.current.isDark).toBe(true);
  });

  it('toggles back to light mode when toggle(false) is called', () => {
    const { result } = renderHook(() => useThemeMode(), { wrapper });

    act(() => { result.current.toggle(true); });
    act(() => { result.current.toggle(false); });

    expect(result.current.isDark).toBe(false);
  });
});

describe('useThemeMode — TC-FE-HOOK-004: persists to localStorage', () => {
  it('writes "dark" to localStorage when toggled on', () => {
    const { result } = renderHook(() => useThemeMode(), { wrapper });

    act(() => { result.current.toggle(true); });

    expect(localStorage.getItem('theme_mode')).toBe('dark');
  });

  it('writes "light" to localStorage when toggled off', () => {
    const { result } = renderHook(() => useThemeMode(), { wrapper });

    act(() => { result.current.toggle(true); });
    act(() => { result.current.toggle(false); });

    expect(localStorage.getItem('theme_mode')).toBe('light');
  });

  it('reads stored "dark" from localStorage on mount', () => {
    localStorage.setItem('theme_mode', 'dark');
    const { result } = renderHook(() => useThemeMode(), { wrapper });
    expect(result.current.isDark).toBe(true);
  });

  it('defaults to light when stored value is not "dark"', () => {
    localStorage.setItem('theme_mode', 'light');
    const { result } = renderHook(() => useThemeMode(), { wrapper });
    expect(result.current.isDark).toBe(false);
  });
});
