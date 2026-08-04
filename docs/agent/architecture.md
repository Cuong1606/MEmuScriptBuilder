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

### Startup lifecycle

- `App.xaml` giữ `ShutdownMode="OnExplicitShutdown"` trong khoảng bootstrap DI. Sau khi resolve đúng một `MainWindow`, `App` gán `Application.MainWindow`, chuyển sang `OnMainWindowClose`, gọi `Show()` đúng một lần và đợi `ContentRendered` đầu tiên trước khi await `MainViewModel.InitializeAsync`.
- ViewModel bắt đầu ở trạng thái `IsInitializing=true`. Workspace bind với readiness để không nhận thao tác khi dữ liệu chưa sẵn sàng; loading/error overlay vẫn hiển thị trong chính MainWindow.
- Exception khởi tạo ngoài các lỗi phục hồi cục bộ được ghi bằng `StartupErrorReporter` nhưng không đóng cửa sổ đã hiển thị. ViewModel chuyển sang initialization-error state và giữ workspace bị khóa. Lỗi phục hồi được vẫn cho phép workspace hoạt động nhưng phải ghi cùng startup log và hiển thị cảnh báo trong status.
- Smoke launcher chỉ quan sát process/window tối đa 45 giây; `MainWindowHandle != 0` là điều kiện `READY`, còn `Responding` và title vẫn được refresh/in như diagnostics tại thời điểm đó. Launcher không build, kill, restart, mở lần hai hoặc tự điều tra khi timeout.

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
- `ApplicationSettings` schema 5 lưu đường dẫn MEMUC, tên ứng dụng tùy chỉnh, launch spacing, policy preflight, mapping instance → script và script dùng chung. Field target-scope/concurrency cùng object bố trí cửa sổ của schema cũ được bỏ qua khi load và không được ghi lại. Cấu hình máy cục bộ này không thuộc document kịch bản hoặc `.memuscript`.
- Khi thêm field settings, mọi writer phải bảo toàn field không thuộc trách nhiệm của nó; không được dựng lại object chỉ chứa path hoặc một nhóm setting rồi làm mất cấu hình chạy.

## 8. Execution semantics

- Kịch bản chạy từng bước đúng thứ tự.
- Các bước bị tắt hoặc ghi chú không được thực thi và phải có trạng thái phù hợp.
- Chính sách dừng/tiếp tục khi lỗi là thuộc tính của từng bước.
- `ScriptExecutionEngine` tiếp tục chỉ chạy tuần tự trên đúng một instance. Scheduler đa instance nằm ở lớp trên và gọi engine độc lập cho mỗi target.
- Scheduler preflight toàn bộ target bằng truy vấn `listvms` read-only; không tự khởi động instance.
- Mặc định target đang tắt, mất hoặc không hợp lệ được ghi `Unavailable` và bỏ qua. Policy tùy chọn có thể dừng batch trước khi target hợp lệ nào được chạy.
- Mỗi lời gọi scheduler là một launch group độc lập. Target hợp lệ đầu tiên bắt đầu ngay; mỗi target tiếp theo chỉ chờ fixed/random launch spacing của group rồi gọi engine, không chờ target trước hoàn tất. ViewModel điều phối nhiều group và reserve instance index để chống nhận trùng active/waiting.
- Random spacing lấy mẫu mới cho từng lần khởi chạy sau máy đầu tiên. Delay phải dùng abstraction hỗ trợ `CancellationToken` để unit test không chờ thời gian thật.
- Mỗi launch group có batch token riêng và mỗi instance có token liên kết riêng. UI dừng group truyền trực tiếp `LaunchGroupId` tới đúng `MultiInstanceExecutionSession.StopAll`; dừng một group không được chạm token của group khác. Dừng instance chỉ hủy token instance; dừng tất cả mới lặp qua mọi session.
- Exception hoặc kết quả thất bại của một engine invocation phải được chuyển thành kết quả riêng và không làm fault/cancel các target khác theo mặc định.
- Mọi target hợp lệ đã nhận vào group được admission đúng một lần nếu không có cancellation.
- Progress nhiều instance luôn mang `LaunchGroupId` và `InstanceIndex`; UI không dùng chung scalar step status/log cho các instance hoặc lần chạy lại.
- Tọa độ lưu trong step được chuyển nguyên vẹn cho từng engine invocation. Coordinate mapper chỉ dùng khi capture; scheduler không scale, clamp hoặc truy vấn resolution để biến đổi tọa độ lúc chạy.
- Execution result phải giữ thời gian bắt đầu/kết thúc, exit code, stdout, stderr và lệnh đã thực thi.
- Các trạng thái tối thiểu: Chưa chạy, Đang chạy, Thành công, Thất bại, Đã bỏ qua và Đã hủy.

## 9. Multi-instance UI state

- `MainViewModel` là state dùng chung duy nhất cho MainWindow và Control Center. `RunControlPanel` có visual tree riêng, được tạo mới cùng mỗi `ControlCenterWindow`; chỉ `DataContext` được chia sẻ, không di chuyển/reuse `UIElement` từ MainWindow hoặc window đã đóng.
- Window manager giữ tối đa một Control Center đang mở, chỉ restore/activate cửa sổ hiện có, bỏ reference khi `Closed`, và tạo window mới sau khi đóng hoặc khởi tạo/`Show` thất bại trước khi window trở thành live. Nếu `Show`/`Activate` ném sau khi HWND đã tồn tại, manager vẫn giữ reference để không tạo duplicate. Lệnh mở chặn exception ở UI boundary, ghi đầy đủ vào `application-error.log` và báo người dùng mà không làm đóng MainWindow. Global `DispatcherUnhandledException` cũng ghi log nhưng giữ `Handled=false` để không âm thầm nuốt lỗi không liên quan. `Application.MainWindow`/shutdown mode và vòng đời singleton của ViewModel/scheduler không đổi.
- `SelectedInstance` là instance focus cho preview, app picker và capture; nó không đại diện toàn bộ run target.
- Run target dùng collection ViewModel riêng; checkbox chỉ chọn mục cho thao tác hiện tại và được bỏ cho mục đã nhận thành công.
- Runtime active được nhóm và tra cứu theo `(LaunchGroupId, InstanceIndex)`, chỉ giữ trạng thái instance, bước hiện tại và state cần cho cancellation/tóm tắt. Counter group/aggregate cập nhật theo transition thay vì quét lại toàn bộ collection. Khi scheduler terminal, ViewModel tạo một snapshot `LatestRunResult` chỉ chứa scalar, mô tả hữu hạn và tóm tắt lỗi/hủy ngắn, tháo subscription rồi loại group/instance khỏi active collections theo thời gian tuyến tính. Snapshot mới thay snapshot cũ, không giữ full log, không persist và có lệnh xóa riêng không tác động active session.
- Callback phải khớp launch group còn active để progress đến muộn không ghi vào lần chạy lại. Registry active index chống một instance nằm trong hai group active/waiting.
- UI vẫn cho đổi target và dropdown script trong khi group chạy; snapshot group không đổi. Mutation danh sách step vẫn bị khóa để tránh xung đột editor/persistence.
- Chế độ một kịch bản resolve `CommonRunScript` từ dropdown Control Center (mặc định script editor lúc khởi tạo) rồi clone snapshot; chế độ gán riêng resolve script ID trên từng row rồi tạo một `ScriptDefinition` snapshot độc lập cho từng instance trước khi gọi scheduler.
- `MultiInstanceExecutionRequest.ScriptsByInstance` là snapshot map theo index. Scheduler chọn map này trước, fallback về `Script` để giữ chế độ một kịch bản/tương thích API; update/result mang script ID và tên để runtime UI không ghép nhầm.

## 10. Input-capture geometry

- `ScreenPoint` và `ScreenRectangle` là model Core dùng chung cho overlay, viewport và screen bounds; chúng không đại diện cho cấu hình bố trí cửa sổ.
- Coordinate capture dùng window handle/PID đã discovery từ `listvms`, đối chiếu handle với process đích và đọc client/child bounds hiện tại bằng Win32 read-only.
- `MemuViewportSelector` loại toolbar/child nhỏ, còn `MemuCoordinateMapper` fit theo guest aspect ratio và ánh xạ screen point sang guest point. Viewport được đọc lại trong phiên capture để theo resize, DPI và letterbox.
- Hook tap/swipe chỉ capture và suppress input theo chính sách hiện hành; subsystem này không gọi API move/resize/focus/restore cửa sổ và không thay đổi execution engine.

## 11. UI composition và step clipboard

- MainWindow chỉ sở hữu editor và summary counts. Control Center là presentation duy nhất cho run/stop, launch group, active detail gọn và kết quả lần chạy gần nhất; tất cả dùng cùng `MainViewModel`, scheduler và session registry.
- `RunTargets` giữ target chạy theo index model; checkbox `IsSelected` chỉ chọn mục cho thao tác chạy hoặc gán kịch bản hiện tại.
- Step clipboard nằm trong lifetime của `MainViewModel`, chứa deep-clone snapshot không tham chiếu script nguồn. Paste clone lần nữa để cấp ID mới; Undo entry được ghi vào history của script đích. `MainWindow` chỉ route Ctrl+C/Ctrl+V/Ctrl+Z/Delete tới các `ICommand` editor hiện có khi focus không nằm trong TextBox, PasswordBox hoặc ComboBox editable; selection bước hợp lệ vẫn dùng được khi DataGrid mất focus. Control nhập liệu giữ hành vi clipboard/Undo/Delete native và không có logic mutation danh sách song song trong code-behind.
