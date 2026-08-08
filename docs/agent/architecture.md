# Architecture and Technical Constraints

Đọc tài liệu này trước khi thay đổi cấu trúc solution, project, model, command builder, process runner, execution engine, persistence hoặc dependency. Đây là kiến trúc của một ứng dụng productivity/operations native Windows WPF, không phải web frontend. Trạng thái feature end-to-end nằm trong [`../project-state.md`](../project-state.md); sự tồn tại của model/property/API không tự chứng minh feature đã được triển khai.

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

- Trước khi tạo `ServiceCollection` hoặc `MainWindow`, `App` giành named mutex riêng theo Windows user/session. Primary mở named-pipe listener `CurrentUserOnly`; secondary chỉ gửi `ActivateMainWindow`, shutdown và không chạy DI/bootstrap. Mutex, pipe và listener được hủy/dispose khi application exit.
- Sau khi resolve đúng một `MainWindow`, `App` gán `Application.MainWindow`, giữ `OnMainWindowClose`, gọi `Show()` đúng một lần và đợi `ContentRendered` đầu tiên trước khi await `MainViewModel.InitializeAsync`. Activation đến sớm được giữ pending đến `ContentRendered`, sau đó được marshal qua Dispatcher để show/restore/activate cửa sổ hiện có mà không tạo window hoặc dùng `Topmost`.
- ViewModel bắt đầu ở trạng thái `IsInitializing=true`. Chỉ các hành động phụ thuộc MEmu/readiness bị khóa trong lúc khởi tạo; editor/library vẫn usable và trạng thái được hiển thị qua status message trong MainWindow, không có initialization overlay toàn workspace.
- Exception khởi tạo ngoài các lỗi phục hồi cục bộ được ghi bằng `StartupErrorReporter` nhưng không đóng cửa sổ đã hiển thị. ViewModel báo initialization error/cảnh báo trong status; phần editor/library vẫn dùng được còn hành động cần MEmu tiếp tục bị gate theo readiness/path.
- Smoke launcher chỉ quan sát process/window tối đa 45 giây; `MainWindowHandle != 0` là điều kiện `READY`, còn `Responding` và title vẫn được refresh/in như diagnostics tại thời điểm đó. Launcher không build, kill, restart, mở lần hai hoặc tự điều tra khi timeout.

## 3. Core models

Thiết kế model tương đương:

```text
MemuInstance
ScriptDefinition
ScriptStep
ExecutionRequest
ExecutionResult
StepExecutionResult
MultiInstanceExecutionRequest
MultiInstanceExecutionResult
InstanceExecutionResult
ApplicationSettings
```

`ScriptVariable` và `ScriptDefinition.DefaultInstanceIndex` hiện chỉ là infrastructure được persist/clone/transfer; chưa có UI hoặc behavior end-to-end. Không dùng chúng như bằng chứng rằng placeholders hay default-target selection đã implemented.

`ScriptStep` hiện là abstract base class với derived type cho từng loại bước và discriminator JSON ổn định. Mỗi derived type giữ dữ liệu/validation riêng để tránh một class chứa nhiều field không áp dụng. Nếu thay đổi strategy model, phải đánh giá serialization/migration/validation và cập nhật [`../decisions.md`](../decisions.md).

## 4. Process runner abstraction

- Tạo abstraction cho process runner để unit test có thể mock kết quả mà không chạy MEmu thật.
- `memuc.exe` phải được gọi trực tiếp cho từng bước thông thường.
- Không dùng `cmd.exe` cho các bước thông thường.
- Không nối các bước bằng chuỗi shell hoặc `&&` khi ứng dụng có thể thực thi riêng.
- Ưu tiên `ProcessStartInfo.ArgumentList` để giảm lỗi escape ký tự.
- Không tạo chuỗi tham số thiếu kiểm soát.
- Xử lý chính xác đường dẫn có khoảng trắng.
- Luôn redirect và drain đồng thời standard output cùng standard error. Mỗi stream chỉ giữ tối đa 64 Ki ký tự trong result; phần vượt quota phải được discard khi drain và result phải có marker truncate riêng cho stream đó.
- Luôn kiểm tra exit code; process lỗi không được ánh xạ thành thành công.
- Lệnh xem trước phải tương đương logic với lệnh thực tế được chạy.
- Delay dùng `Task.Delay`, không khởi chạy `timeout.exe`.
- Mỗi lệnh có timeout riêng và hỗ trợ `CancellationToken`.
- Mọi `ProcessRequest` chạy `memuc.exe` phải chọn riêng `CancellationPolicy = WaitForNaturalExit` và `TimeoutPolicy = DirectProcessOnly`. User cancellation chỉ ngăn bước kế tiếp: runner tuyệt đối không gọi kill và tiếp tục chờ đúng command process. Deadline timeout gốc vẫn chạy độc lập; nếu process thoát trước deadline thì drain/cleanup tự nhiên, nếu timeout thắng thì chỉ timeout policy mới được terminate trực tiếp sau grace period. Tuyệt đối không tree-kill hierarchy MEmu. Reservation chỉ được release sau process exit, stream cleanup và session terminal.
- Thực thi không được làm đóng băng UI.

## 5. MEmu discovery and targeting

- Tự động tìm `memuc.exe` nếu có thể, nhưng không hard-code một đường dẫn cài đặt duy nhất.
- Cho phép chọn thủ công và lưu đường dẫn trong application settings.
- Kiểm tra file tồn tại trước khi chạy.
- Không gọi lệnh nếu chưa xác định được máy ảo mục tiêu.
- Không giả định máy ảo đầu tiên có index `0`.
- Parser `memuc listvms` phải giữ index, tên, trạng thái và PID nếu dữ liệu có PID.
- Health probe runtime dùng PID instance-specific vừa nhận từ `listvms` để xác nhận host `MEmu.exe` còn tồn tại bằng Tool Help snapshot. Runtime MEmu hiện hành khởi chạy `MEmuHeadless.exe` qua `MEmuSVC.exe`, không bảo đảm quan hệ parent/descendant với host; probe vì vậy đọc command line hữu hạn bằng Windows `NtQueryInformationProcess` và chỉ map Headless có `--comment` khớp chính xác tên VM từ `MemuInstance`. PID cùng creation time của core tìm thấy tại preflight được pin cho cả run, nên Headless của instance khác, replacement hoặc PID reuse không che mất việc core ban đầu đã chết. Snapshot/command-line/creation-time lỗi, thiếu PID hoặc mapping mơ hồ là `Unknown`, không phải core-dead; mapping chưa pin tại preflight không được nâng terminal thành `Succeeded` bởi kết quả map muộn.

## 6. Safety boundaries

- Không xóa file hoặc máy ảo MEmu.
- Không cung cấp sẵn `memuc remove`, clone, import, export hoặc reset máy ảo trong MVP.
- Nếu direct-MEMUC/raw step được triển khai trong tương lai, lệnh có khả năng nguy hiểm phải có cảnh báo rõ ràng trước khi chạy. Hiện source chưa có loại bước hoặc route này.
- Không thay đổi cấu hình máy ảo ngoài yêu cầu của kịch bản.
- Không tự động tải hoặc cài phần mềm bên ngoài.
- Không gửi dữ liệu ra Internet.

## 7. Persistence and sensitive data

- Tất cả dữ liệu ứng dụng được lưu cục bộ.
- JSON phải có version để hỗ trợ migration cấu trúc sau này.
- Không lưu mật khẩu hoặc token dưới dạng văn bản thuần.
- Biến được đánh dấu bí mật không được tự động ghi giá trị vào log.
- Persistence phải hỗ trợ đóng/mở lại ứng dụng mà kịch bản vẫn còn.
- `ApplicationSettings` schema 7 lưu đường dẫn MEMUC, tên ứng dụng tùy chỉnh, launch spacing, policy preflight, mapping instance → script, script dùng chung và preference bố cục riêng của Control Center (window size/maximized, tỷ lệ splitter thiết lập–runtime và `RecentListRatio`). Width splitter pixel của schema 6 vẫn được đọc để migrate sau khi Grid có `ActualWidth` usable; save mới chỉ ghi ratio. Hai splitter runtime đều dùng native WPF với Star definitions; persistence chỉ capture Actual size khi đóng và restore Star ratio sau `Loaded`, không can thiệp trong drag/resize. Target-scope/concurrency cùng object bố trí cửa sổ MEmu của schema cũ bị bỏ qua khi load và không được ghi lại. Cấu hình máy cục bộ này không thuộc document kịch bản hoặc `.memuscript`.
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
- Mỗi launch group có batch token riêng và mỗi instance có token liên kết riêng. Backend giữ group/session isolation, nhưng UI hiện chỉ expose dừng từng instance, các instance đã chọn hoặc toàn bộ; không có action dừng riêng một group. Dừng instance chỉ hủy token instance; Dừng tất cả mới lặp qua mọi session.
- Stop request được ghi nhận đồng bộ trên runtime item trước khi cancel token: row hiển thị `Đang dừng…`, khóa lệnh Stop và giữ reservation đến khi execution/session cleanup terminal. Progress non-terminal đến muộn không được mở lại Stop hoặc xóa feedback; terminal update cuối cùng thay thế trạng thái stop-requested.
- Scheduler tuần tự hóa việc nhận Stop với terminal commit theo từng instance. Stop được nhận trước commit buộc terminal `Cancelled`; terminal đã commit trước làm Stop bị từ chối và UI không phát feedback `Đang dừng…` giả.
- MainWindow close trong khi execution/cleanup còn active phải khóa admission mới, gọi cùng Stop-all cancellation semantics, giữ cửa sổ/application sống và chỉ approve WPF Close sau khi mọi session terminal cùng reservation đã release. Close lặp lại trong lúc resolve/cleanup là idempotent và không được đi xuyên qua WPF `Closing` reentrancy.
- Exception hoặc kết quả thất bại của một engine invocation phải được chuyển thành kết quả riêng và không làm fault/cancel các target khác theo mặc định.
- Health được kiểm tra hữu hạn tại preflight, trước process-backed step, sau Delay/composite Delay và ngay trước terminal `Succeeded`; không polling và không thêm sleep. Core của đúng instance đã xác nhận mất/exited làm target `Unavailable` và ngăn bước mới. `Unknown` được phép tiếp tục để probe sau có thể phục hồi, nhưng `Unknown` tại final boundary thành `Failed` với message xác minh health, không thành false `Succeeded` hay false core-dead.
- Mọi target hợp lệ đã nhận vào group được admission đúng một lần nếu không có cancellation.
- Progress nhiều instance luôn mang `LaunchGroupId` và `InstanceIndex`; UI không dùng chung scalar step status/log cho các instance hoặc lần chạy lại.
- Tọa độ lưu trong step được chuyển nguyên vẹn cho từng engine invocation. Coordinate mapper chỉ dùng khi capture; scheduler không scale, clamp hoặc truy vấn resolution để biến đổi tọa độ lúc chạy.
- Execution result phải giữ thời gian bắt đầu/kết thúc, exit code, stdout, stderr và lệnh đã thực thi.
- Contract terminal theo target dùng `Succeeded`, `Failed`, `Unavailable` và `Cancelled`; UI tương ứng hiển thị **Thành công**, **Lỗi**, **Không khả dụng** và **Đã hủy**. `Unavailable` áp dụng khi target không thể admission/preflight hoặc core của đúng instance được xác nhận đã mất trong run, không phải status `Đã bỏ qua` riêng.

## 9. Multi-instance UI state

- `MainViewModel` là state dùng chung duy nhất cho MainWindow và Control Center. `RunControlPanel` có visual tree riêng, được tạo mới cùng mỗi `ControlCenterWindow`; chỉ `DataContext` được chia sẻ, không di chuyển/reuse `UIElement` từ MainWindow hoặc window đã đóng.
- Window manager giữ tối đa một Control Center đang mở, chỉ restore/activate cửa sổ hiện có, bỏ reference khi `Closed`, và tạo window mới sau khi đóng hoặc khởi tạo/`Show` thất bại trước khi window trở thành live. Nếu `Show`/`Activate` ném sau khi HWND đã tồn tại, manager vẫn giữ reference để không tạo duplicate. Lệnh mở chặn exception ở UI boundary, ghi đầy đủ vào `application-error.log` và báo người dùng mà không làm đóng MainWindow. Global `DispatcherUnhandledException` cũng ghi log nhưng giữ `Handled=false` để không âm thầm nuốt lỗi không liên quan. `Application.MainWindow`/shutdown mode và vòng đời singleton của ViewModel/scheduler không đổi.
- `SelectedInstance` là instance focus cho preview, app picker và capture; nó không đại diện toàn bộ run target.
- Run target dùng collection ViewModel riêng; checkbox chỉ chọn instance hiện đang chạy, chưa reserved cho thao tác hiện tại, và được bỏ ngay khi target được nhận thành công hoặc refresh cho thấy instance đã tắt.
- Runtime active được nhóm và tra cứu theo `(LaunchGroupId, InstanceIndex)` ở backend nhưng UI luôn trình bày một DataGrid phẳng; item chỉ giữ trạng thái instance, bước cuối có ý nghĩa, message bounded và state cần cho cancellation/tóm tắt. Projection search/filter dùng lại reference item, tháo subscription khi item rời collection. Counter group/aggregate cập nhật theo transition thay vì quét lại toàn bộ collection. Khi scheduler terminal, ViewModel tạo snapshot chỉ chứa scalar, mô tả hữu hạn và tóm tắt bounded, tháo subscription rồi loại group/instance khỏi active collections theo thời gian tuyến tính. Snapshot được thêm newest-first vào collection RAM tối đa 20, không giữ full log/execution/task, không persist và có lệnh xóa riêng không tác động active session.
- Refresh discovery được phép khi execution active. Target đang reserved nhưng tạm vắng khỏi kết quả discovery vẫn được giữ trong `RunTargets` với reservation hiện tại; refresh không tạo admission mới, không đổi session/token và loại row đó ngay khi reservation kết thúc nếu snapshot discovery mới nhất vẫn không còn target.
- Callback phải khớp launch group còn active để progress đến muộn không ghi vào lần chạy lại. Registry active index chống một instance nằm trong hai group active/waiting.
- UI vẫn cho đổi target, dropdown script và chỉnh thư viện khi group chạy; admission snapshot của group không đổi. Editor/persistence phải tự bảo vệ draft và race, không được sửa snapshot đang chạy.
- Mỗi launch group clone toàn bộ library đúng một lần vào `ExecutionScriptLibrarySnapshot` đóng gói, không expose graph nội bộ và không còn liên hệ với model editor. Chế độ một kịch bản và gán riêng đều resolve root script từng target từ snapshot chung đó.
- `MultiInstanceExecutionRequest.ScriptsByInstance` giữ root admission theo index và `ScriptLibrarySnapshot` là nguồn snapshot dùng chung cho group. Ngay trước engine invocation, scheduler materialize chỉ closure root/composite cần thiết thành graph mutable riêng cho target; không target nào dùng chung `ScriptDefinition`, step, composite item hoặc result. Fallback về `Script` giữ tương thích API; update/result vẫn mang script ID và tên để runtime UI không ghép nhầm.

## 10. Input-capture geometry

- `ScreenPoint` và `ScreenRectangle` là model Core dùng chung cho overlay, viewport và screen bounds; chúng không đại diện cho cấu hình bố trí cửa sổ.
- Coordinate capture dùng window handle/PID đã discovery từ `listvms`, đối chiếu handle với process đích và đọc client/child bounds hiện tại bằng Win32 read-only.
- `MemuViewportSelector` loại toolbar/child nhỏ, còn `MemuCoordinateMapper` fit theo guest aspect ratio và ánh xạ screen point sang guest point. Viewport được đọc lại trong phiên capture để theo resize, DPI và letterbox.
- Hook tap/swipe chỉ capture và suppress input theo chính sách hiện hành; subsystem này không gọi API move/resize/focus/restore cửa sổ và không thay đổi execution engine.

## 11. UI composition và step clipboard

- MainWindow chỉ sở hữu editor và summary counts. Control Center là presentation duy nhất cho run/stop, bảng active phẳng và tối đa 20 kết quả gần đây; hai tab cấp cao tách active khỏi recent, tab recent dùng native row splitter giữa list/detail. Backend vẫn định danh launch group nhưng UI history dùng `RunDescription`, không nhấn mạnh group card/label. Tất cả dùng cùng `MainViewModel`, scheduler và session registry.
- `RunTargets` giữ target chạy theo index model; checkbox `IsSelected` chỉ chọn mục cho thao tác chạy hoặc gán kịch bản hiện tại.
- Step clipboard nằm trong lifetime của `MainViewModel`, chứa deep-clone snapshot không tham chiếu script nguồn. Paste clone lần nữa để cấp ID mới; Undo entry được ghi vào history của script đích. `MainWindow` chỉ route Ctrl+C/Ctrl+V/Ctrl+Z/Delete tới các `ICommand` editor hiện có khi focus không nằm trong TextBox, PasswordBox hoặc ComboBox editable; selection bước hợp lệ vẫn dùng được khi DataGrid mất focus. Control nhập liệu giữ hành vi clipboard/Undo/Delete native và không có logic mutation danh sách song song trong code-behind.

## 12. Composite scripts

- `ScriptDefinition.Kind` dùng discriminator chuỗi `Regular`/`Composite`; field vắng mặt mặc định `Regular`. Regular chỉ có `Steps`; Composite chỉ có `CompositeItems` polymorphic `scriptReference`/`delay`.
- `ScriptLibraryValidator` là trust boundary dùng cho store, transfer và admission: ID không rỗng/trùng, reference phải tồn tại và trỏ đúng Regular, nested composite bị từ chối.
- `CompositeScriptExecutionEngine` bọc `ScriptExecutionEngine`: child script tiếp tục dùng engine tuần tự hiện có; reference policy chỉ quyết định đi tiếp sau child failure, cancellation luôn dừng. `CompositeExecutionContext` mang composite/item/occurrence/child/step identity nhưng snapshot kết quả gần đây chỉ giữ đường dẫn lỗi gọn.
- ViewModel tạo một `ExecutionScriptLibrarySnapshot` cho toàn library trước scheduler admission và group dùng chung nguồn snapshot đóng gói đó. Scheduler tạo execution graph riêng cho từng target từ snapshot; sửa library sau click không ảnh hưởng execution active và runtime mutation của một instance không xuyên sang instance khác.
- Clipboard composite và clipboard step là hai buffer khác kiểu. Import/export validate toàn bundle trước mutation; export composite lấy closure child Regular và copy import remap script, step, item cùng reference.

## 13. Chrome CDP và migration bước đã loại bỏ

- `ISpecializedStepExecutor` chỉ route bước đóng tab Chrome; model/selector/execution hiện hành không còn hai bước Android maintenance đã rút. Persistence và transfer tiền xử lý discriminator legacy thành `NoteStep` bị tắt, giữ ID/tên/thứ tự; lần save kế tiếp chỉ ghi discriminator `note`.
- Chrome orchestration ở Infrastructure; Core giữ abstraction Modern browser WebSocket và Legacy HTTP JSON riêng. Modern dùng `Target.getTargets`/`Target.closeTarget`; Legacy dùng `/json/list` và `/json/close/{encodedTargetId}`.
- Chỉ `ChromeProtocolCapabilityException` cho phép chuyển Modern sang Legacy. Lỗi đóng target, verification còn page, timeout và cancellation không được dùng làm điều kiện fallback.
- Cả hai strategy chỉ đóng `type=page`, xác minh đúng 0 page và không thu thập/log URL. MEMUC route ADB theo cú pháp `memuc -i INDEX adb "COMMAND"`; forward dùng `tcp:0`, luôn có cleanup hữu hạn trong `finally` và không dùng state/port static giữa instance.
- Editor mutation không phụ thuộc `IsExecuting`; scheduler admission vẫn nhận deep snapshot root script và library theo instance nên thay đổi khi active chỉ ảnh hưởng lượt sau.
