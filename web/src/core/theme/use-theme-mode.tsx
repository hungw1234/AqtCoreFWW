import { createContext, useContext, useState, useCallback } from 'react';
import type { ReactNode } from 'react';

const STORAGE_KEY = 'theme_mode';

function readStored(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'dark';
  } catch {
    return false;
  }
}

interface ThemeModeCtx {
  isDark: boolean;
  toggle: (checked: boolean) => void;
}

const ThemeModeContext = createContext<ThemeModeCtx>({
  isDark: false,
  toggle: () => {},
});

// Provider: wrap at app root so all consumers share the same state
export function ThemeModeProvider({ children }: { children: ReactNode }) {
  const [isDark, setIsDark] = useState<boolean>(readStored);

  const toggle = useCallback((checked: boolean) => {
    setIsDark(checked);
    try {
      localStorage.setItem(STORAGE_KEY, checked ? 'dark' : 'light');
    } catch {
      // ignore storage errors
    }
  }, []);

  return (
    <ThemeModeContext.Provider value={{ isDark, toggle }}>
      {children}
    </ThemeModeContext.Provider>
  );
}

// Hook: access shared theme mode from any component
export function useThemeMode() {
  return useContext(ThemeModeContext);
}
