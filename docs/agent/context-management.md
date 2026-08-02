# Context Management and Handoff

## 1. Vai trò của từng tài liệu

- `AGENTS.md` chứa quy tắc ổn định, luôn áp dụng và định tuyến tài liệu.
- [`../project-state.md`](../project-state.md) chứa trạng thái hiện tại, ngắn gọn và được cập nhật thường xuyên.
- [`../decisions.md`](../decisions.md) chứa các quyết định bền vững đã chốt và các quyết định kiến trúc còn mở.
- Product spec và tài liệu trong `docs/agent/` chứa chi tiết theo chủ đề, chỉ nạp khi nhiệm vụ liên quan.

Không chuyển trạng thái tạm thời, log dài hoặc diễn biến từng phút vào `AGENTS.md`.

## 2. Khi nào phải tạo checkpoint

Cập nhật [`../project-state.md`](../project-state.md):

- Trước khi đổi sang cuộc trò chuyện mới.
- Trước khi compact context.
- Khi context đạt khoảng 70–80%, trước khi compact xảy ra.
- Sau một mốc triển khai hoặc verification quan trọng.
- Khi mục tiêu, blocker, lỗi chưa xử lý hoặc bước tiếp theo thay đổi đáng kể.

Không chờ đến khi context đã cạn mới tạo checkpoint.

## 3. Nội dung checkpoint bắt buộc

Checkpoint phải giữ đủ:

1. Mục tiêu hiện tại.
2. Quyết định đã chốt liên quan đến mục tiêu.
3. File đã tạo hoặc sửa.
4. Build/test/kiểm tra gần nhất, gồm trạng thái, lệnh và exit code nếu có.
5. Lỗi chưa xử lý.
6. Blocker.
7. Bước tiếp theo cụ thể.

Có thể thêm phạm vi không được thay đổi hoặc giả định quan trọng nếu cần cho việc bàn giao chính xác.

## 4. Nội dung không đưa vào project state

- Log terminal dài.
- Toàn bộ stdout/stderr khi một tóm tắt và vị trí artifact là đủ.
- Nội dung đã có nguyên văn trong product spec, architecture hoặc AGENTS.
- Lịch sử trò chuyện lặp lại.
- Suy đoán chưa được xác minh được trình bày như quyết định.
- Dữ liệu bí mật, token, mật khẩu hoặc giá trị biến được đánh dấu bí mật.

## 5. Cách ghi kết quả verification

Dùng đúng các trạng thái trong [`verification.md`](verification.md): `passed`, `failed`, `not run`, `blocked`.

Mỗi mục kiểm tra trong checkpoint nên ngắn gọn theo mẫu:

```text
- passed — `dotnet build ...` — exit 0 — không có compiler error.
- blocked — smoke test MEmu — `memuc.exe` chưa được cấu hình.
```

Không sao chép toàn bộ log. Nếu cần giữ log chi tiết, lưu artifact phù hợp rồi liên kết đến nó.

## 6. Khôi phục trong cuộc trò chuyện mới

Agent tiếp tục công việc phải:

1. Đọc toàn bộ `AGENTS.md`.
2. Đọc `docs/project-state.md`.
3. Đọc các entry liên quan trong `docs/decisions.md`.
4. Nạp tài liệu chuyên biệt theo bảng định tuyến trong `AGENTS.md`.
5. Kiểm tra trạng thái repository thay vì tin tuyệt đối vào checkpoint.
6. Tiếp tục từ “Bước tiếp theo”, không lặp lại công việc đã được chứng minh là hoàn tất.

Nếu project state mâu thuẫn với repository, repository và bằng chứng verification mới nhất được ưu tiên; cập nhật lại project state.

## 7. Mẫu checkpoint

```markdown
## Checkpoint — YYYY-MM-DD HH:mm, timezone

### Mục tiêu hiện tại
- ...

### Quyết định đã chốt
- ...

### File đã sửa
- ...

### Verification gần nhất
- <status> — `<command>` — exit <code/N/A> — <result>

### Lỗi chưa xử lý
- Không có. / ...

### Blocker
- Không có. / ...

### Bước tiếp theo
1. ...
```
