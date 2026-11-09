# Docs Restructure & Consolidation (2026-06-25)

**Type:** Documentation-only restructure | **Plan:** `260625-1606-docs-restructure-consolidation` (6 phases) | **Commits:** `b32b64c0`→`04de78ea` (7) pushed origin+bitbucket | **Review:** code-reviewer APPROVE_WITH_NITS (0 BLOCKER/HIGH)

## Goal
Rà soát + tái cấu trúc `docs/` (nội dung **và** cấu trúc): tinh gọn, đầy đủ, không trùng lặp, đúng canonical set của Claude KIT/Code. Scope giới hạn living-docs; `journals/ srs/ compliance/` giữ nguyên (history dày).

## What shipped (6 phases, rủi-ro thấp→cao, checkpoint commit mỗi pha)
1. **Format-dup** (`b32b64c0`): xóa stale `project-modules-overview.html` (0 inbound ref). **GIỮ** `bao-cao-trien-khai-corebase.docx` (grep lộ active backlog `LEADERSHIP-REPORT-DOCX-REVIEW` = deliverable lãnh đạo) + `De_Xuat...docx` (standalone) — deviation vs plan.
2. **Changelog archive** (`47398a99`): move 114 entry April (≤2026-04-30) → `project-changelog-archive.md`; main **1.14MB→862KB**; integrity 426 entry bảo toàn (307 main + 119 archive). Ranh giới có bẫy: `v2.133p17 (2026-04-30 ~21:50 ICT)` — date pattern grep bỏ sót vì có ` ~HH:MM ICT` sau ngày.
3. **Roadmap merge** (`5d77b94c`): gộp `development-roadmap.md` → `project-roadmap.md` (canonical name per project CLAUDE.md). **1555→194 dòng**. Base = dev-roadmap (tươi hơn), giữ unique-historical từ project-roadmap, refresh June (signature P1-3, module-RLS, audits). docs-manager subagent; reviewer xác nhận 5/5 unique-item survival + 0 fabrication.
4. **codebase-summary consolidate** (`66a487b0`): hết split-brain. Entry `.md` canonical (233 LOC, tech-stack→link), dir còn 1 appendix `modules.md`. Xóa `index/infrastructure/testing.md`.
5. **Structural reorg** (`1b2795b3`): 12 file phụ → `runbooks/ ops/ guides/ reference/ reports/` (top-level **30→18**). Link-fixup 6 file gồm **`scripts/reset-all.ps1`** (script→doc ref).
6. **README rebuild + link-check** (`bb4d4f3c`): index chính xác+đầy đủ + current-status June. Python link-checker: 94 link living-docs, **0 refactor-broken** (3 fix áp).
- **M1 review-fix** (`04de78ea`): restore 8 infra-test-project enumeration vào modules.md (testing.md KHÔNG phải exact-duplicate — reviewer bắt).

## Key decisions / lessons
- **Audit-before-delete cứu 1 mis-step:** Agent phân loại `bao-cao...docx` "redundant export" nhưng grep lộ active backlog row treat nó là leadership deliverable → GIỮ, không xóa. (review-audit-self-decision: đừng xóa thứ mâu thuẫn mô tả.)
- **Archive ≠ delete:** changelog/roadmap history đẩy sang archive/condensed, git history giữ → byte total chỉ −199KB nhưng duplication/sprawl giảm mạnh (−2126 dòng). "Tinh gọn" = bớt trùng + tổ chức, không phải xóa history.
- **Self-consistent commit:** fix inbound link NGAY trong P5 (không defer P6) → mỗi commit không broken-link trung gian.
- **Scope-guard empirical:** `git diff --stat 2c914019..HEAD -- docs/journals docs/srs docs/compliance` = empty → chứng minh frozen dirs bất biến.
- **Link-check scope:** living-docs only; journals/srs/compliance frozen (citations tới file đã move/xóa = historical artifact, không rewrite). 10 broken còn lại đều pre-existing (changelog code-citations `:line`, plan-refs cũ, 1 ops dangling).
- **aqt-next probe `development-roadmap.md`** → HAS_ROADMAP=0 sau gộp = soft-signal degrade git-log, KHÔNG vỡ; không tạo stub (DRY).

## Verify
code-reviewer APPROVE_WITH_NITS · changelog 426=426 · roadmap 5/5 + 0 fabrication · 0 refactor-broken-link · scope-guard empty · VN diacritics + 0 AI-ref · net −2126 dòng · 7 commit pushed.

## Follow-up — 3 unresolved questions handled (2026-06-25, same day)
- **Q1 (ops dangling link) RESOLVED:** `ops/phase-05b-...md` ref `audit-260521-1715-phase-05c-...md` thực ra tồn tại ở `plans/reports/` (gitignored local artifact), chỉ sai relative path → fix 2 ref → `../../plans/reports/...` (consistent với cách changelog ref plans/). Link-check 10→8 broken.
- **Q1 phần còn (8 broken) = WONTFIX có chủ ý:** đều là changelog/-archive historical-entry citations (code-refs dạng `File.cs:line` KHÔNG phải nav-link + plan-dirs đã rename/remove). Changelog = immutable delivery record → KHÔNG rewrite historical entries.
- **Q2 (MEMORY.md 370KB) RESOLVED:** compact → **18.3KB loadable**. Tách project rows (288/346KB) → `MEMORY-project-archive-260625.md` (verbatim, grep-able); giữ loaded toàn bộ feedback(71)/user/reference (timeless) + top-10 recent-project truncated one-liner. 365 rows preserved 0-loss; backup `MEMORY.md.bak-260625`. (memory ngoài repo, không commit.)
- **Q3 (README L1) RESOLVED:** `guides/` row đồng bộ directory-link (bỏ 2 per-file link) → mọi subdir entry nhất quán, index lean.

## Follow-up 2 — top-level thin-out + srs stale-ref sweep (2026-06-25, same day)
- **4 file rời top-level** (commit `b28e6ff1`): `project-modules-overview.md`→reference/, `bao-cao-trien-khai-corebase.md`+`.docx`+`De_Xuat...docx`→reports/. Top-level **18→16 .md, 0 .docx loose**. Refs cập nhật: README + bao-cao prose + backlog row + bonus stale `development-roadmap`→`project-roadmap` trong modules-overview. Giữ 5 high-traffic + compliance-checklist (hub 6-srs-ref) top-level theo user-choice.
- **srs/ stale-ref FIX (LESSON):** audit "đã chuẩn hoá meta-docs chưa" lộ **9 ref cũ trong `docs/srs/`** (2 broken md-link srs/README + 7 prose) tới file tôi đã move (slo→ops/, orval→guides/, security-incident→runbooks/, authorization-gov→reference/). **Root cause: bucket srs/ là "frozen" SAI** — SRS là living requirements-spec, KHÔNG phải append-only như journals; move file → ref của nó gãy. Link-checker ban đầu loại srs/ khỏi scope nên bỏ sót. FIX: 9 ref + mở rộng checker SCAN_DIRS thêm `srs/`+`srs/use-cases/` (91→162 link phủ). **LESSON: "frozen dir" chỉ đúng cho append-only history (journals); requirements-spec/reference dirs là living → phải nằm trong link-fixup + link-check scope khi move file.**
- **Verify cuối:** CLAUDE.md doc-tree ✓ (8 canonical đúng, 0 development-roadmap) · README/MEMORY/canonical-core ✓ · 0 stale old-path ref toàn docs (excl journals) · link-check 162 link → 9 broken ĐỀU pre-existing (8 changelog citations + 1 srs→ClaimTaskModal.tsx code-ref cũ), 0 do restructure.
