# Runbook — Backfill orphan Forms → Workflow links

**Mục đích:** Nạp một hàng retry-armed `Pending` cho mỗi hồ sơ (FormSubmission) mồ côi lịch sử — hồ sơ
có biểu mẫu gắn quy trình phê duyệt nhưng chưa bao giờ được liên kết `WorkflowInstanceId` (do bước tạo
instance thất bại lúc nộp, trước khi job retry tồn tại). Sau khi nạp, job Hangfire
`FormWorkflowLinkRetryJob` sẽ tự claim và giải quyết chúng ở các tick kế tiếp.

**Script:** `scripts/backfill/forms-workflow-link-pending-backfill.sql`
**Cửa sổ thời gian:** hồ sơ có `SubmittedAt >= 2026-05-21` (ngày ship 5b fail-closed graceful-skip).

---

## 1. Điều kiện tiên quyết

- Job `FormWorkflowLinkRetryJob` **đã deploy và đang chạy** (Hangfire recurring, cron `*/5 * * * *`).
- Đã **sao lưu DB** (hoặc snapshot) trước khi chạy — đây là thao tác ghi.
- Chọn **principal chạy script** (xem mục RLS bên dưới).

---

## 2. Principal — vì sao cần đặc quyền

Bảng **đích** `forms.FormSubmissionWorkflowLinkPendingAttempts` KHÔNG có RLS ở tầng DB (chỉ có EF
query-filter ở tầng ứng dụng) → INSERT không bao giờ bị RLS chặn. Nơi CẦN principal đặc quyền là câu
**SELECT nguồn**: `forms.FormSubmissions` CÓ RLS theo `TenantId`, nên principal ứng dụng chỉ nhìn thấy
hồ sơ của tenant mình. Chọn MỘT trong hai:

- **(Khuyến nghị) Chạy như principal migration / db_owner** — đọc được hồ sơ mồ côi của MỌI tenant
  trong một lần. Thao tác ops một lần, phù hợp principal đặc quyền.
- **Hoặc lặp theo tenant** — với principal ứng dụng bị RLS giới hạn ở nguồn, gọi
  `EXEC sys.sp_set_session_context @key=N'TenantId', @value=<tenant-guid>;` trước mỗi lần chạy, và thêm
  `AND s.TenantId = <tenant-guid>` vào mệnh đề WHERE. Lặp cho từng tenant.

> Nếu chạy dưới principal ứng dụng mà không lặp theo tenant, câu SELECT nguồn chỉ thấy một tenant → backfill
> sót các tenant khác. Luôn xác nhận principal trước.

---

## 3. Dry-run (BẮT BUỘC trước khi ghi)

Đếm số hồ sơ mồ côi sẽ được nạp — KHÔNG ghi gì:

```sql
SELECT COUNT(*)
FROM forms.FormSubmissions s
INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
WHERE t.ApprovalWorkflowDefinitionId IS NOT NULL
  AND s.WorkflowInstanceId IS NULL
  AND s.IsDeleted = 0
  AND s.SubmittedAt >= '2026-05-21'
  AND NOT EXISTS (
      SELECT 1 FROM forms.FormSubmissionWorkflowLinkPendingAttempts a
      WHERE a.SubmissionId = s.PublicId AND a.TenantId = s.TenantId);
```

Ghi lại con số này — nó phải khớp với số hàng INSERT ở bước sau.

---

## 4. Thực thi

Chạy script (đã bọc transaction, `XACT_ABORT ON` — lỗi bất kỳ sẽ rollback toàn bộ):

```
sqlcmd -S <server> -d <database> -i scripts/backfill/forms-workflow-link-pending-backfill.sql
```

Script in ra `Backfill inserted rows: <n>`. Con số `<n>` phải khớp dry-run ở bước 3.

**Idempotent:** chạy lại script an toàn — mệnh đề `NOT EXISTS` (khóa unique `SubmissionId, TenantId`)
bỏ qua mọi hàng đã có, lần chạy thứ hai sẽ báo `inserted rows: 0`.

---

## 5. Xác minh

Đếm số hàng vừa nạp:

```sql
SELECT COUNT(*) FROM forms.FormSubmissionWorkflowLinkPendingAttempts
WHERE LastFailureCode = 'BACKFILL_ORPHAN';
```

Theo dõi job giải quyết dần (trong vài tick, mỗi tick cách nhau 5 phút; số hàng claim mỗi tick bị chặn
bởi `BatchSize` nên tập lớn sẽ drain qua nhiều tick):

```sql
-- Còn chờ / đang retry
SELECT Status, COUNT(*) FROM forms.FormSubmissionWorkflowLinkPendingAttempts
WHERE LastFailureCode = 'BACKFILL_ORPHAN'
GROUP BY Status;
```

Kỳ vọng: `Pending`/`Retrying` giảm dần → `Succeeded` tăng dần. Hàng nào cạn số lần thử (`MaxAttempts`)
sẽ chuyển `Shelved` để admin xử lý thủ công ở trang "Liên kết quy trình đang chờ".

---

## 6. Rollback

**Chỉ xóa hàng backfill job CHƯA hề thử** — không bao giờ động vào `Succeeded`/`Shelved` (trạng thái
thật), và cũng không xóa hàng job ĐÃ thử rồi re-arm (một hàng thất bại tạm thời quay lại `Pending` nhưng
có `AttemptCount > 0` — đó là tiến trình retry thật, không phải "chưa xử lý"):

```sql
DELETE FROM forms.FormSubmissionWorkflowLinkPendingAttempts
WHERE LastFailureCode = 'BACKFILL_ORPHAN'
  AND Status = 'Pending'
  AND AttemptCount = 0;
```

Nếu cần dừng job (ví dụ để điều tra), dùng đường rollback của Phase 04 (`RecurringJob.RemoveIfExists`).
