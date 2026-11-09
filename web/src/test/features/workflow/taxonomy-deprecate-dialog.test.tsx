// TC-FE-WFT-DEP-001: Dialog disabled submit until confirmCode matches AND reason ≥10 chars
// TC-FE-WFT-DEP-002: ConfirmCode match is case-SENSITIVE (per plan R7 + BE Ordinal compare)
// TC-FE-WFT-DEP-003: Successful submit passes {confirmCode, reason} to onConfirm
// TC-FE-WFT-DEP-004: onCancel resets form (next open sees empty fields)

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createElement, type ReactNode } from 'react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, opts?: Record<string, unknown> | string) =>
      typeof opts === 'string' ? opts : key,
    i18n: { changeLanguage: vi.fn() },
  }),
}));

import { TaxonomyDeprecateDialog } from '@/features/workflow/taxonomy-deprecate-dialog';

function renderDialog(node: ReactNode) {
  return render(createElement('div', null, node));
}

describe('TaxonomyDeprecateDialog', () => {
  beforeEach(() => vi.clearAllMocks());

  // TC-FE-WFT-DEP-002
  it('rejects case-mismatched confirmCode with validation error', async () => {
    const onConfirm = vi.fn();
    const user = userEvent.setup();

    renderDialog(
      <TaxonomyDeprecateDialog open code="Draft" onConfirm={onConfirm} onCancel={vi.fn()} />,
    );

    await user.type(
      await screen.findByLabelText(/workflow.taxonomy.admin.confirmCodeLabel/i),
      'draft', // lowercase — should fail
    );
    await user.type(
      screen.getByLabelText(/workflow.taxonomy.admin.reasonLabel/i),
      'This is a valid reason with enough length',
    );

    // Find the OK button ("Archive")
    const buttons = screen.getAllByRole('button');
    const okButton = buttons.find((b) => b.textContent?.includes('workflow.taxonomy.admin.deprecate'));
    expect(okButton).toBeDefined();
    await user.click(okButton!);

    // onConfirm must NOT fire because confirmCode is case-mismatched
    await waitFor(() => {
      expect(screen.getByText(/workflow.taxonomy.admin.confirmCodeMismatch/i)).toBeInTheDocument();
    });
    expect(onConfirm).not.toHaveBeenCalled();
  });

  // TC-FE-WFT-DEP-001 + DEP-003
  it('submits successfully when confirmCode matches exactly and reason ≥10 chars', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();

    renderDialog(
      <TaxonomyDeprecateDialog open code="Draft" onConfirm={onConfirm} onCancel={vi.fn()} />,
    );

    await user.type(
      await screen.findByLabelText(/workflow.taxonomy.admin.confirmCodeLabel/i),
      'Draft',
    );
    await user.type(
      screen.getByLabelText(/workflow.taxonomy.admin.reasonLabel/i),
      'Ten chars+',
    );

    const buttons = screen.getAllByRole('button');
    const okButton = buttons.find((b) => b.textContent?.includes('workflow.taxonomy.admin.deprecate'));
    await user.click(okButton!);

    await waitFor(() => {
      expect(onConfirm).toHaveBeenCalledWith({
        confirmCode: 'Draft',
        reason: 'Ten chars+',
      });
    });
  });

  // TC-FE-WFT-DEP-001 partial — short reason
  it('rejects reason shorter than 10 characters', async () => {
    const onConfirm = vi.fn();
    const user = userEvent.setup();

    renderDialog(
      <TaxonomyDeprecateDialog open code="Draft" onConfirm={onConfirm} onCancel={vi.fn()} />,
    );

    await user.type(
      await screen.findByLabelText(/workflow.taxonomy.admin.confirmCodeLabel/i),
      'Draft',
    );
    await user.type(
      screen.getByLabelText(/workflow.taxonomy.admin.reasonLabel/i),
      'too short', // 9 chars
    );

    const buttons = screen.getAllByRole('button');
    const okButton = buttons.find((b) => b.textContent?.includes('workflow.taxonomy.admin.deprecate'));
    await user.click(okButton!);

    await waitFor(() => {
      expect(screen.getByText(/workflow.taxonomy.admin.reasonTooShort/i)).toBeInTheDocument();
    });
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
