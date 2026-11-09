/**
 * use-pdf-text-search.test.ts
 *
 * Locks the SECURITY-CRITICAL escaping invariant of the search hook's customTextRenderer. The
 * viewer component test mocks react-pdf's <Page>, so customTextRenderer is never executed there —
 * this hook test covers the path where untrusted PDF text is turned into innerHTML.
 *
 * Asserts:
 *   - customTextRenderer is undefined for an empty term (default text layer, no highlighting).
 *   - PDF text is HTML-escaped (no live markup can be injected via document content).
 *   - Matches are wrapped in <mark data-pdf-mark>.
 *   - The term is treated literally (regex metacharacters escaped) — 'a.c' must not match 'axc'.
 */

import { describe, it, expect } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePdfTextSearch } from '@/features/digital-signature/signed-document/use-pdf-text-search';

describe('usePdfTextSearch — customTextRenderer', () => {
  it('is undefined when the term is empty (default text layer)', () => {
    const { result } = renderHook(() => usePdfTextSearch());
    expect(result.current.customTextRenderer).toBeUndefined();
  });

  it('escapes untrusted PDF text so no live markup is injected (XSS guard)', () => {
    const { result } = renderHook(() => usePdfTextSearch());
    act(() => result.current.setTerm('742'));

    const renderText = result.current.customTextRenderer;
    expect(renderText).toBeDefined();

    const html = renderText!({ str: '<img src=x onerror=alert(1)>742' });
    // The document's angle brackets must be escaped — no live <img> tag reaches innerHTML.
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
    // The match itself is highlighted.
    expect(html).toContain('<mark data-pdf-mark>742</mark>');
  });

  it('treats the term literally — regex metacharacters are escaped', () => {
    const { result } = renderHook(() => usePdfTextSearch());
    act(() => result.current.setTerm('a.c'));

    const renderText = result.current.customTextRenderer!;
    // '.' must be a literal dot, not a regex wildcard → 'axc' is NOT a match.
    expect(renderText({ str: 'axc' })).toBe('axc');
    expect(renderText({ str: 'a.c' })).toContain('<mark data-pdf-mark>a.c</mark>');
  });
});
