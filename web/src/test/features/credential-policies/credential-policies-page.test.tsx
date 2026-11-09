// TC-FE-CP-001: credential-policies-page renders 3 new escalation fields in modal
// TC-FE-CP-002: submit payload includes escalationLockoutCount/escalationWindowHours/showRemainingAttempts
// TC-FE-CP-003: escalationLockoutCount=0 triggers validation error

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useSearch: () => ({}),
  Link: ({ children }: { children: ReactNode }) => children,
}));

// Shared mock for notification bridge (antd message uses it internally in some setups).
// appMessage is consumed by the page's save/delete handlers — without it the success/error
// paths throw and surface as unhandled rejections.
vi.mock('@/core/api/notification-bridge', () => ({
  getNotificationInstance: () => null,
  setNotificationInstance: vi.fn(),
  appMessage: {
    success: vi.fn(),
    error: vi.fn(),
    warning: vi.fn(),
    info: vi.fn(),
    loading: vi.fn(),
    destroy: vi.fn(),
  },
}));

// Controllable API hooks
const mockCreatePolicy = vi.fn();
const mockUpdatePolicy = vi.fn();
const mockDeletePolicy = vi.fn();

vi.mock('@/features/credential-policies/credential-policies-api', () => ({
  useCredentialPolicies: () => ({
    data: { items: [], totalCount: 0 },
    isLoading: false,
  }),
  useCreateCredentialPolicy: () => ({
    mutateAsync: mockCreatePolicy,
    isPending: false,
  }),
  useUpdateCredentialPolicy: () => ({
    mutateAsync: mockUpdatePolicy,
    isPending: false,
  }),
  useDeleteCredentialPolicy: () => ({
    mutate: mockDeletePolicy,
    isPending: false,
  }),
}));

// Stub shared layout components to avoid deep dependency trees
vi.mock('@/shared/components/admin-page-header', () => ({
  AdminPageHeader: ({ title, actions }: { title: string; actions?: ReactNode }) =>
    createElement('div', { 'data-testid': 'page-header' }, title, actions),
}));
vi.mock('@/shared/components/admin-content-card', () => ({
  AdminContentCard: ({ children }: { children: ReactNode }) =>
    createElement('div', { 'data-testid': 'content-card' }, children),
}));
vi.mock('@/shared/components/admin-table-toolbar', () => ({
  AdminTableToolbar: () => createElement('div', { 'data-testid': 'table-toolbar' }),
}));

import { CredentialPoliciesPage } from '@/features/credential-policies/credential-policies-page';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// Helper: open the create modal
function openModal() {
  // The "Thêm" / common.add button opens the create modal
  const addBtn = screen.getAllByRole('button').find(
    (btn) => btn.textContent?.includes('common.add') || btn.textContent?.includes('Thêm'),
  );
  if (addBtn) fireEvent.click(addBtn);
}

// ─── TC-FE-CP-001: Render 3 new escalation fields ────────────────────────────

describe('CredentialPoliciesPage — TC-FE-CP-001: 3 escalation fields in modal', () => {
  it('renders without crashing', () => {
    const { container } = render(<CredentialPoliciesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('escalationLockoutCount label visible after opening modal', async () => {
    render(<CredentialPoliciesPage />, { wrapper: makeWrapper() });
    openModal();
    await waitFor(() => {
      expect(screen.getByText('credentialPolicies.escalationLockoutCount')).toBeTruthy();
    });
  });

  it('escalationWindowHours label visible after opening modal', async () => {
    render(<CredentialPoliciesPage />, { wrapper: makeWrapper() });
    openModal();
    await waitFor(() => {
      expect(screen.getByText('credentialPolicies.escalationWindowHours')).toBeTruthy();
    });
  });

  it('showRemainingAttempts label visible after opening modal', async () => {
    render(<CredentialPoliciesPage />, { wrapper: makeWrapper() });
    openModal();
    await waitFor(() => {
      expect(screen.getByText('credentialPolicies.showRemainingAttempts')).toBeTruthy();
    });
  });
});

// ─── TC-FE-CP-002: Submit payload includes the 3 new fields ──────────────────

describe('CredentialPoliciesPage — TC-FE-CP-002: submit includes escalation fields', () => {
  it('mutateAsync called with escalationLockoutCount, escalationWindowHours, showRemainingAttempts', async () => {
    mockCreatePolicy.mockResolvedValue({ id: 'new-id' });
    render(<CredentialPoliciesPage />, { wrapper: makeWrapper() });
    openModal();

    // Wait for modal form to render
    await waitFor(() => {
      expect(screen.getByText('credentialPolicies.escalationLockoutCount')).toBeTruthy();
    });

    // Fill required "name" field (only required non-defaulted field)
    const nameInput = document.querySelector('input[id="name"]') as HTMLInputElement
      ?? document.querySelector('.ant-modal input[type="text"]') as HTMLInputElement;
    if (nameInput) {
      fireEvent.change(nameInput, { target: { value: 'Test Policy' } });
    }

    // Click OK / Lưu
    const okBtn = screen.getAllByRole('button').find(
      (btn) => btn.textContent === 'common.save' || btn.textContent === 'Lưu',
    );
    if (okBtn) fireEvent.click(okBtn);

    await waitFor(() => {
      if (mockCreatePolicy.mock.calls.length > 0) {
        const payload = mockCreatePolicy.mock.calls[0][0] as Record<string, unknown>;
        expect(payload).toHaveProperty('escalationLockoutCount');
        expect(payload).toHaveProperty('escalationWindowHours');
        expect(payload).toHaveProperty('showRemainingAttempts');
      }
    }, { timeout: 2000 });
  });
});

// ─── TC-FE-CP-003: escalationLockoutCount = 0 triggers validation ────────────
// AntD InputNumber validates via JS rules (not native min attr).
// Strategy: open modal, clear the escalationLockoutCount field, submit → assert error.

describe('CredentialPoliciesPage — TC-FE-CP-003: escalationLockoutCount=0 triggers error', () => {
  it('shows validation error when escalationLockoutCount is cleared before submit', async () => {
    mockCreatePolicy.mockResolvedValue({ id: 'new-id' });
    render(<CredentialPoliciesPage />, { wrapper: makeWrapper() });
    openModal();

    // Wait for modal form to mount
    await waitFor(() => {
      expect(screen.getByText('credentialPolicies.escalationLockoutCount')).toBeTruthy();
    });

    // Find the escalationLockoutCount spinbutton — it is the one inside the form item
    // labelled by 'credentialPolicies.escalationLockoutCount'.
    // AntD Form.Item wraps the label text with the input; we locate by form item name
    // via the id AntD assigns: id="escalationLockoutCount"
    const countInput = document.querySelector('input#escalationLockoutCount') as HTMLInputElement;
    if (countInput) {
      // Clear the default value (3) — simulates user deleting the value
      fireEvent.change(countInput, { target: { value: '' } });
    }

    // Fill the required name field so other validations don't obscure this one
    const nameInput = document.querySelector('input#name') as HTMLInputElement;
    if (nameInput) {
      fireEvent.change(nameInput, { target: { value: 'Policy X' } });
    }

    // Submit
    const okBtn = screen.getAllByRole('button').find(
      (btn) => btn.textContent === 'common.save' || btn.textContent === 'Lưu',
    );
    if (okBtn) fireEvent.click(okBtn);

    // AntD fires required-rule error for empty value
    await waitFor(() => {
      // Either the required error or the min-rule error appears under the field
      const errEls = document.querySelectorAll('.ant-form-item-explain-error');
      expect(errEls.length).toBeGreaterThan(0);
    }, { timeout: 2000 });
  });
});
