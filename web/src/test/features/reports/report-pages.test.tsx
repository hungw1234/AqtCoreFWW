// TC-FE-RPT-001: Report definitions page renders
// TC-FE-RPT-002: Report executions page renders

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// Mock report-run-modal to avoid deep renders
vi.mock('@/features/reports/report-run-modal', () => ({
  ReportRunModal: () => null,
}));

// Controllable report API
const mockUseReportDefinitions = vi.fn(() => ({ data: [] as ReportDefinitionDto[], isLoading: false }));
const mockUseReportExecution = vi.fn(() => ({ data: null, isLoading: false }));

vi.mock('@/features/reports/report-api', () => ({
  useReportDefinitions: () => mockUseReportDefinitions(),
  useCreateReportDefinition: () => ({ mutate: vi.fn(), isPending: false }),
  useReportExecution: () => mockUseReportExecution(),
  downloadReport: vi.fn(),
}));

import { ReportDefinitionsPage } from '@/features/reports/report-definitions-page';
import { ReportExecutionsPage } from '@/features/reports/report-executions-page';
import type { ReportDefinitionDto } from '@/features/reports/report-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// TC-FE-RPT-001: Report definitions page renders
describe('ReportDefinitionsPage — TC-FE-RPT-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<ReportDefinitionsPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<ReportDefinitionsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.reports.definitions.title')).toBeTruthy();
  });

  it('renders create button', () => {
    render(<ReportDefinitionsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.reports.definitions.btnCreate')).toBeTruthy();
  });

  it('renders table with definition rows when data is provided', () => {
    mockUseReportDefinitions.mockReturnValue({
      data: [
        {
          id: 'rpt-1',
          name: 'monthly_report',
          nameVi: 'Báo cáo tháng',
          description: 'Báo cáo hồ sơ hàng tháng',
          parametersSchema: '{}',
          outputFormat: 'Excel',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
        },
      ],
      isLoading: false,
    });
    render(<ReportDefinitionsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Báo cáo tháng')).toBeTruthy();
  });

  it('renders table element', () => {
    mockUseReportDefinitions.mockReturnValue({ data: [], isLoading: false });
    const { container } = render(<ReportDefinitionsPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

// TC-FE-RPT-002: Report executions page renders
describe('ReportExecutionsPage — TC-FE-RPT-002', () => {
  it('renders without crashing', () => {
    mockUseReportDefinitions.mockReturnValue({ data: [], isLoading: false });
    const { container } = render(<ReportExecutionsPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    mockUseReportDefinitions.mockReturnValue({ data: [], isLoading: false });
    render(<ReportExecutionsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.reports.executions.title')).toBeTruthy();
  });

  it('renders run button', () => {
    mockUseReportDefinitions.mockReturnValue({ data: [], isLoading: false });
    render(<ReportExecutionsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.reports.executions.btnRun')).toBeTruthy();
  });

  it('renders empty session alert when no executions tracked', () => {
    mockUseReportDefinitions.mockReturnValue({ data: [], isLoading: false });
    render(<ReportExecutionsPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.reports.executions.emptySession')).toBeTruthy();
  });
});
