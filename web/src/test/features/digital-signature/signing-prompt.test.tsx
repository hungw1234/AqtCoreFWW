/**
 * signing-prompt.test.tsx — local-agent probe outcome → UI message mapping (C2).
 *
 * Drives the real usb-vgca adapter (registered via SigningPrompt's side-effect imports)
 * by stubbing window.vgca_get_version, then asserts each probe outcome routes to the right
 * message: 'unreachable' shows the retry affordance and NOT the install guide; 'not-installed'
 * shows the install guide. This is the regression the three-state probe was introduced to fix —
 * a transient probe failure must no longer surface "install the software".
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string, fallback?: string) => fallback ?? key, i18n: { language: 'vi' } }),
}));

// vgca globals must exist before the adapter self-registers on import.
vi.stubGlobal('vgca_sign_msg', vi.fn());
vi.stubGlobal('vgca_get_version', vi.fn());

import { SigningPrompt } from '@/features/digital-signature/signing-prompt';

// RequiresLocalAgent in capabilities makes the prompt run the agent probe.
const baseProps = {
  open: true,
  providerKey: 'usb-vgca',
  capabilities: 'RawSign, RequiresLocalAgent',
  payload: new Uint8Array([1, 2, 3]),
  onSuccess: () => {},
  onCancel: () => {},
};

describe('SigningPrompt — agent probe → UI mapping', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("'unreachable' (probe error) shows retry, not the install guide", async () => {
    // Plugin present but returns a non-zero Status → probe resolves 'unreachable'.
    vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
      Promise.resolve().then(() => cb(JSON.stringify({ Status: 500, Message: 'err' })));
    });

    render(<SigningPrompt {...baseProps} />);

    await waitFor(() =>
      expect(screen.getByText('Không kết nối được tới agent ký số')).toBeTruthy(),
    );
    // Retry affordance present; misleading install-guide absent.
    expect(screen.getByRole('button', { name: /Thử lại/ })).toBeTruthy();
    expect(screen.queryByText('Không kết nối được VGCASignService')).toBeNull();
    expect(screen.queryByText(/Tải phần mềm VGCA/)).toBeNull();
  });

  it("'not-installed' (plugin absent) shows the install guide, not retry", async () => {
    vi.stubGlobal('vgca_get_version', undefined);

    render(<SigningPrompt {...baseProps} />);

    await waitFor(() =>
      expect(screen.getByText('Không kết nối được VGCASignService')).toBeTruthy(),
    );
    expect(screen.getByText(/Tải phần mềm VGCA/)).toBeTruthy();
    expect(screen.queryByText('Không kết nối được tới agent ký số')).toBeNull();
    expect(screen.queryByRole('button', { name: /Thử lại/ })).toBeNull();
  });
});
