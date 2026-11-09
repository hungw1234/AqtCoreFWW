// TC-FE-WF-001: Inbox renders pending tasks
// TC-FE-WF-002: Inbox empty state
// TC-FE-WF-003: Admin page renders definitions

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({}),
  // useSearch is needed by useServerPagination used in WorkflowAdminPage
  useSearch: () => ({}),
  Link: ({ children }: { children: ReactNode }) => children,
}));

// The inbox page reads usePermissions to gate its "create dossier" action; grant-all so it renders.
vi.mock('@/features/auth/use-permissions', () => ({
  usePermissions: () => ({
    permissions: [],
    roles: [],
    hasPermission: () => true,
    hasRole: () => false,
    isPermissionsLoading: false,
    isPermissionsRefetching: false,
  }),
}));

// Controllable inbox API — useMyInbox returns bare WorkflowTaskDto[] (no .items wrapper)
// data is WorkflowTaskDto[] | undefined to allow isFetching=true / data=undefined scenarios
const mockUseMyInbox = vi.fn(() => ({
  data: [] as WorkflowTaskDto[] | undefined,
  isFetching: false,
}));

vi.mock('@/features/inbox/inbox-api', () => ({
  useMyInbox: () => mockUseMyInbox(),
}));

// Mock my-processed infinite query (View 2 tab — not under test here)
vi.mock('@/features/inbox/my-processed-api', () => ({
  useMyProcessedInfinite: () => ({
    data: undefined,
    isFetching: false,
    isFetchingNextPage: false,
    hasNextPage: false,
    fetchNextPage: vi.fn(),
  }),
}));

// Mock BP active list used by ProcessedDossiersList for name resolution
vi.mock('@/features/business-process/business-process-run-api', () => ({
  useActiveBps: () => ({ data: { items: [] }, isLoading: false }),
}));

// Controllable workflow definitions API
const mockUseWorkflowDefinitions = vi.fn(() => ({
  data: { items: [] as WorkflowDefinitionListDto[], totalCount: 0 },
  isLoading: false,
}));

vi.mock('@/features/workflow/workflow-api', () => ({
  useWorkflowDefinitions: () => mockUseWorkflowDefinitions(),
  useCreateWorkflow: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteWorkflowDefinition: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

import { WorkflowInboxPage } from '@/features/inbox/inbox-page';
import { WorkflowAdminPage } from '@/features/workflow/workflow-admin-page';
import type { WorkflowTaskDto } from '@/features/inbox/inbox-api';
import type { WorkflowDefinitionListDto } from '@/features/workflow/workflow-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// TC-FE-WF-001: Inbox renders pending tasks
describe('WorkflowInboxPage — TC-FE-WF-001: renders pending tasks', () => {
  it('renders without crashing', () => {
    mockUseMyInbox.mockReturnValue({ data: [], isFetching: false });
    const { container } = render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders my-dossiers page title', () => {
    render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    // Page title changed to page.myDossiers.title after 2-tab conversion
    expect(screen.getByText('page.myDossiers.title')).toBeTruthy();
  });

  it('renders pending task rows in inbox table', () => {
    mockUseMyInbox.mockReturnValue({
      data: [
        {
          id: 1,
          workflowInstanceId: 'wfi-guid-001',
          title: 'Hồ sơ xin cấp phép xây dựng',
          description: null,
          assigneeId: 'user-1',
          status: 'Open',
          priority: 'High',
          dueDate: '2024-06-10T00:00:00Z',
          completedAt: null,
          completedBy: null,
          tenantId: 'tenant-1',
          createdAt: '2024-06-01T00:00:00Z',
          createdBy: 'user-1',
          delegatedFromUserId: null,
        },
        {
          id: 2,
          workflowInstanceId: 'wfi-guid-002',
          title: 'Hồ sơ đăng ký kinh doanh',
          description: null,
          assigneeId: null,
          status: 'InProgress',
          priority: 'Normal',
          dueDate: null,
          completedAt: null,
          completedBy: null,
          tenantId: 'tenant-1',
          createdAt: '2024-06-02T00:00:00Z',
          createdBy: 'user-2',
          delegatedFromUserId: null,
        },
      ] satisfies WorkflowTaskDto[],
      isFetching: false,
    });
    render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Hồ sơ xin cấp phép xây dựng')).toBeTruthy();
    expect(screen.getByText('Hồ sơ đăng ký kinh doanh')).toBeTruthy();
  });

  it('renders inbox table element', () => {
    mockUseMyInbox.mockReturnValue({ data: [], isFetching: false });
    const { container } = render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

// TC-FE-WF-002: Inbox empty state
describe('WorkflowInboxPage — TC-FE-WF-002: empty state', () => {
  it('renders empty locale text when inbox has no items', () => {
    mockUseMyInbox.mockReturnValue({ data: [], isFetching: false });
    render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    // Ant Design Table renders locale.emptyText when dataSource is empty
    expect(screen.getByText('page.inbox.empty')).toBeTruthy();
  });

  it('renders empty locale text (no data rows) when items array is empty', () => {
    mockUseMyInbox.mockReturnValue({ data: [], isFetching: false });
    render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    // Ant Design renders emptyText when dataSource is empty
    expect(screen.getByText('page.inbox.empty')).toBeTruthy();
  });

  it('renders loading state while fetching', () => {
    mockUseMyInbox.mockReturnValue({ data: undefined, isFetching: true });
    const { container } = render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('.ant-spin')).toBeTruthy();
  });
});

// TC-FE-WF-003: Admin page renders definitions
describe('WorkflowAdminPage — TC-FE-WF-003: renders definitions', () => {
  it('renders without crashing', () => {
    mockUseWorkflowDefinitions.mockReturnValue({
      data: { items: [], totalCount: 0 },
      isLoading: false,
    });
    const { container } = render(<WorkflowAdminPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders workflow admin page title', () => {
    render(<WorkflowAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('workflow.title')).toBeTruthy();
  });

  it('renders create workflow button', () => {
    render(<WorkflowAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('workflow.createTitle')).toBeTruthy();
  });

  it('renders definitions table', () => {
    const { container } = render(<WorkflowAdminPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });

  it('renders workflow definition rows when data is provided', () => {
    mockUseWorkflowDefinitions.mockReturnValue({
      data: {
        items: [
          {
            id: 'wf-1',
            name: 'Case Approval Workflow',
            entityType: 'Case',
            isActive: true,
            definitionKey: 'case-approval',
            version: 1,
            isLatest: true,
            stateCount: 5,
            transitionCount: 8,
            createdAt: '2024-01-01T00:00:00Z',
          },
        ],
        totalCount: 1,
      },
      isLoading: false,
    });
    render(<WorkflowAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Case Approval Workflow')).toBeTruthy();
    // stateCount and transitionCount rendered in table cells
    expect(screen.getByText('5')).toBeTruthy();
  });

  it('opens create workflow modal when create button is clicked', () => {
    mockUseWorkflowDefinitions.mockReturnValue({
      data: { items: [], totalCount: 0 },
      isLoading: false,
    });
    render(<WorkflowAdminPage />, { wrapper: makeWrapper() });
    fireEvent.click(screen.getByText('workflow.createTitle'));
    // After click the modal form is open — form label appears in addition to table header
    // workflow.col.name appears as table <th> + modal form <label>, so ≥2 elements
    expect(screen.getAllByText('workflow.col.name').length).toBeGreaterThanOrEqual(2);
    // modal form label for entityType is rendered once inside the modal
    expect(screen.getByText('workflow.col.entityType')).toBeTruthy();
  });
});
