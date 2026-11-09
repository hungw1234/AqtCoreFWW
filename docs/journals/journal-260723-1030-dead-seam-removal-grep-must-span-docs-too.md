# Journal — 2026-07-23 10:30 — Xóa dead seam: grep removal phải quét cả docs

## Việc

`FILES-DIGSIG-DEAD-SEAM` (T4, filed sáng nay từ P2 audit): xóa `IDigitalSignatureService`
+ `StubDigitalSignatureService` + 1 dòng DI (Files) — 0 caller full-span. -57/+4 LOC,
review APPROVE, Files tests 15+23+144+37 xanh.

## Hai điều đáng giữ

**1. Fake-pass stub nguy hiểm hơn dead code.** Stub trả `IsValid=true, "STUB - ND68
not implemented"` — dead hôm nay, nhưng nếu ai wire nó vào một luồng verify thật sau
này thì mọi tài liệu đều "hợp lệ". Đây là lý do delete thắng comment-keep, và là một
biến thể của [[feedback-fixing-a-bug-can-arm-a-dormant-hole]] chiều ngược: giữ một
mảnh "vô hại" hôm nay có thể arm một lỗ ngày mai.

**2. Removal grep phải span cả DOCS, không chỉ code.** Lesson
`symbol-removal-grep-must-span-tests` nói tests+Presentation — lần này reviewer tìm
ra **6 doc mention stale**, trong đó nặng nhất là `compliance-evidence-checklist.md`:
bảng bằng chứng NĐ68 mà auditor sẽ lần theo trỏ thẳng vào file vừa xóa. Sửa cùng
commit thì nó thành cơ hội: dòng evidence giờ trỏ vào Identity signing stack THẬT
(E2E-confirmed) — bằng chứng mạnh hơn stub giả. SRS giữ nguyên (snapshot-stamped =
tài liệu lịch sử, không sửa).

Mở rộng lesson: blast-radius của việc XÓA một symbol = src + tests + Presentation
+ **docs/compliance artifacts** — đặc biệt các file dạng "evidence map" mà người
ngoài (auditor) dùng làm điểm vào.
