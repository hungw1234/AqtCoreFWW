# Journal — Accent-insensitive geographic search (address widget + master-data page)

**Date:** 2026-06-03 · **Ships:** v2.137p146 + v2.137p147 · **Commits:** `03cc3d7d` + `6054b5fd` (p146), `4720915a` + `a888d4ac` (p147) (pushed origin + bitbucket, ahead=0)

## What shipped
Two companion fixes made province/ward/district search diacritic-insensitive by introducing a new shared util `foldDiacritics()`. v2.137p146 applied it to the address-field-widget province+ward Selects (filterOption). v2.137p147 reused it across all 5 search points on the master-data admin page (2 Select filterOptions + 3 table row-search filters). Users can now type "nghe" and match "Nghệ An"; "phuong" matches "Phường …".

## The Unicode trap that cost clarity (đ has no NFD decomposition)
The naïve approach—normalize NFD then strip combining marks U+0300–U+036F—fails silently on `đ` and `Đ` (U+0111/U+0110). These are **precomposed** characters; NFD does not decompose them. A mark-strip-only fold returns them unchanged, so "dak" never matches "Đắk Lắk". The fix: explicit `.replace(/đ/g,'d')` after lowercase. The test suite has a case that flips red if that line is dropped—a future "simplification" audit can't regress silently.

## Toolchain quirk: linter reverts regex escapes to literal combining chars
The code holds the combining mark range as `[̀-ͯ]` (literal bytes U+0300–U+036F, not escaped). On 4 separate saves, a post-commit linter/formatter hook attempted to convert literal combining chars back to `\u` escapes and the vcs rejected the drift. Accepted the literal form. Byte-verified via `node codePointAt()` — the range is correct.

## Scope surfaced, not silently expanded or silently left half-done
The backlog row named "2 Select filterOptions"; grep found 3 more table-search points on the same geographic data. A 2-of-5 fix leaves an inconsistent half-fix; the table search is the more-used affordance. Surfaced via AskUserQuestion; user chose all 5. Folding non-name columns (id/code/numeric) is harmless (no diacritics, only lowercase/trim). `.trim()` on search input is improvement not regression.

## Pre-existing failure proven independent, not assumed
The shared test suite carried 1 failure in `admin-tenant-selector` (query-cancellation race). Isolatd via `git stash push -- master-data-page.tsx` → test failed identically on clean HEAD → stash pop. Proved independence without false confidence in "probably unrelated."

## Verification
Util engine `fold-diacritics.test.ts` 14/14 vitest (cases cover đ, horn ư/ơ, dot-below, breve, hook-above, tilde, uppercase, null/empty, search-match contract). `tsc --noEmit` 0E. Code-review APPROVE 0 Crit/High (v146); v147 proportionate review skipped (mechanical reuse of just-reviewed util). FE-only; no BE/contract/migration/entity.

## Carry-forward
- Theme accent-insensitive geo COMPLETE for 34-province data (address widget + admin CRUD).
- ~13 other Selects (users/roles/orgunits, ref-fields) still diacritic-sensitive on smaller lists—tolerable, no row filed (YAGNI).
- `BP-FORM-OPTIONS-SNAPSHOT` and other open items unblocked.

## Meta
Scout found zero existing diacritics util (~15 Selects all raw `.includes()`). Built reusable, applied to initial site (v146), then shipped the strongest reuse case same session (v147 needed it more). Advisor pinpointed THE single crux (đ) among ~10 mechanics. Proportionate review (skip heavyweight re-review for mechanical reuse). Stash-isolate is a cheap empirical proof of pre-existing failure independence.
