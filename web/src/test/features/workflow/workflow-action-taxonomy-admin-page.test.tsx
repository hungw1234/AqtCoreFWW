// TC-FE-WFT-AADM-001: Renders table with rows fetched from useWorkflowActionTaxonomies
// TC-FE-WFT-AADM-002: "Create" button opens drawer in create mode
// TC-FE-WFT-AADM-003: Submitting Create form calls useCreateWorkflowActionTaxonomy with payload
// TC-FE-WFT-AADM-004: Edit button opens drawer with pre-filled values
// TC-FE-WFT-AADM-005: Submitting Update form calls useUpdateWorkflowActionTaxonomy with id + rowVersion

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { WorkflowActionTaxonomyDto } from '@/features/workflow/use-workflow-taxonomies';

const mockGet = vi.fn();
const mockPost = vi.fn();
const mockPut = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
    put: (...args: unknown[]) => mockPut(...args),
  },
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, fallback?: string) => fallback ?? key,
    i18n: { changeLanguage: vi.fn() },
  }),
}));

import { WorkflowActionTaxonomyAdminPage } from '@/features/workflow/workflow-action-taxonomy-admin-page';

const ROWS: WorkflowActionTaxonomyDto[] = [
  {
    id: '33333333-3333-3333-3333-333333333333',
    code: 'Submit',
    nameVi: 'Nộp',
    nameEn: 'Submit',
    i18nKey: null,
    icon: null,
    sortOrder: 1,
    isActive: true,
    deprecatedReason: null,
    rowVersion: 'AAAAAAAAB/M=',
  },
];

function renderWithClient(node: ReactNode) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 }, mutations: { retry: false } },
  });
  return render(createElement(QueryClientProvider, { client: qc }, node));
}

describe('WorkflowActionTaxonomyAdminPage', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
    mockPut.mockReset();
  });

  // TC-FE-WFT-AADM-001
  it('renders table with rows from the action taxonomy hook', async () => {
    mockGet.mockResolvedValueOnce({ data: ROWS });
    renderWithClient(<WorkflowActionTaxonomyAdminPage />);

    expect(await screen.findByText('Nộp')).toBeInTheDocument();
    // "Submit" appears twice (code column + nameEn column); "Nộp" is unique
    const submitMatches = screen.getAllByText('Submit');
    expect(submitMatches.length).toBeGreaterThanOrEqual(1);
    expect(mockGet).toHaveBeenCalledWith('/admin/master-data/workflow-taxonomy/actions');
  });

  // TC-FE-WFT-AADM-002
  it('opens an empty Create drawer when "Create" button clicked', async () => {
    mockGet.mockResolvedValueOnce({ data: [] });
    renderWithClient(<WorkflowActionTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /workflow.taxonomy.admin.create/i }));

    expect(await screen.findByLabelText(/workflow.taxonomy.admin.code/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/workflow.taxonomy.admin.nameVi/i)).toBeInTheDocument();
  });

  // TC-FE-WFT-AADM-003
  it('submits Create form → POST /actions with payload', async () => {
    mockGet.mockResolvedValue({ data: [] });
    mockPost.mockResolvedValueOnce({ data: { id: 'new-id' } });

    renderWithClient(<WorkflowActionTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /workflow.taxonomy.admin.create/i }));

    await user.type(await screen.findByLabelText(/workflow.taxonomy.admin.code/i), 'Approve');
    await user.type(screen.getByLabelText(/workflow.taxonomy.admin.nameVi/i), 'Duyệt');
    await user.type(screen.getByLabelText(/workflow.taxonomy.admin.nameEn/i), 'Approve');

    await user.click(screen.getByRole('button', { name: /common.save/i }));

    await waitFor(() => expect(mockPost).toHaveBeenCalled());
    expect(mockPost).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/actions',
      expect.objectContaining({
        code: 'Approve',
        nameVi: 'Duyệt',
        nameEn: 'Approve',
      }),
    );
  });

  // TC-FE-WFT-AADM-004
  it('opens Edit drawer with pre-filled values', async () => {
    mockGet.mockResolvedValueOnce({ data: ROWS });
    renderWithClient(<WorkflowActionTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: /workflow.taxonomy.admin.edit/i }));

    const nameViInput = await screen.findByLabelText(/workflow.taxonomy.admin.nameVi/i);
    expect(nameViInput).toHaveValue('Nộp');
  });

  // TC-FE-WFT-AADM-005
  it('submits Update form → PUT /actions/{id} with rowVersion', async () => {
    mockGet.mockResolvedValue({ data: ROWS });
    mockPut.mockResolvedValueOnce({ data: undefined });

    renderWithClient(<WorkflowActionTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: /workflow.taxonomy.admin.edit/i }));

    const nameViInput = await screen.findByLabelText(/workflow.taxonomy.admin.nameVi/i);
    await user.clear(nameViInput);
    await user.type(nameViInput, 'Đã sửa');

    await user.click(screen.getByRole('button', { name: /common.save/i }));

    await waitFor(() => expect(mockPut).toHaveBeenCalled());
    expect(mockPut).toHaveBeenCalledWith(
      `/admin/master-data/workflow-taxonomy/actions/${ROWS[0].id}`,
      expect.objectContaining({
        nameVi: 'Đã sửa',
        rowVersion: ROWS[0].rowVersion,
      }),
    );
  });
});
