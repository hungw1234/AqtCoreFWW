import { useState, useEffect } from 'react';

// 3-tier responsive layout mode based on viewport width
export type LayoutMode = 'desktop' | 'tablet' | 'mobile';

// matchMedia queries — fire only at breakpoint boundaries (no per-pixel resize)
const MOBILE_QUERY = '(max-width: 767px)';
const TABLET_QUERY = '(min-width: 768px) and (max-width: 1023px)';

function getMode(): LayoutMode {
  if (window.matchMedia(MOBILE_QUERY).matches) return 'mobile';
  if (window.matchMedia(TABLET_QUERY).matches) return 'tablet';
  return 'desktop';
}

// Hook: returns current layout mode, updates only at breakpoint boundaries
export function useLayoutMode(): LayoutMode {
  const [mode, setMode] = useState<LayoutMode>(getMode);

  useEffect(() => {
    const mobileMq = window.matchMedia(MOBILE_QUERY);
    const tabletMq = window.matchMedia(TABLET_QUERY);
    const handler = () => setMode(getMode());

    mobileMq.addEventListener('change', handler);
    tabletMq.addEventListener('change', handler);
    return () => {
      mobileMq.removeEventListener('change', handler);
      tabletMq.removeEventListener('change', handler);
    };
  }, []);

  return mode;
}
