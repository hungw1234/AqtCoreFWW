// Accent-insensitive folding for Vietnamese text search/compare.
//
// NFD decomposition separates base letters from their combining diacritical
// marks (U+0300-U+036F covers acute, grave, hook, tilde, dot-below AND the
// horn U+031B used by u-horn/o-horn), which are then stripped. The crux: d-bar/D-bar
// (U+0111/U+0110) are precomposed letters with NO NFD decomposition, so the
// mark-strip alone leaves them intact - they must be mapped explicitly, else
// "dak" would stop matching "Dak Lak".

/**
 * Fold case + Vietnamese diacritics for accent-insensitive matching.
 * Returns lowercased, mark-free, trimmed text; empty string for null/undefined/empty.
 */
export function foldDiacritics(input: string | null | undefined): string {
  if (!input) return '';
  return input
    .toLowerCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '') // strip combining marks (incl. horn, dot-below)
    .replace(/đ/g, 'd') // U+0111 has no NFD form - map after lowercasing
    .trim();
}
