/**
 * form-submit-modal-formula.test.tsx — reactive Formula wiring in the authenticated submit modal.
 *
 * Drives the REAL useFormulaValues orchestration hook (only the API layer is mocked). Locks:
 * (1) editing an input triggers a debounced evaluate that renders the returned value in the
 * disabled Formula widget; (2) the FEEDBACK-LOOP GUARD — a settled edit fires evaluate a bounded
 * number of times (no runaway loop from writing the computed value back into the form);
 * (3) the ONLY compute source is the injected mock — there is no client-side expression eval.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { FormTemplateDto, FormFieldDto } from './form-types';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (_key: string, opts?: { defaultValue?: string }) => opts?.defaultValue ?? _key,
    i18n: { language: 'vi' },
  }),
}));

// Signing hook is irrelevant to Formula — stub it so the modal mounts without a provider probe.
vi.mock('@/features/digital-signature/signature-api', () => ({
  useSigningProviderInfo: () => ({ data: null }),
}));

// Spy on the injected evaluate mutation. mutateAsync resolves to the batch result the BE would send.
const evaluateSpy = vi.fn(async (_args: unknown) => ({ results: { total: 42 } }));
const submitMutate = vi.fn();

vi.mock('./form-api', () => ({
  useSubmitForm: () => ({ mutate: submitMutate, isPending: false }),
  useEvaluateFormulas: () => ({ mutateAsync: evaluateSpy }),
}));

import { FormSubmitModal } from './form-submit-modal';

function field(overrides: Partial<FormFieldDto> & Pick<FormFieldDto, 'fieldKey' | 'type'>): FormFieldDto {
  return {
    id: overrides.fieldKey,
    labelVi: overrides.fieldKey,
    labelEn: overrides.fieldKey,
    required: false,
    displayOrder: 1,
    isActive: true,
    addedInVersion: 1,
    ...overrides,
  };
}

const template: FormTemplateDto = {
  id: 'tpl-guid-1',
  code: 'ORDER',
  name: 'Order',
  status: 'Active',
  version: 1,
  submissionsCount: 0,
  createdAt: '2026-08-04T00:00:00Z',
  fields: [
    field({ fieldKey: 'qty', type: 'Number', displayOrder: 1 }),
    field({ fieldKey: 'total', type: 'Formula', displayOrder: 2 }),
  ],
};

function renderModal() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <FormSubmitModal template={template} open onClose={() => {}} />
    </QueryClientProvider>
  );
}

describe('FormSubmitModal — reactive Formula field', () => {
  beforeEach(() => {
    evaluateSpy.mockClear();
    submitMutate.mockClear();
    vi.useFakeTimers();
  });
  afterEach(() => vi.useRealTimers());

  it('debounced edit → evaluate fires and the disabled Formula widget shows the computed value', async () => {
    renderModal();

    // The Formula widget renders disabled with the placeholder before any compute resolves.
    const formulaInput = screen.getByPlaceholderText('(computed)') as HTMLInputElement;
    expect(formulaInput.disabled).toBe(true);

    // Type a quantity → antd Form.useWatch picks it up → debounce → evaluate.
    const numberInput = screen.getByRole('spinbutton');
    await act(async () => {
      fireEvent.change(numberInput, { target: { value: '3' } });
    });

    await act(async () => {
      vi.advanceTimersByTime(300);
      await Promise.resolve();
      await Promise.resolve();
    });

    // Compute happened via the injected mock only (no client-side expression evaluation).
    expect(evaluateSpy).toHaveBeenCalled();
    expect(evaluateSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({ templatePublicId: 'tpl-guid-1' })
    );

    // The BE-supplied value is rendered in the disabled widget.
    expect((screen.getByPlaceholderText('(computed)') as HTMLInputElement).value).toBe('42');
  });

  it('FEEDBACK-LOOP GUARD: a single settled edit does NOT trigger a runaway compute loop', async () => {
    renderModal();

    const numberInput = screen.getByRole('spinbutton');
    await act(async () => {
      fireEvent.change(numberInput, { target: { value: '7' } });
    });

    // Let the debounce settle and give the resolved compute several extra ticks to (not) re-trigger.
    await act(async () => {
      vi.advanceTimersByTime(300);
      await Promise.resolve();
    });
    const callsAfterSettle = evaluateSpy.mock.calls.length;

    await act(async () => {
      vi.advanceTimersByTime(2000);
      await Promise.resolve();
      await Promise.resolve();
    });

    // If the computed value were written back into the form, useWatch would re-fire compute
    // indefinitely. It is display-only → no additional calls after the value settles.
    expect(evaluateSpy.mock.calls.length).toBe(callsAfterSettle);
    // Bounded: initial mount + one settled edit ≤ 2 calls total, never unbounded.
    expect(callsAfterSettle).toBeLessThanOrEqual(2);
  });
});
