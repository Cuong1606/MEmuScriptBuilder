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
MultiInstanceExecutionRequest
MultiInstanceExecutionResult
InstanceExecutionResult
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
- `ApplicationSettings` schema 3 lưu cấu hình chạy đa instance gần nhất, mapping instance → script và cấu hình/bố cục cửa sổ. Cấu hình máy cục bộ này không thuộc document kịch bản hoặc `.memuscript`.
- Khi thêm field settings, mọi writer phải bảo toàn field không thuộc trách nhiệm của nó; không được dựng lại object chỉ chứa path hoặc một nhóm setting rồi làm mất cấu hình chạy.

## 8. Execution semantics

- Kịch bản chạy từng bước đúng thứ tự.
- Các bước bị tắt hoặc ghi chú không được thực thi và phải có trạng thái phù hợp.
- Chính sách dừng/tiếp tục khi lỗi là thuộc tính của từng bước.
- `ScriptExecutionEngine` tiếp tục chỉ chạy tuần tự trên đúng một instance. Scheduler đa instance nằm ở lớp trên và gọi engine độc lập cho mỗi target.
- Scheduler preflight toàn bộ target bằng truy vấn `listvms` read-only; không tự khởi động instance.
- Mặc định target đang tắt, mất hoặc không hợp lệ được ghi `Unavailable` và bỏ qua. Policy tùy chọn có thể dừng batch trước khi target hợp lệ nào được chạy.
- Target hợp lệ đầu tiên bắt đầu ngay. Với mỗi target tiếp theo, admission loop phải đợi `active < maxConcurrency`, sau đó mới chờ fixed/random launch spacing rồi mới gọi engine.
- Random spacing lấy mẫu mới cho từng lần khởi chạy sau máy đầu tiên. Delay phải dùng abstraction hỗ trợ `CancellationToken` để unit test không chờ thời gian thật.
- Mỗi instance có cancellation token liên kết giữa batch token và token riêng. Dừng một instance không hủy token khác; dừng tất cả hủy batch và không admission target mới.
- Exception hoặc kết quả thất bại của một engine invocation phải được chuyển thành kết quả riêng và không làm fault/cancel các target khác theo mặc định.
- Mọi target hợp lệ được admission đúng một lần nếu không có cancellation. Concurrency thực tế không được vượt giới hạn đã resolve.
- Progress nhiều instance luôn mang `InstanceIndex`; UI không dùng chung scalar step status/log cho các instance chạy đồng thời.
- Tọa độ lưu trong step được chuyển nguyên vẹn cho từng engine invocation. Coordinate mapper chỉ dùng khi capture; scheduler không scale, clamp hoặc truy vấn resolution để biến đổi tọa độ lúc chạy.
- Execution result phải giữ thời gian bắt đầu/kết thúc, exit code, stdout, stderr và lệnh đã thực thi.
- Các trạng thái tối thiểu: Chưa chạy, Đang chạy, Thành công, Thất bại, Đã bỏ qua và Đã hủy.

## 9. Multi-instance UI state

- `SelectedInstance` là instance focus cho preview, app picker và capture; nó không đại diện toàn bộ run target.
- Run target dùng collection ViewModel riêng với checkbox/select-all semantics.
- Mỗi phiên chạy tạo collection runtime riêng theo instance, chứa trạng thái instance, trạng thái step và log. Chọn một dòng instance chỉ đổi phần log/step đang quan sát, không làm mất kết quả instance khác.
- Callback phải được kiểm tra bằng run ID để progress đến muộn từ phiên cũ không ghi vào phiên hiện tại.
- UI khóa thay đổi script, target và cấu hình trong lúc chạy nhưng vẫn cho phép dừng từng instance hoặc dừng tất cả.
- Chế độ một kịch bản dùng snapshot chung theo logic; chế độ gán riêng resolve script ID trên UI rồi tạo một `ScriptDefinition` snapshot độc lập cho từng instance trước khi gọi scheduler.
- `MultiInstanceExecutionRequest.ScriptsByInstance` là snapshot map theo index. Scheduler chọn map này trước, fallback về `Script` để giữ chế độ một kịch bản/tương thích API; update/result mang script ID và tên để runtime UI không ghép nhầm.

## 10. Window grid architecture

- `WindowGridPlanner` ở Core là hàm thuần tính page, hàng, cột và bounds theo work area; không gọi Win32 hoặc MEMUC và không có giới hạn cứng số cửa sổ/cột.
- `IMemuWindowLayoutService` là abstraction để test không cần cửa sổ MEmu thật. Implementation Windows nằm ở Infrastructure và chỉ dùng `EnumDisplayMonitors`/`GetMonitorInfo`, `GetWindowRect`, `GetWindowThreadProcessId` và `SetWindowPos`; toàn bộ lời gọi Win32 được chạy ngoài WPF dispatcher.
- Work area từ `GetMonitorInfo` là ranh giới bố cục để không che taskbar. Màn hình được nhận bằng device name; nếu màn hình đã lưu không còn tồn tại thì fallback về primary.
- Mọi target dùng window handle và PID đã discovery từ `listvms`. Trước mỗi thao tác, adapter phải đối chiếu HWND vẫn thuộc PID dự kiến để không tác động nhầm handle đã bị Windows tái sử dụng; grid/focus không đổi index, process target hoặc handle. Coordinate capture tiếp tục tự đọc bounds/viewport hiện tại từ đúng handle nên không thêm scale tọa độ lúc chạy.
- Arrange áp dụng plan, đọc lại toàn bộ bounds thực tế và kiểm tra sai lệch hai chiều về vị trí/kích thước cùng overlap. Khi cửa sổ không nhận đúng kích thước yêu cầu, vòng auto-fit giảm items-per-page đến khi phù hợp hoặc còn một cửa sổ, rồi trả cảnh báo; không gọi command hoặc sửa settings của MEmu.
- Chế độ `MoveOnly` gọi `SetWindowPos` với cờ không đổi kích thước. Hai chế độ Auto/Custom mới được phép gửi width/height.
- Cửa sổ của trang khác được di chuyển tới các vị trí đỗ riêng ngoài toàn bộ work area đang hiển thị, không hide/minimize và không xếp cùng một tọa độ; việc thực thi script độc lập với vị trí này.
- Bố cục gốc chụp trước lần arrange đầu tiên của từng instance theo index và được bổ sung khi phát hiện instance mới. Khôi phục dùng handle/PID hiện tại của cùng index, read-back kết quả và báo rõ số cửa sổ thất bại; không lưu/phục hồi index MEmu.
