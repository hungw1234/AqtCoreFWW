import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string, fallback?: string) => fallback ?? key }),
}));

// Mock all audit API hooks
const mockUseExportAuditLogs = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));
const mockUseAuditLogs = vi.fn(() => ({ data: { items: [], totalCount: 0 }, isFetching: false }));
const mockUseLoginAuditLogs = vi.fn(() => ({ data: { items: [], totalCount: 0 }, isFetching: false }));
const mockUseSecurityIncidents = vi.fn(() => ({ data: { items: [], totalCount: 0 }, isFetching: false }));

vi.mock('@/features/audit/audit-api', () => ({
  useExportAuditLogs: () => mockUseExportAuditLogs(),
  useAuditLogs: () => mockUseAuditLogs(),
  useLoginAuditLogs: () => mockUseLoginAuditLogs(),
  useSecurityIncidents: () => mockUseSecurityIncidents(),
  useVerifyAuditChain: () => ({ mutate: vi.fn(), isPending: false, data: undefined, reset: vi.fn() }),
}));

// Stub sub-tables to avoid deep dependency chains
vi.mock('@/features/audit/audit-log-table', () => ({
  AuditLogTable: () => <div data-testid="audit-log-table">AuditLogTable</div>,
}));
vi.mock('@/features/audit/login-audit-table', () => ({
  LoginAuditTable: () => <div data-testid="login-audit-table">LoginAuditTable</div>,
}));
vi.mock('@/features/audit/security-incidents-table', () => ({
  SecurityIncidentsTable: () => <div data-testid="security-incidents-table">SecurityIncidentsTable</div>,
}));

vi.mock('@/core/hooks/use-server-pagination', () => ({
  useServerPagination: () => ({
    antPagination: { current: 1, pageSize: 20 },
    toQueryParams: () => ({ pageNumber: 1, pageSize: 20 }),
  }),
}));

import { AuditLogPage } from '@/features/audit/audit-log-page';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('AuditLogPage — render', () => {
  it('renders without crashing', () => {
    const { container } = render(<AuditLogPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<AuditLogPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.audit.title')).toBeTruthy();
  });

  it('renders export CSV button', () => {
    render(<AuditLogPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Export CSV')).toBeTruthy();
  });

  it('renders audit log tab by default', () => {
    render(<AuditLogPage />, { wrapper: makeWrapper() });
    expect(screen.getByTestId('audit-log-table')).toBeTruthy();
  });

  it('renders all three tab labels', () => {
    render(<AuditLogPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.audit.tab.logs')).toBeTruthy();
    expect(screen.getByText('page.audit.tab.login')).toBeTruthy();
    expect(screen.getByText('page.audit.tab.incidents')).toBeTruthy();
  });
});
