// TC-FE-WFT-ADM-001: Renders table with rows fetched from useWorkflowStateTaxonomies
// TC-FE-WFT-ADM-002: "Create" button opens drawer in create mode (empty form)
// TC-FE-WFT-ADM-003: Submitting Create form calls useCreateWorkflowStateTaxonomy with payload
// TC-FE-WFT-ADM-004: Edit button on a row opens drawer with pre-filled values
// TC-FE-WFT-ADM-005: Submitting Update form calls useUpdateWorkflowStateTaxonomy with id + rowVersion

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { WorkflowStateTaxonomyDto } from '@/features/workflow/use-workflow-taxonomies';

// ─── Mocks ───────────────────────────────────────────────────────────────────

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

// i18next: pass-through key resolver so tests don't need full i18n config
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, fallback?: string) => fallback ?? key,
    i18n: { changeLanguage: vi.fn() },
  }),
}));

import { WorkflowStateTaxonomyAdminPage } from '@/features/workflow/workflow-state-taxonomy-admin-page';

// ─── Fixtures ────────────────────────────────────────────────────────────────

const ROWS: WorkflowStateTaxonomyDto[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    code: 'Draft',
    nameVi: 'Nháp',
    nameEn: 'Draft',
    i18nKey: null,
    icon: 'EditOutlined',
    color: '#808080',
    category: 'Initial',
    sortOrder: 1,
    isActive: true,
    deprecatedReason: null,
    rowVersion: 'AAAAAAAAB/E=',
  },
];

// ─── Wrapper ─────────────────────────────────────────────────────────────────

function renderWithClient(node: ReactNode) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 }, mutations: { retry: false } },
  });
  return render(createElement(QueryClientProvider, { client: qc }, node));
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('WorkflowStateTaxonomyAdminPage', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
    mockPut.mockReset();
  });

  // TC-FE-WFT-ADM-001
  it('renders table with rows from the taxonomy hook', async () => {
    mockGet.mockResolvedValueOnce({ data: ROWS });

    renderWithClient(<WorkflowStateTaxonomyAdminPage />);

    // "Draft" appears twice (code column + nameEn column); "Nháp" is unique
    expect(await screen.findByText('Nháp')).toBeInTheDocument();
    const draftMatches = screen.getAllByText('Draft');
    expect(draftMatches.length).toBeGreaterThanOrEqual(1);
    expect(mockGet).toHaveBeenCalledWith('/admin/master-data/workflow-taxonomy/states');
  });

  // TC-FE-WFT-ADM-002
  it('opens an empty Create drawer when "Create" button clicked', async () => {
    mockGet.mockResolvedValueOnce({ data: [] });

    renderWithClient(<WorkflowStateTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /workflow.taxonomy.admin.create/i }));

    // Form fields become visible
    expect(await screen.findByLabelText(/workflow.taxonomy.admin.code/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/workflow.taxonomy.admin.nameVi/i)).toBeInTheDocument();
  });

  // TC-FE-WFT-ADM-003
  it('submits Create form → POST /states with payload', async () => {
    mockGet.mockResolvedValue({ data: [] });
    mockPost.mockResolvedValueOnce({ data: { id: 'new-id' } });

    renderWithClient(<WorkflowStateTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /workflow.taxonomy.admin.create/i }));

    await user.type(await screen.findByLabelText(/workflow.taxonomy.admin.code/i), 'InReview');
    await user.type(screen.getByLabelText(/workflow.taxonomy.admin.nameVi/i), 'Đang xử lý');
    await user.type(screen.getByLabelText(/workflow.taxonomy.admin.nameEn/i), 'In Review');
    // Category defaults to 'Intermediate' in openCreate — no user interaction needed

    await user.click(screen.getByRole('button', { name: /common.save/i }));

    await waitFor(() => expect(mockPost).toHaveBeenCalled());
    expect(mockPost).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/states',
      expect.objectContaining({
        code: 'InReview',
        nameVi: 'Đang xử lý',
        nameEn: 'In Review',
        category: 'Intermediate',
      }),
    );
  });

  // TC-FE-WFT-ADM-004
  it('opens Edit drawer with pre-filled values when Edit clicked', async () => {
    mockGet.mockResolvedValueOnce({ data: ROWS });

    renderWithClient(<WorkflowStateTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: /workflow.taxonomy.admin.edit/i }));

    const nameViInput = await screen.findByLabelText(/workflow.taxonomy.admin.nameVi/i);
    expect(nameViInput).toHaveValue('Nháp');
  });

  // TC-FE-WFT-ADM-005
  it('submits Update form → PUT /states/{id} with rowVersion', async () => {
    mockGet.mockResolvedValue({ data: ROWS });
    mockPut.mockResolvedValueOnce({ data: undefined });

    renderWithClient(<WorkflowStateTaxonomyAdminPage />);
    await waitFor(() => expect(mockGet).toHaveBeenCalled());

    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: /workflow.taxonomy.admin.edit/i }));

    const nameViInput = await screen.findByLabelText(/workflow.taxonomy.admin.nameVi/i);
    await user.clear(nameViInput);
    await user.type(nameViInput, 'Đã sửa');

    await user.click(screen.getByRole('button', { name: /common.save/i }));

    await waitFor(() => expect(mockPut).toHaveBeenCalled());
    expect(mockPut).toHaveBeenCalledWith(
      `/admin/master-data/workflow-taxonomy/states/${ROWS[0].id}`,
      expect.objectContaining({
        nameVi: 'Đã sửa',
        rowVersion: ROWS[0].rowVersion,
      }),
    );
  });
});
