# Files List — camelCase Contract Fix + File-Type Icons

**Date:** 2026-06-16 ~18:10 ICT · **Flow:** `/ck-debug` → `/cook` (FE-only) · **Scope:** `web/src/features/files`

## Trigger
User reported the Files list (`/files`) rendered 17 rows but **every field blank**: no file name, no type, "0 B" size, "Đang quét" (scanning) for all rows, empty uploader/date, and download broken.

## Diagnosis
The rows rendered (count + total correct) but item fields were all empty — the classic shape of a **list-returns-items-but-item-keys-don't-match** contract bug.

- BE `ListFilesQueryHandler` projects rows into an **anonymous object** (PascalCase props) cast to `object`, then returns via `Ok(ApiResponse<object>.Ok(...))`. A stale comment claimed "PascalCase to match Dapper" / "preserved by Newtonsoft".
- But the app uses **System.Text.Json** (`AddControllers().AddJsonOptions`, no `PropertyNamingPolicy` override → MVC default **camelCase**; no `AddNewtonsoftJson` anywhere). A boxed anonymous type is still a typed class — STJ applies the camelCase policy to its properties. So the wire shape is `originalFileName`, `sizeBytes`, `virusScanStatus`, `id`…
- The FE `FileRecordDto` + table `dataIndex` read **PascalCase** (`OriginalFileName`, `SizeBytes`, `Id`). Every column → `undefined` → defaults: `formatFileSize(undefined ?? 0)` = "0 B", `virusScanStatus undefined ?? 'Pending'` = "Đang quét", dates "—", `downloadFile(r.Id=undefined…)` → broken URL.
- Cross-check: **every other list page reads camelCase** (`api-key-list-page`, etc.); within the same `file-types.ts`, `FileMetadataDto` (used by download/scan-status, which work) is camelCase. The list DTO was the lone PascalCase outlier.

The "Dapper/Newtonsoft PascalCase" comments are the fossil of an older implementation (Dapper `dynamic` → dict keys serialize verbatim, or Newtonsoft's PascalCase default). A later refactor that added cross-module `UploadedByName` enrichment switched the projection to a typed anonymous object — silently flipping the wire casing to camelCase and breaking the FE.

## Lesson — the test mirrored the bug
`file-list-page.test.tsx` mocked **PascalCase** data matching the FE's (wrong) contract, so the test was **green while production was broken**. A render test that builds its own fixture in the same shape the component reads validates nothing about the real API contract. Fixed the mock to camelCase (the real wire shape) — now the test would catch a re-divergence.

## Fix (FE-only)
- `file-types.ts` `FileRecordDto` → camelCase (the `VirusScanStatus` string VALUES stay PascalCase — they're SQL-CASE data, not JSON keys).
- `file-list-page.tsx` → camelCase `dataIndex`/`key`, `downloadFile(r.id, r.originalFileName)`, `rowKey="id"`.
- `file-list-page.test.tsx` mock → camelCase.
- Chose FE-only over forcing PascalCase on the BE: the BE already serves camelCase consistently with the whole API; the FE was the outlier. Left the BE's stale comments for a follow-up (out of FE-only scope).

## Polish — file-type icons (same slice)
User then asked to replace the raw MIME string in the LOẠI column with a type icon. Added `file-type-icon.tsx` — `FileTypeIcon` maps a MIME content type to an AntD icon (pdf/word/excel/ppt/image/zip/text/generic, family-coloured) and keeps the raw type in a `Tooltip` + `aria-label` so no information is lost. Wired into the column (width 80, centered). No shared MIME→icon helper existed (form-attachment-preview only did pdf/generic), so this is the first reusable one.

## Verification
Visual on real data (17 rows: names, type icons, real sizes 778 KB–2.7 MB, "Sạch" tags, uploader "Dev Admin", dates). Full vitest **1075/1075** (115 files); files feature 21/21; ESLint 0E; app typecheck 0E.

## Note
Subagents (tester/code-reviewer/git-manager/journal-writer) failed mid-session with an org auth error ("Claude subscription access disabled") — ran all verification + commit + this journal inline instead.

## Follow-up
- BE stale comments in `ListFilesQueryHandler.cs` + `ListFilesQuery.cs` ("PascalCase/Dapper/Newtonsoft") still misleading — code is correct (camelCase), comments lie. Optional cleanup.
- `web/.gitignore` ignores `vitest-results.*` not `vitest-output*.txt` → two reporter files persist untracked.
