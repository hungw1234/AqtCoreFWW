// TC-DSIG-REG-001: registerAdapter + resolveAdapter returns registered adapter
// TC-DSIG-REG-002: resolveAdapter throws for unknown key
// TC-DSIG-REG-003: getRegisteredKeys returns all registered keys
// TC-DSIG-REG-004: hasAdapter returns true/false correctly
// TC-DSIG-REG-005: registerAdapter overwrites prior registration

import { describe, it, expect } from 'vitest';

// Import after each test using a fresh module to avoid cross-test pollution.
// Since vitest isolates module state per file (not per test), we directly manage
// the registry by testing observable behaviour rather than resetting internals.

import {
  registerAdapter,
  resolveAdapter,
  getRegisteredKeys,
  hasAdapter,
} from '@/features/digital-signature/registry';
import type { SignatureAdapter } from '@/features/digital-signature/registry';

function makeAdapter(id: string): SignatureAdapter {
  return {
    sign: async (_payload, _opts) => ({
      signatureBase64: `sig-${id}`,
      providerKey: id,
      isPending: false,
    }),
  };
}

// Use unique keys per test to avoid cross-test registry pollution
let keyCounter = 0;
function uniqueKey(prefix: string): string {
  return `${prefix}-${++keyCounter}`;
}

// TC-DSIG-REG-001
describe('registry — TC-DSIG-REG-001: register + resolve returns correct adapter', () => {
  it('resolves the registered adapter', async () => {
    const key = uniqueKey('test-provider');
    const adapter = makeAdapter(key);
    registerAdapter(key, adapter);

    const resolved = resolveAdapter(key);
    expect(resolved).toBe(adapter);
  });

  it('resolved adapter.sign returns expected shape', async () => {
    const key = uniqueKey('test-sign');
    registerAdapter(key, makeAdapter(key));

    const result = await resolveAdapter(key).sign(new Uint8Array([1, 2, 3]), {});
    expect(result.signatureBase64).toBe(`sig-${key}`);
    expect(result.providerKey).toBe(key);
    expect(result.isPending).toBe(false);
  });
});

// TC-DSIG-REG-002
describe('registry — TC-DSIG-REG-002: resolveAdapter throws for unknown key', () => {
  it('throws Error with key in message', () => {
    expect(() => resolveAdapter('definitely-not-registered-xyz-999')).toThrow(
      'No adapter registered for provider key: "definitely-not-registered-xyz-999"',
    );
  });
});

// TC-DSIG-REG-003
describe('registry — TC-DSIG-REG-003: getRegisteredKeys includes all registered keys', () => {
  it('includes keys registered so far', () => {
    const key1 = uniqueKey('keys-test-a');
    const key2 = uniqueKey('keys-test-b');
    registerAdapter(key1, makeAdapter(key1));
    registerAdapter(key2, makeAdapter(key2));

    const keys = getRegisteredKeys();
    expect(keys).toContain(key1);
    expect(keys).toContain(key2);
  });
});

// TC-DSIG-REG-004
describe('registry — TC-DSIG-REG-004: hasAdapter returns correct boolean', () => {
  it('returns false before registration', () => {
    expect(hasAdapter('never-registered-abc-123')).toBe(false);
  });

  it('returns true after registration', () => {
    const key = uniqueKey('has-test');
    registerAdapter(key, makeAdapter(key));
    expect(hasAdapter(key)).toBe(true);
  });
});

// TC-DSIG-REG-005
describe('registry — TC-DSIG-REG-005: registerAdapter overwrites prior registration', () => {
  it('second registration replaces first', async () => {
    const key = uniqueKey('overwrite-test');
    const adapterA = makeAdapter('a');
    const adapterB = makeAdapter('b');

    registerAdapter(key, adapterA);
    registerAdapter(key, adapterB);

    const resolved = resolveAdapter(key);
    expect(resolved).toBe(adapterB);

    const result = await resolved.sign(new Uint8Array(), {});
    expect(result.providerKey).toBe('b');
  });
});
