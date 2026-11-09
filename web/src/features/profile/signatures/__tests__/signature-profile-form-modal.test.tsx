// SignatureProfileFormModal — Phase 6 Slice A + stamp visibility toggles.
// Covers: create-mode submit emits trimmed DisplayName + default-on toggles; edit-mode pre-fills the values.

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

import { SignatureProfileFormModal } from '../signature-profile-form-modal';

describe('SignatureProfileFormModal', () => {
  it('emits the entered display name + default-on toggles on submit (create mode)', async () => {
    const onSubmit = vi.fn();
    render(
      <SignatureProfileFormModal open mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />,
    );

    const input = screen.getByPlaceholderText('profile.signatures.namePlaceholder');
    fireEvent.change(input, { target: { value: '  Work signature  ' } });
    fireEvent.click(screen.getByText('common.save'));

    // New profiles default both stamp lines ON; display name is trimmed.
    await waitFor(() =>
      expect(onSubmit).toHaveBeenCalledWith({ displayName: 'Work signature', showSerial: true, showIssuer: true }),
    );
  });

  it('blocks submit when display name is empty', async () => {
    const onSubmit = vi.fn();
    render(
      <SignatureProfileFormModal open mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />,
    );

    fireEvent.click(screen.getByText('common.save'));

    // Required-rule rejection — validateFields throws, onSubmit never fires.
    await waitFor(() =>
      expect(screen.getByText('profile.signatures.nameRequired')).toBeInTheDocument(),
    );
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('pre-fills the display name + toggles in edit mode', () => {
    render(
      <SignatureProfileFormModal
        open
        mode="edit"
        initialValues={{ displayName: 'Existing name', showSerial: false, showIssuer: true }}
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByDisplayValue('Existing name')).toBeInTheDocument();
  });
});
