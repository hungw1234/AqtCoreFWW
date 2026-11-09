/**
 * signature-config-form-signflowmode.test.tsx
 *
 * Pins that the admin signature-config modal actually FORWARDS the per-tenant remote sign-flow choice
 * (signFlowMode) through validateFields → onSave. A field whose Form.Item name does not match the form-values
 * key silently drops to undefined and the modal would default it away — exactly the "field never reaches the wire"
 * false-green class. This test renders the real modal, edits the flow select, saves, and asserts the payload.
 */

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, fallback?: string) => (typeof fallback === 'string' ? fallback : key),
    i18n: { language: 'vi' },
  }),
}));

import { SignatureConfigFormModal } from '../../../features/digital-signature/signature-config-form-modal';
import type {
  TenantSignatureConfigDto,
  AvailableProviderDto,
} from '../../../features/digital-signature/signature-types';

const PROVIDERS: AvailableProviderDto[] = [
  { providerKey: 'remote-viettel-mysign', displayName: 'Viettel Mysign', capabilities: 'ServerSideSigning, RawSign' },
];

function makeConfig(overrides: Partial<TenantSignatureConfigDto> = {}): TenantSignatureConfigDto {
  return {
    tenantId: '00000000-0000-0000-0000-000000000001',
    providerKey: 'remote-viettel-mysign',
    apiCredentialsRef: null,
    allowedCaRootThumbprints: '',
    ocspMode: 'Soft',
    isEnabled: true,
    signFlowMode: 'Async',
    rowVersion: 'AAAAAAAAB9E=',
    createdAt: '2026-06-19T00:00:00Z',
    updatedAt: null,
    ...overrides,
  };
}

describe('SignatureConfigFormModal — signFlowMode wiring', () => {
  it('forwards the edited signFlowMode (Async → Sync) in the onSave payload', async () => {
    const onSave = vi.fn();
    render(
      <SignatureConfigFormModal
        open
        config={makeConfig({ signFlowMode: 'Async' })}
        providers={PROVIDERS}
        isSubmitting={false}
        onSave={onSave}
        onCancel={() => {}}
      />,
    );

    // The flow-mode select shows the current value (Async label). Switch it to Sync.
    const asyncLabel = await screen.findByText(/Async/);
    fireEvent.mouseDown(asyncLabel);
    const syncOption = await screen.findByText(/Sync — chờ OTP/);
    fireEvent.click(syncOption);

    fireEvent.click(screen.getByText('Lưu'));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ signFlowMode: 'Sync' }));
  });

  it('defaults a new (no-config) form to Async in the onSave payload', async () => {
    const onSave = vi.fn();
    render(
      <SignatureConfigFormModal
        open
        config={null}
        providers={PROVIDERS}
        isSubmitting={false}
        onSave={onSave}
        onCancel={() => {}}
      />,
    );

    // Pick a provider (required) then save — flow mode should default to Async without touching the select.
    const providerSelect = screen.getByText(/Chọn nhà cung cấp/);
    fireEvent.mouseDown(providerSelect);
    fireEvent.click(await screen.findByText('Viettel Mysign'));

    fireEvent.click(screen.getByText('Lưu'));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ signFlowMode: 'Async' }));
  });
});
