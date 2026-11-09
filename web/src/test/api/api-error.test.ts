import { describe, it, expect } from 'vitest';
import { normalizeError, formatGraphViolations, type GraphViolation } from '@/core/api/api-error';

// Wire fixture mirroring the BE default-deny opt-in PublicMetadata envelope (commit f907e0ca).
// Casing is intentional and load-bearing: outer keys camelCase (anon-type props), inner violation
// `metadata` dict keys stay PascalCase (UnknownGroupCodes) — BE sets no global DictionaryKeyPolicy.
// Locked by integration tests WorkflowByGroupProducerAndSaveValidationTests I4/I5.
const graphValidation422Body = {
  message: 'Workflow graph validation failed.',
  errors: [
    {
      code: 'GOV_WFL_017',
      message: 'Workflow graph validation failed: 1 violation(s).',
      metadata: {
        violations: [
          {
            rule: 'R4',
            stateName: 'Approved',
            transitionActionName: 'Submit',
            detail: 'Unknown group codes in ByGroup clause.',
            metadata: { UnknownGroupCodes: ['DOES_NOT_EXIST'] },
          },
        ],
      },
    },
  ],
};

describe('normalizeError — graph violation metadata', () => {
  it('preserves the violations array from errors[0].metadata.violations', () => {
    const result = normalizeError(422, graphValidation422Body);
    expect(result.violations).toHaveLength(1);
    expect(result.violations?.[0]).toMatchObject({
      rule: 'R4',
      stateName: 'Approved',
      transitionActionName: 'Submit',
      detail: 'Unknown group codes in ByGroup clause.',
    });
  });

  it('keeps the PascalCase UnknownGroupCodes key readable on the inner violation metadata', () => {
    const result = normalizeError(422, graphValidation422Body);
    const inner = result.violations?.[0].metadata;
    // Verbatim PascalCase — auto-lowercasing here would silently break the renderer.
    expect(inner?.['UnknownGroupCodes']).toEqual(['DOES_NOT_EXIST']);
  });

  it('leaves violations undefined for a 422 body without metadata (no regression)', () => {
    const plain422 = { errors: [{ code: 'GOV_SYS_002', message: 'Validation failed.' }] };
    const result = normalizeError(422, plain422);
    expect(result.violations).toBeUndefined();
    // existing behaviour intact: per-field message still wins
    expect(result.message).toBe('Validation failed.');
  });
});

describe('formatGraphViolations', () => {
  // Pass-through translate fn returns the defaultValue so the test asserts the rendered Vietnamese text.
  const t = (_key: string, options?: Record<string, unknown>) => String(options?.defaultValue ?? '');

  it('surfaces UnknownGroupCodes inline with the state name', () => {
    const violations: GraphViolation[] = [
      { rule: 'R4', stateName: 'Approved', metadata: { UnknownGroupCodes: ['GROUP_X', 'GROUP_Y'] } },
    ];
    const msg = formatGraphViolations(violations, t);
    expect(msg).toContain('Lưu thất bại — 1 vi phạm:');
    expect(msg).toContain('• Approved: Mã nhóm không tồn tại: GROUP_X, GROUP_Y');
  });

  it('caps the list at 5 and appends a "more" line', () => {
    const violations: GraphViolation[] = Array.from({ length: 7 }, (_, i) => ({
      rule: 'R4',
      stateName: `S${i}`,
      detail: `d${i}`,
    }));
    const msg = formatGraphViolations(violations, t);
    const lines = msg.split('\n');
    // header + 5 capped lines + 1 "more" line = 7
    expect(lines).toHaveLength(7);
    expect(lines[lines.length - 1]).toBe('…và 2 vi phạm khác');
  });

  it('falls back to detail then rule when no UnknownGroupCodes present', () => {
    const violations: GraphViolation[] = [{ rule: 'R2', stateName: 'Draft', detail: 'No handler resolved.' }];
    const msg = formatGraphViolations(violations, t);
    expect(msg).toContain('• Draft: No handler resolved.');
  });

  // transitionActionName bullet rendering — 4 combinations

  it('renders state + transitionActionName as "State → Transition: detail" arrow form', () => {
    const violations: GraphViolation[] = [
      { rule: 'R4', stateName: 'Approved', transitionActionName: 'Submit', detail: 'No handler resolved.' },
    ];
    const msg = formatGraphViolations(violations, t);
    expect(msg).toContain('• Approved → Submit: No handler resolved.');
  });

  it('renders state-only (no transitionActionName) unchanged as "State: detail"', () => {
    const violations: GraphViolation[] = [
      { rule: 'R2', stateName: 'Draft', transitionActionName: null, detail: 'Missing handler.' },
    ];
    const msg = formatGraphViolations(violations, t);
    expect(msg).toContain('• Draft: Missing handler.');
    // Must NOT contain arrow — no regression on the state-only shape
    expect(msg).not.toContain('→');
  });

  it('renders transition-only (no stateName) as "Transition: detail" without leading arrow', () => {
    const violations: GraphViolation[] = [
      { rule: 'R7', stateName: null, transitionActionName: 'Approve', detail: 'No assignee pool.' },
    ];
    const msg = formatGraphViolations(violations, t);
    expect(msg).toContain('• Approve: No assignee pool.');
    expect(msg).not.toContain('→');
  });

  it('renders neither-state-nor-transition as bare "• detail"', () => {
    const violations: GraphViolation[] = [
      { rule: 'R9', stateName: null, transitionActionName: null, detail: 'Graph is disconnected.' },
    ];
    const msg = formatGraphViolations(violations, t);
    expect(msg).toContain('• Graph is disconnected.');
    // Must not prefix an empty label with a colon
    expect(msg).not.toMatch(/^• :/m);
  });
});
