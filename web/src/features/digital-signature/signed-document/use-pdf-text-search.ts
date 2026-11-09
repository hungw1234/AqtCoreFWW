/**
 * use-pdf-text-search.ts — In-document text search for the embedded signed-PDF viewer.
 *
 * react-pdf has no built-in search UI. This hook drives one via two seams:
 *   1. `customTextRenderer` — wraps every match of the term in <mark data-pdf-mark> inside the
 *      transparent text layer that react-pdf overlays on each page canvas, so matches highlight.
 *   2. A MutationObserver on the scroll container — text layers render asynchronously per page, so
 *      the mark set grows over time; the observer recollects matches as pages settle, keeping the
 *      count live and match navigation correct across all pages.
 *
 * The PDF's own text is escaped before being re-emitted as innerHTML (the text comes from an
 * untrusted document), and the term is regex-escaped so punctuation is matched literally.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

const MARK_ATTR = 'data-pdf-mark';
const ACTIVE_CLASS = 'pdf-mark-active';

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) =>
    c === '&' ? '&amp;' : c === '<' ? '&lt;' : c === '>' ? '&gt;' : c === '"' ? '&quot;' : '&#39;',
  );
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export interface PdfTextSearch {
  term: string;
  setTerm: (value: string) => void;
  /** Total matches currently rendered across all pages. */
  matchCount: number;
  /** 1-based index of the active match for display; 0 when there is none. */
  activeMatch: number;
  next: () => void;
  prev: () => void;
  /** Pass to <Page customTextRenderer>; undefined when the term is empty (default text layer). */
  customTextRenderer: ((item: { str: string }) => string) | undefined;
  /** Attach to the scrollable page container — the observer + scroll-into-view target. */
  containerRef: React.RefObject<HTMLDivElement | null>;
}

export function usePdfTextSearch(): PdfTextSearch {
  const [term, setTerm] = useState('');
  const [matchCount, setMatchCount] = useState(0);
  const [activeIndex, setActiveIndex] = useState(0); // 0-based internally
  const containerRef = useRef<HTMLDivElement | null>(null);

  const customTextRenderer = useMemo(() => {
    const q = term.trim();
    if (!q) return undefined;
    const re = new RegExp(escapeRegExp(q), 'gi');
    return ({ str }: { str: string }) => {
      let out = '';
      let last = 0;
      str.replace(re, (match: string, offset: number) => {
        out += escapeHtml(str.slice(last, offset));
        out += `<mark ${MARK_ATTR}>${escapeHtml(match)}</mark>`;
        last = offset + match.length;
        return match;
      });
      out += escapeHtml(str.slice(last));
      return out;
    };
  }, [term]);

  // Reset position whenever the term changes.
  useEffect(() => {
    setActiveIndex(0);
  }, [term]);

  // Recollect marks as text layers render in (async, per page).
  const collect = useCallback(() => {
    const root = containerRef.current;
    if (!root) {
      setMatchCount(0);
      return;
    }
    const count = root.querySelectorAll(`mark[${MARK_ATTR}]`).length;
    setMatchCount(count);
    setActiveIndex((prev) => (count === 0 ? 0 : Math.min(prev, count - 1)));
  }, []);

  useEffect(() => {
    const root = containerRef.current;
    if (!root) return;
    const observer = new MutationObserver(() => collect());
    observer.observe(root, { childList: true, subtree: true });
    collect();
    return () => observer.disconnect();
  }, [collect, term]);

  // Mark the active match and scroll it into view.
  useEffect(() => {
    const root = containerRef.current;
    if (!root) return;
    const marks = root.querySelectorAll<HTMLElement>(`mark[${MARK_ATTR}]`);
    marks.forEach((mark, i) => mark.classList.toggle(ACTIVE_CLASS, i === activeIndex));
    const active = marks[activeIndex];
    if (active && typeof active.scrollIntoView === 'function') {
      active.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }, [activeIndex, matchCount, term]);

  const next = useCallback(() => {
    setActiveIndex((i) => (matchCount === 0 ? 0 : (i + 1) % matchCount));
  }, [matchCount]);

  const prev = useCallback(() => {
    setActiveIndex((i) => (matchCount === 0 ? 0 : (i - 1 + matchCount) % matchCount));
  }, [matchCount]);

  return {
    term,
    setTerm,
    matchCount,
    activeMatch: matchCount ? activeIndex + 1 : 0,
    next,
    prev,
    customTextRenderer,
    containerRef,
  };
}
