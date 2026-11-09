// TC-FE-API-001: Renders key list with masked values
// TC-FE-API-002: Create key shows full key once (modal opens)

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// Mock create modal — tested separately
vi.mock('@/features/api-keys/api-key-create-modal', () => ({
  ApiKeyCreateModal: ({ open }: { open: boolean }) =>
    open ? createElement('div', { 'data-testid': 'create-modal' }, 'create-modal') : null,
}));

// Controllable API key hooks
const mockUseApiKeys = vi.fn(() => ({ data: [] as ApiKeyDto[], isFetching: false }));

vi.mock('@/features/api-keys/api-key-api', () => ({
  useApiKeys: () => mockUseApiKeys(),
  useRevokeApiKey: () => ({ mutate: vi.fn(), isPending: false }),
}));

import { ApiKeyListPage } from '@/features/api-keys/api-key-list-page';
import type { ApiKeyDto } from '@/features/api-keys/api-key-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// TC-FE-API-001: Renders key list with masked values
describe('ApiKeyListPage — TC-FE-API-001: renders key list with masked values', () => {
  it('renders without crashing', () => {
    const { container } = render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.apiKeys.title')).toBeTruthy();
  });

  it('renders create API key button', () => {
    render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.apiKeys.create')).toBeTruthy();
  });

  it('renders table element', () => {
    const { container } = render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });

  it('renders key prefix with masking suffix characters', () => {
    mockUseApiKeys.mockReturnValue({
      data: [
        {
          id: 'key-1',
          name: 'Integration Key',
          prefix: 'aqt_abc123',
          scopes: ['cases.read', 'files.read'],
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          expiresAt: undefined,
        },
      ],
      isFetching: false,
    });
    render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Integration Key')).toBeTruthy();
    // Prefix rendered as: `{prefix}••••••••••••••••`
    const { container } = render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    const codeEl = container.querySelector('code');
    expect(codeEl?.textContent).toContain('aqt_abc123');
    expect(codeEl?.textContent).toContain('••••••••••••••••');
  });

  it('renders scope tags for each key', () => {
    mockUseApiKeys.mockReturnValue({
      data: [
        {
          id: 'key-2',
          name: 'Read Only Key',
          prefix: 'aqt_xyz',
          scopes: ['cases.read'],
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          expiresAt: undefined,
        },
      ],
      isFetching: false,
    });
    render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(screen.getAllByText('cases.read').length).toBeGreaterThan(0);
  });
});

// TC-FE-API-002: Create key modal opens and shows full key once
describe('ApiKeyListPage — TC-FE-API-002: create key shows modal', () => {
  it('create button click opens the modal', () => {
    mockUseApiKeys.mockReturnValue({ data: [], isFetching: false });
    render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    const createBtn = screen.getByText('page.admin.apiKeys.create');
    fireEvent.click(createBtn);
    expect(screen.getByTestId('create-modal')).toBeTruthy();
  });

  it('modal is not shown initially before button click', () => {
    mockUseApiKeys.mockReturnValue({ data: [], isFetching: false });
    render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(screen.queryByTestId('create-modal')).toBeNull();
  });
});
