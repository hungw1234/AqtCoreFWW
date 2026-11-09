# Signature Profiles + Visible PDF Stamp — Plan / Red-Team / Validate

**Date:** 2026-06-23 (evening) · **Type:** Planning-only (0 code commit) · **Branch:** main

## Session goal
Brainstorm → plan → red-team → validate cho feature: mỗi user tự đăng ký profile chữ ký (cert + ảnh thường/nháy), remote Mysign render con dấu nhìn thấy lên PDF tại vị trí kéo-thả. Cờ `--html` brainstorm + `--tdd` plan.

## What worked
- **Brainstorm** (`/brainstorm --html`): 4 scout song song map trạng thái signing → phát hiện `SignerCertEnrollment` đã hỗ trợ đa-cert/user, remote Mysign ký PAdES VÔ HÌNH (`/Rect [0 0 0 0]`), chưa có entity ảnh. Chốt Approach 1 (UserSignatureProfile aggregate + raster-stamp) + 2 report (md+html) tại `plans/reports/brainstorm-design-260623-1950-...`.
- **Plan** (`/ck:plan --tdd`): 6 phase TDD scaffold qua `ck plan create`, RED-first mỗi phase. Plan dir `plans/260623-1950-user-signature-profiles-remote-visible-stamp/`.
- **Red-team** (4 reviewer hostile, code-reviewer subagent): **18 finding, 100% evidence file:line, 18/18 ACCEPT**. Bắt được lỗi kiến trúc nền tảng trước khi code.
- **Validate** (6 Q, 2 vòng): bắt mâu thuẫn vật lý PAdES; thêm Phase 7 (HA). Plan → 7 phase.

## What didn't / lessons (3 hard constraints — quan trọng cho mọi signing-work tương lai)
1. **PAdES KHÔNG thể hiển giờ-ký-thật trên con dấu visual.** Appearance nằm trong ByteRange/digest → phải compose TRƯỚC khi gửi hash Mysign; giờ Mysign ký chỉ biết SAU (completion). Giải: dấu in server-initiate-time, giờ thẩm quyền = CMS `signingTime` (Adobe panel). Plan ban đầu (compose-at-completion, Resolved-Q5) SAI hoàn toàn.
2. **`PdfIncrementalSignatureWriter` là SPIKE** — throw khi PDF đã có `/Annots` (`:33-34`) hoặc `/AcroForm` (`PdfDocumentModel.cs:166-167`); `PadesTransactionInitiator.cs:69-74` fail-close seq>1. → Multi-signer bất khả thi không viết lại writer; PDF có form field cũng throw (cần flatten). Multi-signer CẮT → plan riêng.
3. **SkiaSharp glibc KHÔNG load Alpine musl.** Deploy `Dockerfile` `linux-musl-x64`; QuestPDF+fontconfig+font-noto ĐÃ cài & proven VN-glyph trên đúng image → dùng QuestPDF, KHÔNG thêm SkiaSharp (nếu bắt buộc thì `.NoDependencies`).

Lesson meta: brainstorm/plan dựa trên scout đọc-excerpt vẫn miss các ràng buộc sâu (writer-là-spike, deploy-musl, capture-cert-null-on-callback). **Red-team grep-fact-check bắt được — value rõ rệt.** Validate bắt thêm mâu thuẫn user-decision vs vật-lý.

## Decisions made
- Approach 1 (aggregate + raster QuestPDF) · single-signer v1 · compose-at-initiate · drop SVG (XSS, `FileSecurityService:17` chặn sẵn) · upload qua `IFileSecurityService` magic-byte · explicit-enroll challenge-sign (gate visible-stamp) · TenantId-pin JWT (cross-tenant IDOR) · snapshot stamp at initiate (profile-delete-safe) · flatten-PDF trước Prepare · Phase 7 atomic row-claim (HA confirmed).
- Deferred: `MULTI-SIGNER-VISIBLE-STAMP` (viết lại writer) — plan follow-up.

## Test status
N/A — planning-only, 0 code. Plan đặt RED-first gates: P4 ByteRange-integrity (`VerifyAsync` thật), P2 IDOR same+cross-tenant, P7 concurrent-claim 2-instance.

## Next session
1. `/clear` → `/ck:cook plans/260623-1950-.../plan.md` Phase 1 (Domain `UserSignatureProfile`).
2. Phase 7 (HA) = production gate — dev P1-P6 trước, P7 bắt buộc trước prod multi-replica.
3. Xác nhận deploy topology với ops (đã giả định HA).
