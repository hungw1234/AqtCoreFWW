// CopilotProvider — React context that exposes sidebar open/toggle state
// Wraps authenticated layout so the sidebar is available on all protected pages.
// SessionId is stable across navigations (lives in context, not per-page state).
import { createContext, useContext, useState, useMemo, type ReactNode } from 'react';

interface CopilotContextValue {
  isOpen: boolean;
  toggle: () => void;
  /** Stable session ID that persists across page navigations */
  sessionId: string;
}

const CopilotContext = createContext<CopilotContextValue | null>(null);

/** Hook to consume CopilotContext — must be inside CopilotProvider */
export function useCopilot() {
  const ctx = useContext(CopilotContext);
  if (!ctx) throw new Error('useCopilot must be used within CopilotProvider');
  return ctx;
}

interface CopilotProviderProps {
  children: ReactNode;
}

/** Provides copilot sidebar state to the entire authenticated subtree */
export function CopilotProvider({ children }: CopilotProviderProps) {
  const [isOpen, setIsOpen] = useState(false);
  // Stable session ID — generated once per provider mount (survives navigation)
  const [sessionId] = useState(() => crypto.randomUUID());

  const value = useMemo<CopilotContextValue>(
    () => ({
      isOpen,
      toggle: () => setIsOpen((prev) => !prev),
      sessionId,
    }),
    [isOpen, sessionId],
  );

  return <CopilotContext.Provider value={value}>{children}</CopilotContext.Provider>;
}
