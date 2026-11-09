// TC-FE-SP-001: Renders parameter groups (tabs: params, feature flags, announcements)

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// Mock tab children — each is its own component with API calls
vi.mock('@/features/system-params/params-table', () => ({
  ParamsTable: () => createElement('div', { 'data-testid': 'params-table' }),
}));

vi.mock('@/features/system-params/feature-flags-tab', () => ({
  FeatureFlagsTab: () => createElement('div', { 'data-testid': 'feature-flags-tab' }),
}));

vi.mock('@/features/system-params/announcements-tab', () => ({
  AnnouncementsTab: () => createElement('div', { 'data-testid': 'announcements-tab' }),
}));

import { SystemParamsPage } from '@/features/system-params/system-params-page';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// TC-FE-SP-001: Renders parameter groups
describe('SystemParamsPage — TC-FE-SP-001: renders parameter groups', () => {
  it('renders without crashing', () => {
    const { container } = render(<SystemParamsPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<SystemParamsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.systemParams.title')).toBeTruthy();
  });

  it('renders system params tab label', () => {
    render(<SystemParamsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.systemParams.tab.params')).toBeTruthy();
  });

  it('renders feature flags tab label', () => {
    render(<SystemParamsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.systemParams.tab.flags')).toBeTruthy();
  });

  it('renders announcements tab label', () => {
    render(<SystemParamsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.systemParams.tab.announcements')).toBeTruthy();
  });

  it('renders ParamsTable in active default tab', () => {
    render(<SystemParamsPage />, { wrapper: makeWrapper() });
    // Default active tab is "params" — ParamsTable should be mounted
    expect(screen.getByTestId('params-table')).toBeTruthy();
  });

  it('renders the active tab pane content (params tab is default)', () => {
    render(<SystemParamsPage />, { wrapper: makeWrapper() });
    // Only the active tab pane content is in the DOM in jsdom
    // Default active tab is "params" — ParamsTable is rendered
    expect(screen.getByTestId('params-table')).toBeTruthy();
  });
});
