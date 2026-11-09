import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@/features/dashboard-overview/admin-dashboard-api', () => ({
  useAdminDashboardStats: () => ({
    data: { healthStatus: 'healthy', totalUsers: 0, activeSessions: 0, pendingJobs: 0 },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}));

vi.mock('@/features/dashboard-overview/admin-quick-actions', () => ({
  AdminQuickActions: () => createElement('div', { 'data-testid': 'quick-actions' }),
}));

vi.mock('@/features/dashboard-overview/admin-recent-activity', () => ({
  AdminRecentActivity: () => createElement('div', { 'data-testid': 'recent-activity' }),
}));

import { DashboardPage } from '@/features/dashboard/dashboard-page';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('DashboardPage — admin overview at "/"', () => {
  it('renders without crashing', () => {
    const { container } = render(<DashboardPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders admin dashboard title', () => {
    render(<DashboardPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.adminDashboard.title')).toBeTruthy();
  });

  it('renders quick actions and recent activity sections', () => {
    render(<DashboardPage />, { wrapper: makeWrapper() });
    expect(screen.getByTestId('quick-actions')).toBeTruthy();
    expect(screen.getByTestId('recent-activity')).toBeTruthy();
  });
});
