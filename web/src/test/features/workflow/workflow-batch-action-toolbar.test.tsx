// T5-18: Confirm dialog guard for >20 selections in batch transition toolbar.
// Soft threshold below MAX_BATCH=50 hard cap; prevents accidental mass transitions.
// - count <= 20 → action button opens transition modal directly (no confirm)
// - count  > 20 → Modal.confirm intercepts; onOk opens modal, onCancel does not

import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { createElement } from 'react';
import { Modal } from 'antd';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, opts?: Record<string, unknown>) => {
      if (opts && 'count' in opts) return `${key}:${opts.count}`;
      if (opts && 'max' in opts) return `${key}:${opts.max}`;
      return key;
    },
  }),
}));

// Avoid QueryClient dependency — batch mutation never invoked in these tests
vi.mock('@/features/workflow/workflow-api', () => ({
  useBatchTransition: () => ({
    mutateAsync: vi.fn(),
    isPending: false,
  }),
}));

// Result dialog unused in confirm-guard scope
vi.mock('@/features/workflow/workflow-batch-result-dialog', () => ({
  WorkflowBatchResultDialog: () => null,
}));

import { WorkflowBatchActionToolbar } from '@/features/workflow/workflow-batch-action-toolbar';

function makeIds(n: number): string[] {
  return Array.from({ length: n }, (_, i) => `inst-${i}`);
}

// Button has ThunderboltOutlined icon → accessible name includes icon aria-label; match by substring.
const actionButtonName = /workflow\.batch\.actionButton/;

describe('WorkflowBatchActionToolbar — confirm guard (T5-18)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('count <= 20: action click opens transition modal directly, no Modal.confirm', async () => {
    const confirmSpy = vi.spyOn(Modal, 'confirm');

    render(
      createElement(WorkflowBatchActionToolbar, {
        selectedInstanceIds: makeIds(20),
        onClearSelection: vi.fn(),
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: actionButtonName }));

    expect(confirmSpy).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(screen.getByText('workflow.batch.modalHint')).toBeTruthy();
    });
  });

  it('count > 20: action click triggers Modal.confirm with count-aware content', () => {
    const confirmSpy = vi.spyOn(Modal, 'confirm').mockImplementation(
      (() => ({ destroy: vi.fn(), update: vi.fn() })) as unknown as typeof Modal.confirm,
    );

    render(
      createElement(WorkflowBatchActionToolbar, {
        selectedInstanceIds: makeIds(21),
        onClearSelection: vi.fn(),
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: actionButtonName }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    const config = confirmSpy.mock.calls[0][0] as { title: unknown; content: unknown };
    expect(config.title).toBe('workflow.batch.confirmTitle');
    expect(String(config.content)).toContain('21');
  });

  it('count > 20: onOk from confirm opens transition modal', async () => {
    let capturedOnOk: (() => void) | undefined;
    vi.spyOn(Modal, 'confirm').mockImplementation(((config: { onOk?: () => void }) => {
      capturedOnOk = config.onOk;
      return { destroy: vi.fn(), update: vi.fn() };
    }) as unknown as typeof Modal.confirm);

    render(
      createElement(WorkflowBatchActionToolbar, {
        selectedInstanceIds: makeIds(30),
        onClearSelection: vi.fn(),
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: actionButtonName }));

    expect(capturedOnOk).toBeDefined();
    capturedOnOk!();

    await waitFor(() => {
      expect(screen.getByText('workflow.batch.modalHint')).toBeTruthy();
    });
  });

  it('count > 20: skipping onOk (user cancels) keeps transition modal closed', () => {
    vi.spyOn(Modal, 'confirm').mockImplementation(
      (() => ({ destroy: vi.fn(), update: vi.fn() })) as unknown as typeof Modal.confirm,
    );

    render(
      createElement(WorkflowBatchActionToolbar, {
        selectedInstanceIds: makeIds(25),
        onClearSelection: vi.fn(),
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: actionButtonName }));

    // Never invoke onOk — transition modal must not be in DOM
    expect(screen.queryByText('workflow.batch.modalHint')).toBeNull();
  });
});
