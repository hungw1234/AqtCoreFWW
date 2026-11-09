/**
 * form-data-canonicalizer.test.ts — FE↔BE byte-parity guard.
 *
 * These vectors are the SAME ones pinned in BE FormDataCanonicalizerTests.cs. If the FE
 * canonicalizer ever drifts from BE, a live signature would false-reject at verify-at-ingest;
 * this suite catches the drift offline. Each expected value is the UTF-8 of the canonical JSON
 * the BE test asserts — compared as hex so any byte difference is visible.
 */

import { describe, it, expect } from 'vitest';
import { canonicalizeMode1, buildCanonicalDataObject } from './form-data-canonicalizer';
import type { CanonicalField } from './form-data-canonicalizer';
import type { FormFieldType } from '../forms/form-types';

function field(fieldKey: string, type: FormFieldType, isActive = true): CanonicalField {
  return { fieldKey, type, isActive };
}

function hex(bytes: Uint8Array): string {
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0').toUpperCase()).join('');
}

function utf8Hex(s: string): string {
  return hex(new TextEncoder().encode(s));
}

function asString(bytes: Uint8Array): string {
  return new TextDecoder().decode(bytes);
}

describe('canonicalizeMode1 — pinned byte vectors (BE contract)', () => {
  it('empty data → {}', () => {
    const bytes = canonicalizeMode1({}, [field('name', 'Text')]);
    expect(asString(bytes)).toBe('{}');
  });

  it('empty fields → {}', () => {
    const bytes = canonicalizeMode1({ name: 'Alice' }, []);
    expect(asString(bytes)).toBe('{}');
  });

  it('single text field → {"name":"Alice"}', () => {
    const bytes = canonicalizeMode1({ name: 'Alice' }, [field('name', 'Text')]);
    expect(hex(bytes)).toBe(utf8Hex('{"name":"Alice"}'));
  });

  it('two fields ordinal-sorted, number coerced → {"age":"30","name":"Bob"}', () => {
    const bytes = canonicalizeMode1(
      { name: 'Bob', age: 30 },
      [field('name', 'Text'), field('age', 'Number')],
    );
    expect(hex(bytes)).toBe(utf8Hex('{"age":"30","name":"Bob"}'));
  });

  it('excludes Formula + UI-only → {"title":"report"}', () => {
    const bytes = canonicalizeMode1(
      { title: 'report', calc: '999', section: 'header' },
      [field('title', 'Text'), field('calc', 'Formula'), field('section', 'Section')],
    );
    expect(hex(bytes)).toBe(utf8Hex('{"title":"report"}'));
  });

  it('Vietnamese value literal UTF-8 (no \\uXXXX) → {"city":"Hà Nội"}', () => {
    const bytes = canonicalizeMode1({ city: 'Hà Nội' }, [field('city', 'Text')]);
    const s = asString(bytes);
    expect(s).toBe('{"city":"Hà Nội"}');
    expect(s).not.toContain('\\u');
    expect(hex(bytes)).toBe(utf8Hex('{"city":"Hà Nội"}'));
  });

  it('boxed bool true → {"flag":"true"} (string, not JSON boolean)', () => {
    const bytes = canonicalizeMode1({ flag: true }, [field('flag', 'Boolean')]);
    expect(asString(bytes)).toBe('{"flag":"true"}');
  });

  it('boxed bool false → {"flag":"false"}', () => {
    const bytes = canonicalizeMode1({ flag: false }, [field('flag', 'Boolean')]);
    expect(asString(bytes)).toBe('{"flag":"false"}');
  });

  it('decimal number invariant string → {"amount":"1234.56"}', () => {
    const bytes = canonicalizeMode1({ amount: 1234.56 }, [field('amount', 'Number')]);
    expect(asString(bytes)).toBe('{"amount":"1234.56"}');
  });

  it('number 98.5 → {"score":"98.5"}', () => {
    const bytes = canonicalizeMode1({ score: 98.5 }, [field('score', 'Number')]);
    expect(asString(bytes)).toBe('{"score":"98.5"}');
  });
});

describe('canonicalizeMode1 — structural rules', () => {
  it('null value omits the key', () => {
    const s = asString(
      canonicalizeMode1({ name: 'Alice', age: null }, [field('name', 'Text'), field('age', 'Number')]),
    );
    expect(s).toContain('"name"');
    expect(s).not.toContain('"age"');
    expect(s).not.toContain('null');
  });

  it('all-null → {}', () => {
    const s = asString(
      canonicalizeMode1({ a: null, b: null }, [field('a', 'Text'), field('b', 'Number')]),
    );
    expect(s).toBe('{}');
  });

  it('keys sorted ordinal: Apple < mango < zebra (uppercase before lowercase)', () => {
    const s = asString(
      canonicalizeMode1(
        { zebra: 'z', Apple: 'a', mango: 'm' },
        [field('zebra', 'Text'), field('Apple', 'Text'), field('mango', 'Text')],
      ),
    );
    expect(s.indexOf('"Apple"')).toBeLessThan(s.indexOf('"mango"'));
    expect(s.indexOf('"mango"')).toBeLessThan(s.indexOf('"zebra"'));
  });

  it('all 9 scalar types included', () => {
    const data = {
      f_text: 'hello', f_number: 42, f_date: '2025-01-15', f_textarea: 'multi\nline',
      f_boolean: true, f_richtext: '<p>hi</p>', f_enumref: 'VALUE_A',
      f_internalref: 'ref-123', f_externalref: 'ext-456',
    };
    const fields: CanonicalField[] = [
      field('f_text', 'Text'), field('f_number', 'Number'), field('f_date', 'Date'),
      field('f_textarea', 'Textarea'), field('f_boolean', 'Boolean'), field('f_richtext', 'RichText'),
      field('f_enumref', 'EnumRef'), field('f_internalref', 'InternalRef'), field('f_externalref', 'ExternalRef'),
    ];
    const s = asString(canonicalizeMode1(data, fields));
    for (const key of Object.keys(data)) expect(s).toContain(`"${key}"`);
  });

  it('non-scalar types excluded (File, Signature, DigitalSignature, Table/Address/DateRange)', () => {
    const fields: CanonicalField[] = [
      field('name', 'Text'), field('attach', 'File'), field('png', 'Signature'),
      field('digsig', 'DigitalSignature'), field('items', 'TableField'),
      field('addr', 'AddressField'), field('period', 'DateRange'),
    ];
    const data = {
      name: 'Alice', attach: 'file-id', png: 'b64png', digsig: 'ignored',
      items: '[]', addr: '{}', period: '{}',
    };
    const s = asString(canonicalizeMode1(data, fields));
    expect(s).toBe('{"name":"Alice"}');
  });

  it('inactive field excluded', () => {
    const s = asString(
      canonicalizeMode1(
        { kept: 'yes', dropped: 'no' },
        [field('kept', 'Text'), field('dropped', 'Text', false)],
      ),
    );
    expect(s).toContain('"kept"');
    expect(s).not.toContain('"dropped"');
  });

  it('NFC: decomposed and precomposed forms produce identical bytes', () => {
    const nfc = 'ă'; // precomposed U+0103
    const nfd = 'ă'; // a + combining breve
    const a = canonicalizeMode1({ name: nfc }, [field('name', 'Text')]);
    const b = canonicalizeMode1({ name: nfd }, [field('name', 'Text')]);
    expect(hex(a)).toBe(hex(b));
  });
});

describe('buildCanonicalDataObject — shared with Mode-2 wrapper', () => {
  it('returns ordinal-sorted coerced dict', () => {
    const obj = buildCanonicalDataObject(
      { name: 'Bob', age: 25 },
      [field('name', 'Text'), field('age', 'Number')],
    );
    expect(JSON.stringify(obj)).toBe('{"age":"25","name":"Bob"}');
  });
});
