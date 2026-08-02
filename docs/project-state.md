# Project State

> Tài liệu này là checkpoint ngắn gọn của trạng thái hiện tại. Cập nhật theo `docs/agent/context-management.md`; không chép log terminal dài vào đây.

## Checkpoint — 2026-08-02, Asia/Saigon

### Mục tiêu hiện tại

- Giai đoạn 1 đã đạt build, toàn bộ automated tests, code review và runtime smoke test thủ công.
- Người dùng đã xác nhận toàn bộ runtime smoke-test checklist của Giai đoạn 1 là `passed`.
- Chưa bắt đầu Giai đoạn 2; chờ yêu cầu mới của người dùng.

### Trạng thái triển khai

- Solution `MEmuScriptStudio.sln` gồm App, Core, Infrastructure và hai test project, target `net8.0-windows`.
- Có core models, polymorphic `ScriptStep`, process runner abstraction/implementation, MEMUC command builder, parser `listvms`, path discovery và JSON settings store.
- UI WPF/MVVM tối thiểu cho phép hiển thị/chọn đường dẫn `memuc.exe`, làm mới và xem index/tên/trạng thái/PID của instance.
- Command preview dùng Windows quoting và không nhân đôi backslash thông thường.
- Parser chỉ nhận schema 5 trường đã xác minh: `index,title,windowHandle,status,pid`; hỗ trợ quoted title và bỏ qua dòng malformed mà không đoán schema.
- Process cleanup khi timeout/cancellation là best-effort với grace period hữu hạn; lỗi cleanup không che timeout/cancellation gốc.
- Settings được ghi qua temporary file rồi move; lỗi JSON/đọc/ghi được chặn ở ViewModel/App boundary và hiển thị hướng khắc phục thay vì đóng ứng dụng.

### Schema MEmu đã xác minh

- Đã tắt: `0,MASTER,0,0,0`.
- Đang chạy: `0,MASTER,12126050,1,5676`.
- Hai output trên được người dùng cho phép khảo sát trực tiếp trước corrective cycle. Không chạy thêm `memuc.exe` trong corrective cycle.

### File đã sửa trong corrective cycle

- `src/MEmuScriptStudio.Core/MEmu/MemuCommandBuilder.cs`
- `src/MEmuScriptStudio.Core/MEmu/MemuListVmsParser.cs`
- `src/MEmuScriptStudio.Infrastructure/Processes/ProcessRunner.cs`
- `src/MEmuScriptStudio.Infrastructure/Persistence/JsonSettingsStore.cs`
- `src/MEmuScriptStudio.App/App.xaml.cs`
- `src/MEmuScriptStudio.App/ViewModels/AsyncCommand.cs`
- `src/MEmuScriptStudio.App/ViewModels/MainViewModel.cs`
- Test builder/parser/instance service và project reference của Infrastructure.Tests.
- Thêm test cho `ProcessRunner`, `JsonSettingsStore` và `MainViewModel` failure paths.

### Verification gần nhất

- `passed` — `dotnet restore MEmuScriptStudio.sln` — exit 0 — tất cả project up-to-date.
- `passed` — `dotnet build MEmuScriptStudio.sln --no-restore` — exit 0 — 0 warning, 0 error.
- `passed` — `dotnet test MEmuScriptStudio.sln --no-build --no-restore` — exit 0 — Core 11/11, Infrastructure 13/13; tổng 24 passed, 0 failed, 0 skipped.
- `passed` — `code_reviewer` review toàn bộ corrective diff và re-review parser remediation — finding malformed CSV quote đã sửa; không còn finding actionable.
- `passed` — `git diff --check` trước QA — exit 0.
- `passed` — `dotnet build MEmuScriptStudio.sln --no-restore` trước runtime smoke test — exit 0 — 0 warning, 0 error.
- `passed` — launch `MEmuScriptStudio.App.exe` — exit N/A — process mở cửa sổ `MEmu Script Studio`, không crash; người dùng xác nhận giao diện không trắng.
- `passed` — runtime smoke test do người dùng quan sát — exit N/A — tự phát hiện và chọn thủ công đúng `memuc.exe`; chọn file sai báo lỗi nhưng ứng dụng không đóng; đóng/mở lại vẫn giữ đường dẫn.
- `passed` — runtime `listvms` qua nút Làm mới — exit code MEMUC không được UI hiển thị riêng — người dùng xác nhận instance `MASTER` hiển thị đúng trạng thái/PID cả khi MEmu chạy và tắt.
- `passed` — kiểm tra bố cục thủ công tại 1280×720 — exit N/A — người dùng xác nhận giao diện không bị cắt.

### Lỗi chưa xử lý

- Không có lỗi source/test đã biết trong phạm vi Giai đoạn 1.

### Blocker

- Không có blocker cho automated Definition of Done của Giai đoạn 1.
- Không còn blocker runtime đã biết trong phạm vi Giai đoạn 1.

### Git

- Baseline blocked trước corrective cycle: `996ef87daa190477c42738ec2699e4a45c103a7e` (`WIP: phase 1 blocked baseline`).
- Remote `origin`: `https://github.com/Cuong1606/MEmuScriptBuilder.git`.
- Corrective implementation, tests và checkpoint hoàn tất Giai đoạn 1 được commit với nội dung `Complete Phase 1 implementation and verification` và push lên `origin/main`.

### Bước tiếp theo

1. Chờ yêu cầu mới của người dùng.
2. Không bắt đầu Giai đoạn 2 khi chưa có yêu cầu mới.
