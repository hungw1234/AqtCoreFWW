# Journal — 2026-06-18 13:55 — Signed-PDF viewer render fix + enrichment + leadership report

## Session goal
Continuation of signed-file view/download (Phase A). Three asks, in order:
1. `/ck-debug` (×2) — the embedded react-pdf viewer failed to render ("Không tải được tài liệu PDF").
2. `/fix A` — enrich the minimal viewer toward pdf.js-demo parity (user picked Option A: in-app react-pdf, keep control).
3. `/cook` — refresh `project-modules-overview.md` + produce a leadership report (.md + .docx) on CoreBase delivery, incl. Jira total = 304h.

## What worked (verified + commit refs)
- **Worker render fix (commit `167b7a8a`, pushed origin+bitbucket):**
  - Root cause nailed by inspecting the *running browser*: `import('react-pdf')` at runtime returned `pdfjs.GlobalWorkerOptions.workerSrc === "pdf.worker.mjs"`. react-pdf eagerly seeds that bare specifier at its own module-load. Vite leaves bare specifiers untransformed → dev server serves `index.html` (text/html) → module-script MIME rejection.
  - The *first* fix attempt (prior turn) used `if (!workerSrc)` guard → saw react-pdf's truthy default → skipped. Fix: set `workerSrc` **unconditionally** + add `pdfjs-dist@5.4.296` as a pinned direct dep so vite-static-copy bundles the worker.
  - Empirical before/after: worker URL `/vendor/pdfjs/pdf.worker.min.mjs` → `200 text/javascript` 1,046,214 B (was `200 text/html` 1039 B). PDF renders E2E (QĐ 742 doc + sig block visible, console clean).
- **Viewer enrichment (Option A, same commit):** continuous scroll + thumbnail sidebar + zoom/auto-fit-width + page indicator + in-document search (highlight + match nav). Modularised: `signed-pdf-viewer.tsx` (orchestrator), `signed-pdf-toolbar.tsx`, `signed-pdf-thumbnails.tsx`, `use-pdf-text-search.ts`. Public props unchanged.
  - E2E verified in browser: 11-page doc, auto-fit 133%, sidebar active-page highlight, search "742" → "1/2" + orange active mark, console clean.
- **code-review APPROVE_WITH_NITS** — applied MED-1/2 (reset `pageRefs`/`baseWidthRef` per-document for safe blob-swap-while-open) + NIT-2 (added `use-pdf-text-search` unit test locking the XSS-escaping invariant — the path the viewer test's `Page` mock never exercises). Tests 120/120, tsc 0, eslint 0.
- **Leadership report:** `docs/bao-cao-trien-khai-corebase.md` + `.docx` (python-docx, 5 tables, headings, VN diacritics intact). Refreshed `docs/project-modules-overview.md` (signing view/download shipped). Proposal `De_Xuat_Core_Fw_v1.docx` extracted via python-docx (439h baseline / 258h AI-est / 7 core modules / 250k VND-h).

## What didn't / lessons
- **Dependency self-init can defeat a falsy-guard.** The big lesson: a library may pre-seed a global at its own module-load with a *broken-but-truthy* value. A `if (!x)` guard then no-ops and the bug ships green. Verify the **runtime value** of a dependency-owned global, not the initial state you assume. The unit test was green because its react-pdf mock seeded `workerSrc:''` — it didn't mirror the real default `"pdf.worker.mjs"`. Fixed the mock to seed the real broken default so the regression guard is meaningful.
- **Same false-green class as the codebase's recurring one** (Files-list casing, VGCA upload casing): a mock that doesn't mirror real dependency behaviour passes while prod breaks. Pin contracts/behaviour against the *real* boundary value.
- **docx + Vietnamese:** python stdout is cp1252 on this Windows box → `UnicodeEncodeError` printing VN. Write extraction to a UTF-8 file and Read it; set `PYTHONIOENCODING=utf-8` when running build scripts.

## Decisions made
- **Viewer direction = Option A (enrich react-pdf in-app)** over B (`@react-pdf-viewer/core`) or C (iframe `viewer.html`). Rationale: signed-document trust surface must keep the mandatory caveat in view and not expose an uncontrolled toolbar (open/print/download) — A keeps tightest control; C breaks the caveat invariant.
- **Eager-render all pages (no virtualization)** — signed docs are small (a few pages); KISS over react-window. Documented as a scalability follow-up if large PDFs appear.
- **Leadership-report AI framing = honest, not inflated.** Presented all three numbers (439h baseline / 258h AI-estimate / 304h actual). 304h > 258h optimistic estimate (+46h) called out explicitly, attributed to scope expansion (16 vs 7 modules) + compliance depth. Leverage framed as "delivered ~2.3× modules under the no-AI baseline hours", ROI vs baseline ~324%.

## Test status
- FE digital-signature suite: 120/120 pass. tsc 0, eslint 0.
- No BE changes this session (FE viewer + docs only).

## Next session (carry-over)
- Decide whether to commit the wrap-up docs bundle (modules-overview refresh + leadership report .md/.docx + this journal + changelog + backlog) — left uncommitted per commit-only-when-asked.
- Deferred viewer polish (LOW/NIT): replace `replace()`-as-side-effect with `matchAll`, drop redundant thumbnail onSelect, inline `<style>` → CSS module, react-window for very large PDFs.
- Phase B: signature crypto/cert verification (PdfPig + BouncyCastle + sandbox) → removes the "Chưa xác minh" caveat.
- Leadership `.docx` not yet human-reviewed for layout (renders valid; numbers should be sanity-checked before sending up).
