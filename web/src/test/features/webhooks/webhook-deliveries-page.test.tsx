// TC-FE-WH-001: Renders delivery log with subscription selector

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// Controllable webhook API
const mockUseWebhookSubscriptions = vi.fn(() => ({ data: [] as WebhookSubscriptionDto[], isLoading: false }));
const mockUseWebhookDeliveries = vi.fn(() => ({
  data: { items: [], totalCount: 0 },
  isFetching: false,
  refetch: vi.fn(),
}));

vi.mock('@/features/webhooks/webhook-api', () => ({
  useWebhookSubscriptions: () => mockUseWebhookSubscriptions(),
  useWebhookDeliveries: () => mockUseWebhookDeliveries(),
  useTestWebhook: () => ({ mutate: vi.fn(), isPending: false }),
}));

import { WebhookDeliveriesPage } from '@/features/webhooks/webhook-deliveries-page';
import type { WebhookSubscriptionDto } from '@/features/webhooks/webhook-api';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// TC-FE-WH-001: Renders delivery log
describe('WebhookDeliveriesPage — TC-FE-WH-001: renders delivery log', () => {
  it('renders without crashing', () => {
    const { container } = render(<WebhookDeliveriesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<WebhookDeliveriesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('webhooks.title')).toBeTruthy();
  });

  it('renders subscription selector card', () => {
    render(<WebhookDeliveriesPage />, { wrapper: makeWrapper() });
    // Select placeholder text rendered via t()
    expect(screen.getByText('webhooks.selectSubscription')).toBeTruthy();
  });

  it('renders prompt text when no subscription selected', () => {
    render(<WebhookDeliveriesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('webhooks.selectPrompt')).toBeTruthy();
  });

  it('renders subscription options when subscriptions are available', () => {
    mockUseWebhookSubscriptions.mockReturnValue({
      data: [
        {
          id: 'sub-1',
          endpointUrl: 'https://api.example.com/webhook',
          eventTypes: ['case.created', 'case.updated'],
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
        },
      ],
      isLoading: false,
    });
    const { container } = render(<WebhookDeliveriesPage />, { wrapper: makeWrapper() });
    // Select element should be present
    const select = container.querySelector('.ant-select');
    expect(select).toBeTruthy();
  });

  it('does not show delivery table when no subscription is selected', () => {
    mockUseWebhookSubscriptions.mockReturnValue({ data: [], isLoading: false });
    const { container } = render(<WebhookDeliveriesPage />, { wrapper: makeWrapper() });
    // Table is only rendered when selectedSubId is truthy
    expect(container.querySelector('table')).toBeNull();
  });
});
