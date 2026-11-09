import { describe, it, expect } from 'vitest';
import { foldDiacritics } from '@/shared/utils/fold-diacritics';

// TC-FE-UTIL-FOLD-001: combining-mark categories strip correctly.
// Each case guards a distinct Unicode trap — if the mark range or đ-map is
// narrowed, exactly one of these flips to red.
describe('foldDiacritics — Vietnamese mark categories', () => {
  it('TC-FE-UTIL-FOLD-001: dot-below (ệ) — "Nghệ An" → "nghe an"', () => {
    expect(foldDiacritics('Nghệ An')).toBe('nghe an');
  });

  it('horn (ư/ờ) — "Phường" → "phuong"', () => {
    expect(foldDiacritics('Phường')).toBe('phuong');
  });

  it('breve (ă) — "Đắk Lắk" → "dak lak"', () => {
    expect(foldDiacritics('Đắk Lắk')).toBe('dak lak');
  });

  it('hook-above (ả) — "Quảng Trị" → "quang tri"', () => {
    expect(foldDiacritics('Quảng Trị')).toBe('quang tri');
  });

  it('tilde (ã/ễ) — "Mã Đà" / "Diễn Châu" strip cleanly', () => {
    expect(foldDiacritics('Mã Đà')).toBe('ma da');
    expect(foldDiacritics('Diễn Châu')).toBe('dien chau');
  });

  // The đ-map is the silent-regression guard: NFD does NOT decompose U+0111,
  // so a mark-strip-only version would leave "đak lak" and fail this case.
  it('đ has no NFD form — lowercase đ maps to d', () => {
    expect(foldDiacritics('đa')).toBe('da');
  });

  it('uppercase Đ folds via lowercase-first → "DAK" / "Đắk" both → "dak"', () => {
    expect(foldDiacritics('DAK')).toBe('dak');
    expect(foldDiacritics('Đắk')).toBe('dak');
  });
});

// TC-FE-UTIL-FOLD-002: empty/null path returns '' so '' .includes('') === true
// → empty search box shows all options (desired Select behavior).
describe('foldDiacritics — empty inputs', () => {
  it('TC-FE-UTIL-FOLD-002: null → ""', () => {
    expect(foldDiacritics(null)).toBe('');
  });

  it('undefined → ""', () => {
    expect(foldDiacritics(undefined)).toBe('');
  });

  it('empty string → ""', () => {
    expect(foldDiacritics('')).toBe('');
  });
});

// TC-FE-UTIL-FOLD-003: the real consumer contract — accent-free input matches
// accented label (the filterOption use in address-field-widget).
describe('foldDiacritics — search-match contract', () => {
  it('TC-FE-UTIL-FOLD-003: "nghe" matches "Nghệ An"', () => {
    expect(foldDiacritics('Nghệ An').includes(foldDiacritics('nghe'))).toBe(true);
  });

  it('"dak" matches "Đắk Lắk"', () => {
    expect(foldDiacritics('Đắk Lắk').includes(foldDiacritics('dak'))).toBe(true);
  });

  it('leading/trailing whitespace in input is trimmed', () => {
    expect(foldDiacritics('  Hà Nội  ')).toBe('ha noi');
  });

  it('plain ASCII passes through unchanged (lowercased)', () => {
    expect(foldDiacritics('Hanoi')).toBe('hanoi');
  });
});
