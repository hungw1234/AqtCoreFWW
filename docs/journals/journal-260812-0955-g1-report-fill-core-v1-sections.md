# Journal - 2026-08-12 09:55 - G1 report fill (Core v1 sections)

## Session goal

Sếp giao update các mục 1.1-1.4 (Phần I - Kết quả phát triển Core G1) trong
`docs/reports/G1_Bao_cao_Core_Pilot_AI_Template.docx` theo outline có sẵn, dựa
trên các báo cáo Core v1.1 đã làm trước đó. Xuất bản mới, không sửa template
gốc.

## What worked

- **Template preservation via python-docx (1.2.0):** unzip inspect XML là 1
  dòng dài 107KB → unfeasible cho regex replace. Chuyển sang python-docx +
  cell-by-cell replacement giữ nguyên style của run đầu tiên. Layout template
  (heading style, font Palatino, bảng navy header) giữ nguyên 100%.
- **Empirical scout ngược giả định:** anh nghi "AI chỉ có interface stub,
  chưa implement". Scout `src/modules/ai/**` xác nhận NGƯỢC LẠI - Ollama LLM +
  Qdrant vector + ReAct agent + OCR + RAG concrete + 7 controller action có
  body thật + ZERO NotImplementedException ngoài stubs config-gated. Reporting
  tương tự: full pipeline definition → Hangfire → Dapper → ClosedXML/QuestPDF
  → storage, 17 handler concrete. Scout kịp thời trước khi ghi sai vào báo cáo.
- **Search module verify:** scout confirm module Search đã bị gỡ 04/2026
  (journal `journal-260421-0949-remove-search-module.md`) → chỉ còn stub dir.
  Bỏ ra khỏi danh sách còn 15 module.
- **Iterative user-in-loop:** đề xuất từng section (1.1 → 1.2 → 1.3 → 1.4),
  anh review + chốt số/wording từng bước; tránh phải làm lại toàn bộ báo cáo.
- **Verify visual:** render PDF → JPG 6 trang, đọc page 3-4 confirm layout
  đúng, tiếng Việt đủ dấu, bảng thẳng cột, không tràn.

## What didn't / lessons

- **LibreOffice hang batch convert:** convert 4 file docx trong 1 loop timeout
  2 phút, sinh file `.tmp` 6GB (rác I/O). Bài học: convert từng file một, KHÔNG
  batch nhiều file lớn cho `soffice --headless` cùng lúc.
- **Windows Python cp1252 UnicodeEncodeError:** stdout mặc định cp1252, print
  ký tự tiếng Việt fail. Fix: `PYTHONIOENCODING=utf-8` trước lệnh python. Bài
  học: mọi Python script chạy trên Windows Bash tool phải set env này nếu có
  Unicode I/O.
- **Nghi vấn cần verify empirically:** anh nghi AI là stub - nếu tin lời và
  ghi ngay vào báo cáo Sếp thì sẽ sai. Bài học: đối với báo cáo cho lãnh đạo,
  MỌI claim về trạng thái module PHẢI probe source (grep controller body,
  count endpoint, check NotImplementedException) trước khi ghi.

## Decisions made

- **Scope:** chỉ điền 1.1-1.4, không đụng 2.x/3.x (thuộc Lead khác: LinhTT,
  HảiDT, Lan Lê, TruongPH).
- **Merge policy:** đề xuất từng section, anh review chốt trước khi ghi.
- **Output:** file mới `G1_Bao_cao_Core_Pilot_AI_v1.1_ThienND.docx` cạnh
  template gốc; template gốc giữ nguyên.
- **Module count = 15** (không phải 16): bỏ Search legacy đã gỡ.
- **Không note MVP cho AI/Reporting:** scout xác nhận production-grade thật.
- **Chi phí:** AI = $444 (anh cấp), nhân sự = $3.040 (~76M VNĐ) theo báo
  cáo v1.1 gốc (304 giờ Jira × 250k/giờ), tổng $3.484 (~87.1M VNĐ). ROI 324%.
- **1.3 policy fields:** đặt `[Điền sau]` cho danh sách quyền + cơ chế khai
  thác + trách nhiệm tái đầu tư (thuộc thẩm quyền Sếp/PM, không phải ThienND).

## Test status

N/A - document-generation task, không đụng code/test.

## Next session

- Chờ Sếp fill 3 ô policy trong 1.3 (danh sách quyền, quy trình xin cấp
  quyền, trách nhiệm tái đầu tư).
- Nếu Sếp yêu cầu Lead khác điền tiếp Phần II/III → có thể reuse script
  `fill-template.py` trong scratchpad làm khung.

## Artifacts

- Deliverable: `docs/reports/G1_Bao_cao_Core_Pilot_AI_v1.1_ThienND.docx`
- Script (scratchpad): `.../scratchpad/docx-build/fill-template.py`
- Sources tham chiếu: `docs/reports/Bao_cao_AqtCoreFW_v1.1.docx`,
  `docs/reports/bao-cao-trien-khai-corebase.md`,
  `docs/reports/bao-cao-kiem-thu-tong-quan-260618.md`.
