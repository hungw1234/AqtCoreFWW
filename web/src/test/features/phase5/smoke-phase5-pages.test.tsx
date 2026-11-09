// Smoke tests for Phase 5 new feature pages
// TC-FE-P5-RULES-001:    RuleSetListPage renders
// TC-FE-P5-TEMPLATES-001: TemplateListPage renders
// TC-FE-P5-JOBS-001:     JobMonitorPage renders
// TC-FE-P5-COPILOT-001:  CopilotChatPage renders
// TC-FE-P5-CHAT-001:     ChatPage renders
// TC-FE-P5-AIADMIN-001:  AiAdminPage renders

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ──────────────────────────────────────────────────────────────────────────────
// Global mocks
// ──────────────────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'en' } }),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({}),
  useLocation: () => ({ pathname: '/' }),
  useSearch: () => ({}),
  Link: ({ children }: { children: ReactNode }) => children,
}));

vi.mock('@/features/auth/use-auth', () => ({
  useAuth: () => ({
    user: { profile: { tenant_id: 'test-tenant', sub: 'test-user', name: 'Test Admin' } },
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}));

vi.mock('@/core/hooks/use-server-pagination', () => ({
  useServerPagination: () => ({
    antPagination: { current: 1, pageSize: 20 },
    toQueryParams: () => ({ pageNumber: 1, pageSize: 20 }),
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Feature-specific API mocks
// ──────────────────────────────────────────────────────────────────────────────

// Rules — rules embedded in RuleSetDto; decision tables fetched via dedicated endpoint.
vi.mock('@/features/rules/rules-api', () => ({
  useRuleSets: () => ({ data: { items: [], totalCount: 0 }, isFetching: false }),
  useRuleSet: () => ({ data: null, isLoading: false }),
  useDecisionTables: () => ({ data: [], isLoading: false }),
  useCreateRuleSet: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateRuleSet: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useActivateRuleSet: () => ({ mutate: vi.fn(), isPending: false }),
  useDeprecateRuleSet: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteRuleSet: () => ({ mutate: vi.fn(), isPending: false }),
  useTestRuleSet: () => ({ mutate: vi.fn(), isPending: false, data: null, isError: false }),
}));

// Templates
vi.mock('@/features/templates/templates-api', () => ({
  useTemplates: () => ({ data: { items: [], totalCount: 0 }, isFetching: false }),
  useTemplate: () => ({ data: null, isLoading: false }),
  useCreateTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteTemplate: () => ({ mutate: vi.fn(), isPending: false }),
  useGeneratePreview: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock('@/core/hooks/use-debounced-value', () => ({
  useDebouncedValue: (val: string) => val,
}));

// Jobs
vi.mock('@/features/jobs/jobs-api', () => ({
  useJobs: () => ({ data: { items: [], totalCount: 0, counts: {} }, isLoading: false }),
  useFailedJobs: () => ({ data: { items: [], totalCount: 0, counts: {} }, isLoading: false }),
  useJobStats: () => ({ data: null, isLoading: false }),
}));

// Copilot
vi.mock('@/features/copilot/copilot-api', () => ({
  useAiModelProfiles: () => ({ data: [], isLoading: false }),
  streamCopilotMessage: vi.fn(() => vi.fn()),
}));

// Chat / SignalR
vi.mock('@/features/collaboration/chat-api', () => ({
  useConversations: () => ({
    data: { pages: [], pageParams: [] },
    isLoading: false,
    hasNextPage: false,
    fetchNextPage: vi.fn(),
    isFetchingNextPage: false,
  }),
  useChatMessages: () => ({ data: { items: [], totalCount: 0 }, isLoading: false }),
  useSendMessage: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCreateConversation: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock('@/features/collaboration/use-chat-signalr', () => ({
  useChatSignalR: vi.fn(),
}));

// AI Admin
vi.mock('@/features/ai-admin/ai-admin-api', () => ({
  useAiModelProfilesAdmin: () => ({ data: [], isLoading: false }),
  useCreateAiModelProfile: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateAiModelProfile: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteAiModelProfile: () => ({ mutate: vi.fn(), isPending: false }),
  useAiPromptTemplates: () => ({ data: [], isLoading: false }),
  useCreateAiPromptTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateAiPromptTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteAiPromptTemplate: () => ({ mutate: vi.fn(), isPending: false }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Imports — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { RuleSetListPage } from '@/features/rules/rule-set-list-page';
import { TemplateListPage } from '@/features/templates/template-list-page';
import { JobMonitorPage } from '@/features/jobs/job-monitor-page';
import { CopilotChatPage } from '@/features/copilot/copilot-chat-page';
import { ChatPage } from '@/features/collaboration/chat-page';
import { AiAdminPage } from '@/features/ai-admin/ai-admin-page';

// ──────────────────────────────────────────────────────────────────────────────
// Utility
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// Tests
// ──────────────────────────────────────────────────────────────────────────────

describe('RuleSetListPage — TC-FE-P5-RULES-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<RuleSetListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<RuleSetListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('rules.title')).toBeTruthy();
  });

  it('renders add button', () => {
    render(<RuleSetListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('common.add')).toBeTruthy();
  });

  it('renders rule sets table', () => {
    const { container } = render(<RuleSetListPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

describe('TemplateListPage — TC-FE-P5-TEMPLATES-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<TemplateListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<TemplateListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('templates.title')).toBeTruthy();
  });

  it('renders add button', () => {
    render(<TemplateListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('common.add')).toBeTruthy();
  });

  it('renders templates table', () => {
    const { container } = render(<TemplateListPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

describe('JobMonitorPage — TC-FE-P5-JOBS-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<JobMonitorPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<JobMonitorPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('jobs.title')).toBeTruthy();
  });

  it('renders tab navigation', () => {
    render(<JobMonitorPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('jobs.tab.all')).toBeTruthy();
  });
});

describe('CopilotChatPage — TC-FE-P5-COPILOT-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<CopilotChatPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders copilot title', () => {
    render(<CopilotChatPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('copilot.title')).toBeTruthy();
  });

  it('renders send button', () => {
    render(<CopilotChatPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('copilot.sendBtn')).toBeTruthy();
  });

  it('renders model selector', () => {
    render(<CopilotChatPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('copilot.selectModel')).toBeTruthy();
  });
});

describe('ChatPage — TC-FE-P5-CHAT-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<ChatPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders chat title', () => {
    render(<ChatPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('chat.title')).toBeTruthy();
  });

  it('renders conversations label', () => {
    render(<ChatPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('chat.conversationsLabel')).toBeTruthy();
  });
});

describe('AiAdminPage — TC-FE-P5-AIADMIN-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<AiAdminPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<AiAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('aiAdmin.title')).toBeTruthy();
  });

  it('renders models tab', () => {
    render(<AiAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('aiAdmin.tab.models')).toBeTruthy();
  });

  it('renders prompts tab', () => {
    render(<AiAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('aiAdmin.tab.prompts')).toBeTruthy();
  });
});
