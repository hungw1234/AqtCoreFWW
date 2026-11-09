# Quy trình phát triển với AI Claude Code

**Phạm vi:** pha phát triển (SRS → PR merged) · **Vai tham gia:** Techlead + Developer · **Bộ skill:** ClaudeKit (`ck:`) · **Cập nhật:** 2026-07-30

Một quy trình phát triển tuyến tính, có cả Techlead và Developer tham gia. SRS + prototype design (BA) và test case (QC) **được chuẩn bị trước** - là đầu vào, không phải bước trong quy trình này. Mỗi bước ghi rõ ai làm, skill dùng, đầu vào và đầu ra.

- **Đầu vào:** SRS (đặc tả yêu cầu) + prototype design (từ BA) + bộ test case (từ QC)
- **Đầu ra:** Code đã merge vào main, test 100% pass, review sạch, journal ghi nhận

> **Bàn giao plan (quan trọng) - CHỈ áp dụng khi TL lập plan rồi giao cho Dev** (task chạm cờ đỏ): thư mục `plans/` **bị gitignore** (tránh repo phình theo thời gian) - plan KHÔNG đi theo git tới Dev được. **Cơ chế:** TL chạy `/ck:plan` tạo plan local (`plan.md` + `phase-*.md`), rồi bàn giao bằng một trong hai cách - **(1) đính kèm file plan vào task**, hoặc **(2) gửi trực tiếp qua kênh chat nội bộ của team**. Dev tải file về máy → feed cho `/ck:cook <đường-dẫn-plan>`. **Khi Dev tự plan** cho task nhỏ của mình thì KHÔNG cần gửi - plan local là đủ. File `plans/` trên máy mỗi người là bản làm việc cục bộ, tự giữ/dọn, không chia sẻ qua git.

**Chú thích vai:** 🟣 Techlead - plan, review, ship · 🔵 Developer - code, fix, test, commit

---

## Quy trình 12 bước

Tuyến tính từ SRS đến khi code merge vào main.

| # | Bước | Vai | Skill | Input | Output |
|---|------|-----|-------|-------|--------|
| 01 | Khảo sát & lập kế hoạch | TL **hoặc** Dev | `/ck:scout` `/ck:plan` | SRS + prototype design (BA) + test case (QC) | `plan.md` + `phase-*.md` local (gitignore) → **đính kèm file vào task** hoặc **gửi qua chat nội bộ team** |
| 02 | Dự đoán rủi ro (thay đổi lớn) | TL · *tùy chọn* | `/ck:predict` `/ck:brainstorm` | Plan có thay đổi rủi ro cao / phá vỡ hợp đồng công khai | Danh sách rủi ro bảo mật/hiệu năng/UX; plan điều chỉnh nếu cần |
| 03 | Định vị codebase | Dev | `/ck:scout` | File plan TL bàn giao (đính kèm task / chat) - tải về máy | Bản đồ file liên quan, pattern hiện có, hợp đồng có thể ảnh hưởng |
| 04 | Chẩn đoán (nếu là bug) | Dev · *tùy chọn* | `/ck:debug` | Lỗi / test fail / hành vi sai | Nguyên nhân gốc được chứng minh trước khi sửa |
| 05 | Thực thi tính năng (TDD bắt buộc) | Dev | `/ck:cook --tdd` | `plan.md` + SRS + prototype design + test case | Test viết TRƯỚC (đỏ) → code cho xanh → refactor. Code khớp SRS, theo pattern dự án |
| 06 | Sửa lỗi / CI | Dev | `/ck:fix` | Bug cụ thể, lỗi type/lint, test fail, CI đỏ | Sửa đúng nguyên nhân gốc, không nới lỏng test |
| 07 | Chạy full suite & đo coverage | Dev | `/ck:test` | Test đơn vị đã có từ bước 05 (TDD) + suite hiện hữu | Chạy full unit/integration/e2e, đo coverage, verify không regression - **100% pass** |
| 08 | Tự review + commit + PR | Dev | `/ck:code-review` `/ck:git` | Code đã test xanh | Tự bắt regression; conventional commit (quét secret) + push + **PR** |
| 09 | Rà soát code & bảo mật | TL | `/ck:code-review` `/ck:security` | PR / diff của Dev | Findings xếp hạng, red-team; quyết định chấp nhận / yêu cầu sửa |
| 10 | Duyệt PR - sửa feedback (vòng lặp) | TL + Dev | Bitbucket PR + `/ck:fix` | PR + findings từ bước 09 | TL comment review thủ công trên Bitbucket PR; Dev `/ck:fix` sửa → quay lại 09 tới khi sạch |
| 11 | Ship / merge vào main | TL | `/ck:ship` | PR đã test + review sạch | Merge main → test → PR URL / release |
| 12 | Ghi nhật ký kỹ thuật | Dev · *tùy chọn* | `/ck:journal` | Phiên có quyết định / bài học đáng lưu | Journal entry ngắn (nguyên nhân, cách giải, bẫy đã gặp) |

---

## Ai lập plan cho task này? (bước 01)

Task đã bóc ≤ 8h nên **KHÔNG dùng thời gian làm tiêu chí** - xét bản chất kỹ thuật. Chạm ≥1 cờ đỏ → Techlead. Không cờ nào → Developer.

### 🟣 Techlead plan - nếu task chạm ≥1 cờ đỏ

- **Đổi hợp đồng công khai** - API / DTO / schema DB / migration / event / config-key
- **Cross-module** - ≥2 module, hoặc sửa SharedKernel dùng chung
- **Bảo mật / tuân thủ** - auth, RLS/tenant, mã hóa, audit, PII, chữ ký số
- **Rủi ro vận hành cao** - migration dữ liệu, đổi luồng đang chạy, blast-radius rộng

### 🔵 Developer tự plan - nếu KHÔNG chạm cờ nào

- Bug gói trong 1 module
- Thêm field/logic nội bộ, không đổi contract
- Refactor cục bộ 1 module
- Thêm test, sửa UI nhỏ

> Techlead vẫn duyệt plan ở bước review (09).

---

## Trường hợp sử dụng cơ bản

4 tình huống thường gặp, mỗi bước kèm prompt mẫu (thay `<...>` bằng nội dung thật).

### 1 · Làm tính năng mới — 🟣 TL plan hoặc 🔵 Dev plan

**Giai đoạn plan (bước 01) tùy tiêu chí cờ đỏ:** feature chạm API/schema/cross-module/bảo mật → Techlead plan (kèm bước 02 dự đoán rủi ro); feature nhỏ gói 1 module, không đổi contract → Dev tự plan (bỏ qua bước 02, TL duyệt ở bước 09). Các bước code trở đi giống nhau.

- **01a** `/ck:plan` (TL · nếu chạm cờ đỏ) → **đính kèm file plan vào task hoặc gửi qua chat nội bộ team**
  > `/ck:plan "Làm tính năng <tên tính năng> (chạm API/schema/cross-module). SRS: <đường-dẫn-srs>, prototype: <đường-dẫn-prototype>, test case: <đường-dẫn-testcase>. Phân rã theo tầng API → Application → Infrastructure, ghi rõ file sở hữu + tiêu chí done."`
- **01b** `/ck:plan` (Dev · nếu KHÔNG chạm cờ) → **plan local, không cần gửi; TL duyệt ở bước 09**
  > `/ck:plan "Làm tính năng nhỏ <tên tính năng> gói trong module <tên module>, không đổi contract. SRS: <đường-dẫn-srs>. Phân rã bước + tiêu chí done."`
- **02** `/ck:predict` (TL · nếu rủi ro cao)
  > `/ck:predict "Đánh giá rủi ro plan <tên tính năng>: bảo mật/PII, hiệu năng, phân quyền, phá vỡ contract."`
- **03** `/ck:scout` (Dev)
  > `/ck:scout "Tìm pattern hiện có liên quan <tính năng> trong module <tên module>: controller, handler, query mẫu."`
- **05** `/ck:cook --tdd` (Dev)
  > `/ck:cook <đường-dẫn-plan> --tdd "Viết test từ test case QC trước (đỏ) rồi mới code cho xanh, theo pattern module <tên module>."`
- **07** `/ck:test` (Dev)
  > `/ck:test "Chạy full suite <module liên quan>, đo coverage code mới, verify không regression."`
- **08** `/ck:git` (Dev)
  > `/ck:git "Commit conventional (feat: <mô tả ngắn>), quét secret, push + tạo PR kèm link plan."`
- **09** `/ck:code-review` (TL)
  > `/ck:code-review "Review PR <tên tính năng>: đúng acceptance-criteria, không regression, không đổi contract ngoài dự kiến, bảo mật OK."`
- **11** `/ck:ship` (TL)
  > `/ck:ship "Merge PR <tên tính năng> vào main sau khi review sạch + test 100% pass."`

### 2 · Fix bug — 🔵 Dev tự plan (bug 1 module)

Bug gói trong 1 module, không đổi contract → Dev tự chủ. TDD = viết test tái hiện bug trước.

- **03** `/ck:scout` (Dev)
  > `/ck:scout "Định vị code liên quan lỗi <mô tả lỗi> trong module <tên module>."`
- **04** `/ck:debug` (Dev)
  > `/ck:debug "Tìm nguyên nhân gốc lỗi <mô tả>. Triệu chứng: <log/hành vi sai>. Chứng minh root cause trước khi sửa."`
- **05** `/ck:fix` (Dev)
  > `/ck:fix "Sửa lỗi <mô tả> đúng root cause đã tìm. Viết test tái hiện bug (đỏ) trước, sửa cho xanh, không nới lỏng test cũ."`
- **07** `/ck:test` (Dev)
  > `/ck:test "Chạy suite module <tên module>, xác nhận test bug xanh + không regression."`
- **08** `/ck:git` (Dev)
  > `/ck:git "Commit conventional (fix: <mô tả>), push + PR."` → TL review → `/ck:ship`

### 3 · Review — 🟣 TL review · 🔵 Dev sửa (vòng lặp)

Lặp review ↔ fix cho đến khi hết finding chặn, mới ship.

- **09** `/ck:code-review` (TL)
  > `/ck:code-review <PR#> "Rà correctness, regression, breaking-change, bảo mật, AI-slop. Xếp hạng finding."`
- **09b** `/ck:security` (TL · nếu nhạy cảm)
  > `/ck:security "Audit PR <PR#> theo STRIDE/OWASP: auth, RLS/tenant, PII, injection."`
- **10** `/ck:fix` (Dev)
  > `/ck:fix "Sửa các finding từ review PR <PR#>: <liệt kê finding>. Không nới lỏng test."` → quay lại `/ck:code-review` tới khi sạch
- **—** Bitbucket PR (TL · đăng review thủ công)
  > Công ty dùng **Bitbucket** - `/ck:review-pr` (gh CLI) chỉ hỗ trợ GitHub nên KHÔNG dùng. TL đăng kết luận review trực tiếp trên giao diện Bitbucket PR; nội dung comment lấy từ kết quả `/ck:code-review` ở bước 09.

### 4 · Refactor — 🔵 Dev tự plan (không đổi hành vi)

Cục bộ 1 module, không đổi contract. TDD ở refactor = test cũ phải giữ xanh (an toàn hồi quy).

- **03** `/ck:scout` (Dev)
  > `/ck:scout "Xác định phạm vi refactor <tên đối tượng> trong module <tên module> + mọi caller bị ảnh hưởng."`
- **01** `/ck:plan` (Dev)
  > `/ck:plan "Refactor <đối tượng> để <mục tiêu: tách module/gỡ trùng lặp>, GIỮ NGUYÊN hành vi. Liệt kê file đụng + test bảo chứng."`
- **05** `/ck:cook --tdd` (Dev)
  > `/ck:cook --tdd "Refactor <đối tượng> theo plan. Test hiện hữu phải xanh trước VÀ sau khi đổi cấu trúc; không thêm hành vi mới."`
- **07** `/ck:test` (Dev)
  > `/ck:test "Chạy full suite, xác nhận 100% test cũ vẫn xanh (bằng chứng không đổi hành vi)."`
- **08** `/ck:git` (Dev)
  > `/ck:git "Commit conventional (refactor: <mô tả>), push + PR."` → TL review

---

## Cấu hình API token để AI tương tác Bitbucket

> **Quan trọng:** Bitbucket đã **gỡ hẳn App Password từ 28/07/2026**. Phải dùng **API token** (Atlassian account). Token dùng Basic Auth với **username = email Atlassian**, **password = API token**.

### 1. Tạo API token

1. Vào `https://id.atlassian.com/manage-profile/security/api-tokens` (hoặc Bitbucket → Account settings → API tokens).
2. Tạo token, **bắt buộc chọn scope** (không scope = không gọi được API):
   - `read:repository:bitbucket` - clone, đọc repo
   - `write:repository:bitbucket` - push
   - `read:pullrequest:bitbucket` - đọc PR
   - `write:pullrequest:bitbucket` - comment/approve PR
3. Đặt **expiry** (tối đa 1 năm). Token chỉ hiện **1 lần** khi tạo - copy ngay, mất thì tạo lại (không xem lại được).

### 2. Lưu token an toàn (KHÔNG hard-code)

```bash
# ~/.bashrc hoặc biến môi trường phiên làm việc (không commit vào repo)
export BITBUCKET_EMAIL="ban@synodus.com"
export BITBUCKET_API_TOKEN="<dán-token-vừa-tạo>"
```

> Không đưa token vào file trong repo, không dán vào chat. Nếu lỡ lộ → revoke ngay tại trang API tokens rồi tạo token mới.

### 3. Git qua HTTPS

```bash
git clone https://x-bitbucket-api-token-auth:$BITBUCKET_API_TOKEN@bitbucket.org/synodus/x25gfx05705-app-phieu.git
```

Hoặc dùng credential helper để không lộ token trong URL/history.

### 4. Gọi REST API (AI dùng để đọc/comment PR)

```bash
# Đọc danh sách PR đang mở
curl -s --user "$BITBUCKET_EMAIL:$BITBUCKET_API_TOKEN" \
  "https://api.bitbucket.org/2.0/repositories/synodus/x25gfx05705-app-phieu/pullrequests?state=OPEN"

# Đăng comment review lên PR <PR_ID>
curl -s --request POST --user "$BITBUCKET_EMAIL:$BITBUCKET_API_TOKEN" \
  --header 'Content-Type: application/json' \
  --data '{"content":{"raw":"<nội dung review từ /ck:code-review>"}}' \
  "https://api.bitbucket.org/2.0/repositories/synodus/x25gfx05705-app-phieu/pullrequests/<PR_ID>/comments"
```

### 5. Prompt mẫu cho AI (bước 09-10)

> `/ck:code-review "Rà PR <PR_ID> trên Bitbucket. Lấy diff qua REST API (workspace=synodus, repo=x25gfx05705-app-phieu). Xếp hạng finding."`
>
> Sau khi có kết luận: `"Đăng các finding lên Bitbucket PR <PR_ID> qua REST API pullrequests/<PR_ID>/comments (dùng $BITBUCKET_EMAIL + $BITBUCKET_API_TOKEN)."`

> **Lưu ý:** skill `/ck:review-pr` mặc định dùng `gh` CLI (chỉ GitHub) - **không dùng cho Bitbucket**. Với Bitbucket, AI thao tác PR qua REST API như trên, hoặc TL comment thủ công trên web.

---

## Ghi chú — ranh giới vai & TDD

**Ranh giới vai:** Bước 01 (plan) do TL **hoặc** Dev tùy tiêu chí cờ đỏ (feature nhỏ 1 module Dev tự plan; chạm API/schema/cross-module/bảo mật thì TL plan). Bước 09-11 (review, duyệt PR, ship) do Techlead; bước 02 (dự đoán rủi ro) do TL và chỉ chạy khi TL plan. Bước 03-08 (scout, chẩn đoán, code, test, commit) + 12 (journal) do Developer. Bước 10 là vòng lặp review ↔ fix chung. Skill *tùy chọn* chỉ dùng khi phát sinh (bug cần chẩn đoán, thay đổi rủi ro cao, PR nhạy cảm bảo mật).

**TDD bắt buộc 100%:** mọi bước code đều test-first - viết test (từ test case QC) trước, thấy đỏ, rồi mới code cho xanh. Feature/refactor dùng `/ck:cook --tdd`, fix bug dùng `/ck:fix` (viết test tái hiện bug trước). Không viết code sản phẩm trước test. Nguyên tắc chung: YAGNI · KISS · DRY, scout trước khi code, test xanh trước khi commit, review trước khi ship.

---

> Bản trình bày trực quan (playbook có sidebar): [`plans/reports/dev-quy-trinh-phat-trien-voi-ai-v1.html`](../plans/reports/dev-quy-trinh-phat-trien-voi-ai-v1.html).
