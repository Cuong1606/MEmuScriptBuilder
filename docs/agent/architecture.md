# Architecture and Technical Constraints

Đọc tài liệu này trước khi thay đổi cấu trúc solution, project, model, command builder, process runner, execution engine, persistence hoặc dependency.

## 1. Technology baseline

- C# và .NET 8.
- WPF với kiến trúc MVVM.
- Dependency Injection của .NET.
- `System.Text.Json` cho persistence JSON.
- `ProcessStartInfo` để chạy `memuc.exe`.
- `async`/`await` cho toàn bộ quá trình thực thi lệnh.
- `CancellationToken` cho thao tác hủy.
- `ObservableCollection` cho dữ liệu hiển thị động.

Có thể dùng thư viện MVVM ổn định nếu thực sự cần, nhưng phải giải thích lý do trước khi thêm dependency. Không dùng Electron, Python hoặc server web cho phiên bản đầu tiên. Không đổi công nghệ chính khi chưa được người dùng chấp thuận.

## 2. Solution layout

Tổ chức solution theo hướng:

```text
src/
  MEmuScriptStudio.App/
  MEmuScriptStudio.Core/
  MEmuScriptStudio.Infrastructure/

tests/
  MEmuScriptStudio.Core.Tests/
  MEmuScriptStudio.Infrastructure.Tests/

docs/
```

Phân tách tối thiểu:

- Models.
- ViewModels.
- Views.
- Services.
- Command builders.
- Script execution engine.
- MEmu process runner.
- Persistence.
- Validation.
- Logging.

Không đặt toàn bộ logic trong code-behind WPF. Code-behind chỉ dùng cho hành vi giao diện khó biểu diễn hợp lý bằng binding.

## 3. Core models

Thiết kế model tương đương:

```text
MemuInstance
ScriptDefinition
ScriptStep
ScriptVariable
ExecutionRequest
ExecutionResult
StepExecutionResult
ApplicationSettings
```

`ScriptStep` phải hỗ trợ nhiều loại bước mà không trở thành một class khổng lồ khó bảo trì. Có thể dùng:

- Base class và các derived step type; hoặc
- Discriminated model bằng enum kết hợp dữ liệu có validation rõ ràng.

Trước khi triển khai, agent phải giải thích lựa chọn, trade-off và ảnh hưởng đến serialization/validation; ghi quyết định đã chốt vào [`../decisions.md`](../decisions.md).

## 4. Process runner abstraction

- Tạo abstraction cho process runner để unit test có thể mock kết quả mà không chạy MEmu thật.
- `memuc.exe` phải được gọi trực tiếp cho từng bước thông thường.
- Không dùng `cmd.exe` cho các bước thông thường.
- Không nối các bước bằng chuỗi shell hoặc `&&` khi ứng dụng có thể thực thi riêng.
- Ưu tiên `ProcessStartInfo.ArgumentList` để giảm lỗi escape ký tự.
- Không tạo chuỗi tham số thiếu kiểm soát.
- Xử lý chính xác đường dẫn có khoảng trắng.
- Luôn redirect và thu thập standard output cùng standard error.
- Luôn kiểm tra exit code; process lỗi không được ánh xạ thành thành công.
- Lệnh xem trước phải tương đương logic với lệnh thực tế được chạy.
- Delay dùng `Task.Delay`, không khởi chạy `timeout.exe`.
- Mỗi lệnh có timeout riêng và hỗ trợ `CancellationToken`.
- Thực thi không được làm đóng băng UI.

## 5. MEmu discovery and targeting

- Tự động tìm `memuc.exe` nếu có thể, nhưng không hard-code một đường dẫn cài đặt duy nhất.
- Cho phép chọn thủ công và lưu đường dẫn trong application settings.
- Kiểm tra file tồn tại trước khi chạy.
- Không gọi lệnh nếu chưa xác định được máy ảo mục tiêu.
- Không giả định máy ảo đầu tiên có index `0`.
- Parser `memuc listvms` phải giữ index, tên, trạng thái và PID nếu dữ liệu có PID.

## 6. Safety boundaries

- Không xóa file hoặc máy ảo MEmu.
- Không cung cấp sẵn `memuc remove`, clone, import, export hoặc reset máy ảo trong MVP.
- Lệnh thô có khả năng nguy hiểm phải có cảnh báo rõ ràng trước khi chạy.
- Không thay đổi cấu hình máy ảo ngoài yêu cầu của kịch bản.
- Không tự động tải hoặc cài phần mềm bên ngoài.
- Không gửi dữ liệu ra Internet.

## 7. Persistence and sensitive data

- Tất cả dữ liệu ứng dụng được lưu cục bộ.
- JSON phải có version để hỗ trợ migration cấu trúc sau này.
- Không lưu mật khẩu hoặc token dưới dạng văn bản thuần.
- Biến được đánh dấu bí mật không được tự động ghi giá trị vào log.
- Persistence phải hỗ trợ đóng/mở lại ứng dụng mà kịch bản vẫn còn.

## 8. Execution semantics

- Kịch bản chạy từng bước đúng thứ tự.
- Các bước bị tắt hoặc ghi chú không được thực thi và phải có trạng thái phù hợp.
- Chính sách dừng/tiếp tục khi lỗi là thuộc tính của từng bước.
- Chạy trên nhiều máy ảo theo thứ tự; MVP chưa yêu cầu chạy song song.
- Execution result phải giữ thời gian bắt đầu/kết thúc, exit code, stdout, stderr và lệnh đã thực thi.
- Các trạng thái tối thiểu: Chưa chạy, Đang chạy, Thành công, Thất bại, Đã bỏ qua và Đã hủy.
