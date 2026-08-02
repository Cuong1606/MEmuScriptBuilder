# Project State

> Tài liệu này là checkpoint ngắn gọn của trạng thái hiện tại. Cập nhật theo `docs/agent/context-management.md`; không chép log terminal dài vào đây.

## Checkpoint — 2026-08-02, Asia/Saigon

### Mục tiêu hiện tại

- Hoàn thành Giai đoạn 1: nền tảng solution, MEmu discovery, command builder, parser, cấu hình và UI tối thiểu.
- Trạng thái: `blocked` sau giới hạn 3 vòng sửa–kiểm tra; chưa được chuyển sang Giai đoạn 2.

### Trạng thái triển khai

- Đã tạo solution `MEmuScriptStudio.sln` với App, Core, Infrastructure và hai test project, target `net8.0-windows`.
- Đã tạo core models, polymorphic `ScriptStep`, process runner abstraction/implementation, MEMUC command builder, parser `listvms`, path discovery và JSON settings store.
- Đã tạo UI WPF/MVVM tối thiểu để hiển thị/chọn đường dẫn `memuc.exe`, làm mới và xem danh sách instance.
- Đã thêm 10 unit test cho command builder, parser và instance service dùng process runner giả; không test nào gọi MEmu thật.
- Đã restore các NuGet được người dùng cho phép: Microsoft.Extensions.DependencyInjection, Microsoft.NET.Test.Sdk, MSTest.TestFramework và MSTest.TestAdapter.
- Không cài SDK hoặc phần mềm hệ thống. Không chạy ứng dụng, MEmu hay `memuc.exe`.

### Quyết định đã chốt

- D-007: `ScriptStep` dùng abstract base class và derived types, với discriminator ổn định của `System.Text.Json`.
- Agent chính là writer duy nhất; QA và reviewer không sửa authored files.

### Môi trường đã khảo sát

- Windows 10 build 19045 x64.
- .NET SDK duy nhất: 10.0.202; runtime .NET/WindowsDesktop 8.0.19 có mặt, reference packs 8 không có sẵn trước restore.
- Git 2.55.0.windows.3 có mặt; repository cục bộ đã được khởi tạo để lưu baseline blocked của Giai đoạn 1.
- `memuc.exe` không có trong PATH; tìm thấy `C:\Program Files\Microvirt\MEmu\memuc.exe` nhưng chưa chạy.

### Verification gần nhất

- `passed` — `dotnet restore MEmuScriptStudio.sln` — exit 0 — đủ 5 project up-to-date, 0 warning/error.
- `passed` — `dotnet build MEmuScriptStudio.sln --no-restore` — exit 0 — đủ 5 project build, 0 warning, 0 error.
- `failed` — `dotnet test MEmuScriptStudio.sln --no-build --no-restore` — exit 1 — 9 passed, 1 failed, 0 skipped, tổng 10.
- Test fail: `BuildListVms_UsesDirectExecutableAndSingleArgument`; preview nhân đôi dấu `\` của đường dẫn Windows.
- `not run` — UI runtime/manual smoke test.
- `not run` — MEmu smoke test; người dùng chưa cho phép chạy lệnh điều khiển MEmu thật.

### Findings reviewer chưa xử lý

1. `high`: parser đang coi field 2 là trạng thái và field 3 là PID; schema MEMUC 6 trường phổ biến đặt trạng thái ở field 3 và PID ở field 4.
2. `medium`: formatter preview nhân đôi backslash và không tương đương `ArgumentList`.
3. `medium`: cleanup timeout/cancellation của `ProcessRunner` có thể chờ vô hạn hoặc che exception gốc.
4. `medium`: lỗi đọc/ghi settings có thể thoát qua `async void` và làm ứng dụng đóng.
5. `medium`: thiếu test cho process runner, persistence và failure paths của ViewModel.

### Blocker

- Đã dùng đủ 3 vòng sửa–kiểm tra theo workflow nhưng test suite còn 1 failure; không được tuyên bố Giai đoạn 1 hoàn thành.
- Parser chưa được xác minh với output `memuc listvms` thật và reviewer phát hiện mapping schema 6 trường có khả năng sai.

### Checkpoint Git

- Đã thêm `.gitignore` cho build/test artifacts, Visual Studio state, runtime logs và settings riêng của máy người dùng.
- Đã rà soát các file authored/staged; không phát hiện mật khẩu, token, API key hoặc credential. Tên thuộc tính `IsSecret` không chứa dữ liệu bí mật.
- Baseline được commit cục bộ với nội dung `WIP: phase 1 blocked baseline`; không cấu hình remote và không push.

### Bước tiếp theo

1. Khi người dùng cho phép một lượt sửa–verification mới: sửa preview quoting, parser schema 6 trường, process cleanup và error boundary persistence; bổ sung test tương ứng.
2. Chạy lại restore/build/test bằng `qa_verifier`, rồi review lại các vùng đã sửa.
3. Chỉ sau khi toàn bộ test passed mới cân nhắc hoàn tất Giai đoạn 1; smoke test MEmu vẫn cần quyền riêng.
