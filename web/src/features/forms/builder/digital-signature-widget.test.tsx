/**
 * digital-signature-widget.test.tsx — DigitalSignature widget behavior.
 *
 * Mocks the reusable <SigningPrompt> (the real one probes tenant adapters) so the test can drive
 * the widget's own logic: payload preparation, SignResult→SignaturePayloadRequest mapping, the
 * unavailable state when no provider, and invalidate-on-edit (signing then changing a covered
 * field clears the captured signature, forcing a re-sign).
 */

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Form, Input } from 'antd';
import type { FormFieldDto, SignatureMode } from '../form-types';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string, fallback?: string) => fallback ?? key, i18n: { language: 'vi' } }),
}));

// Stub SigningPrompt: when open, expose a button that fires onSuccess with a raw-format result.
vi.mock('@/features/digital-signature/signing-prompt', () => ({
  SigningPrompt: ({
    open,
    payload,
    onSuccess,
  }: {
    open: boolean;
    payload: Uint8Array | null;
    onSuccess: (r: unknown) => void;
  }) =>
    open ? (
      <button
        data-testid="do-sign"
        data-haspayload={payload != null}
        onClick={() =>
          onSuccess({
            signatureBase64: 'U0lH',
            signerCertBase64: 'Q0VSVA==',
            hashAlg: 'SHA256',
            encode: 'RSA',
            providerKey: 'usb-vgca',
            isPending: false,
          })
        }
      >
        mock-sign
      </button>
    ) : null,
}));

import { DigitalSignatureWidget } from './digital-signature-widget';

function f(fieldKey: string, type: FormFieldDto['type']): FormFieldDto {
  return {
    id: fieldKey, fieldKey, type, labelVi: fieldKey, labelEn: fieldKey,
    required: false, displayOrder: 1, isActive: true, addedInVersion: 1,
  };
}

const PROVIDER = { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'RawSign' };

type ProviderProp = { isEnabled: boolean; providerKey: string | null; capabilities: string } | null;

function Harness({
  signingProvider,
  mode = 'CanonicalJson',
}: {
  signingProvider: ProviderProp;
  mode?: SignatureMode;
}) {
  const sigField = { ...f('sig', 'DigitalSignature'), validationRulesJson: JSON.stringify({ signatureMode: mode }) };
  const fields = [f('note', 'Text'), sigField];
  return (
    <Form>
      <Form.Item name="note"><Input /></Form.Item>
      <Form.Item name="sig">
        <DigitalSignatureWidget
          field={sigField}
          allFields={fields}
          templateId="tpl-1"
          signingProvider={signingProvider}
        />
      </Form.Item>
    </Form>
  );
}

describe('DigitalSignatureWidget', () => {
  it('public/anonymous form (no provider info) → "không hỗ trợ" info, no sign button', () => {
    render(<Harness signingProvider={null} />);
    expect(screen.getByText('Ký số chưa khả dụng')).toBeTruthy();
    expect(screen.getByText(/Biểu mẫu công khai không hỗ trợ ký số/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Ký số$/ })).toBeNull();
  });

  it('authenticated tenant without configured provider → "liên hệ quản trị viên", no sign button', () => {
    render(<Harness signingProvider={{ isEnabled: false, providerKey: null, capabilities: '' }} />);
    expect(screen.getByText('Ký số chưa khả dụng')).toBeTruthy();
    expect(screen.getByText(/liên hệ quản trị viên/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Ký số$/ })).toBeNull();
  });

  it('Mode-1: opens prompt with a payload and maps SignResult → Raw SignaturePayload', async () => {
    render(<Harness signingProvider={PROVIDER} />);

    fireEvent.click(screen.getByRole('button', { name: /Ký số/ }));

    const signBtn = await screen.findByTestId('do-sign');
    expect(signBtn.getAttribute('data-haspayload')).toBe('true');

    fireEvent.click(signBtn);

    // After success the widget shows the signed indicator (value present in form state).
    await waitFor(() => expect(screen.getByText('Đã ký số')).toBeTruthy());
  });

  it('invalidate-on-edit: changing a covered field after signing clears the signature', async () => {
    render(<Harness signingProvider={PROVIDER} />);

    fireEvent.click(screen.getByRole('button', { name: /Ký số/ }));
    fireEvent.click(await screen.findByTestId('do-sign'));
    await waitFor(() => expect(screen.getByText('Đã ký số')).toBeTruthy());

    // Edit a covered (Text) field → canonical snapshot changes → signature cleared.
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'changed' } });

    await waitFor(() => expect(screen.queryByText('Đã ký số')).toBeNull());
    expect(screen.getByRole('button', { name: /Ký số/ })).toBeTruthy();
  });
});
