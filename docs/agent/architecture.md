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
AndroidAdbDevice
IExecutionTarget
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

Runtime targets implement `IExecutionTarget` and carry `DeviceKind`, a provider-qualified `TargetKey`, provider identifier, display name and runnable state. MEmu uses `memu:INDEX`; Android/ADB uses `android-adb:SERIAL`. Scheduler reservations, cancellation and progress correlation use `TargetKey`; the compatibility `Index=-1` on Android is never an execution identity.

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
- Mỗi admission pass MEmu chụp đúng một Tool Help process snapshot, đọc metadata mỗi host/core candidate tối đa một lần rồi resolve toàn bộ target theo batch. Runtime MEmu hiện hành khởi chạy `MEmuHeadless.exe` qua `MEmuSVC.exe`, không bảo đảm quan hệ parent/descendant với host; resolver vì vậy đọc command line hữu hạn bằng Windows `NtQueryInformationProcess` và chỉ map Headless có `--comment` khớp chính xác identity nội bộ lấy từ đúng host `MEmu.exe`. PID cùng creation time của core tìm thấy tại preflight được pin cho cả run. Nếu worker WMI fallback bị tách do resolver deadline hoặc caller cancellation, admission gate được release và fallback đó bị circuit-break cho các pass sau trong process hiện tại; native reader vẫn được thử, tránh một COM worker treo chặn vĩnh viễn mọi launch hoặc tạo thêm worker WMI không giới hạn. Các checkpoint sau admission mở trực tiếp đúng PID, kiểm tra executable `MEmuHeadless.exe` và generation bằng creation time, không enumerate lại process table hoặc resolve lại instance; Headless của instance khác, replacement hoặc PID reuse vì vậy không che mất việc core ban đầu đã chết. Snapshot/command-line/creation-time lỗi, thiếu PID hoặc mapping mơ hồ là `Unknown`, không phải core-dead; mapping chưa pin tại preflight không được nâng terminal thành `Succeeded` bởi kết quả map muộn.

### Android / ADB discovery and targeting

- ADB path is a persisted, configurable setting behind `IAdbPathDiscovery`; discovery checks PATH, SDK platform-tools and the ADB sibling of a resolved MEmu installation without installing software.
- `IAndroidAdbTransportService` runs one `adb devices -l` and reuses `AdbDevicesParser` to return the exact serial/state snapshot without any per-device command. Scheduler admission depends only on this lightweight abstraction. Full UI/editor refresh remains behind `IAndroidAdbDeviceService`, reuses the transport snapshot, then reads metadata only for `device` transports. Unauthorized/offline rows remain unavailable; an optional metadata failure is isolated to that serial, retains the authoritative runnable transport state and exposes a bounded diagnostic.
- Android discovery classifies targets as external, MEmu-backed or unknown. A localhost endpoint is MEmu-backed only when its Windows TCP listener resolves to an allowlisted MEmu executable under a `Microvirt` installation, or an allowlisted product identity property explicitly identifies MEmu/Microvirt. Listener/process/path inspection is read-only and fail-open: missing, inaccessible or ambiguous evidence stays `Unknown` and visible. `adb.exe` ownership by itself, localhost, `emulator-*`, generic model text and metadata failure are not sufficient evidence. A user alias is persisted by exact serial and never changes `TargetKey`, assignment or execution targeting.
- Every Android execution and health command includes `-s SERIAL`. The app never auto-authorizes, restarts the ADB server or falls back to an unscoped ADB command.
- Android screenshot capture uses exact-serial `adb -s SERIAL exec-out screencap -p` through a separate bounded binary-output process contract. PNG stdout is never decoded as text; stderr is drained independently and timeout/cancellation terminate only that screenshot process.
- Android execution supports Delay, Tap, Hold, Swipe, Input Text, Clipboard Paste, Force Stop, Open App and Home/Back/Recent Apps. Hold reuses the normal serial-scoped ADB process route as `input swipe X Y X Y DurationMs`; Clipboard Paste emits `KEYCODE_PASTE` plus optional `KEYCODE_ENTER`; Force Stop emits `am force-stop PACKAGE`. Admission validates regular/composite closures, explicitly rejects Close All Chrome Tabs on Android, and never sends unsupported enabled steps to MEMUC.
- `IAndroidApplicationService` queries launcher activities and optional non-localized label metadata through exact-serial ADB and returns a request-local catalog. Label-query failure preserves the component catalog; unresolved labels remain unknown and never copy the package into friendly-name metadata. `IAndroidForegroundApplicationService` is stateless and read-only: it runs exact-serial `dumpsys activity activities`, accepts only verified resumed markers, then runs exact-serial `dumpsys window` only as fallback and accepts only current-focus/focused-app markers. A non-launcher component becomes a temporary picker candidate, and a foreground Activity never silently collapses to the package's launcher Activity. The Android picker does not persist either discovered catalog. It overlays the existing package-keyed `ApplicationDisplayNames` settings map ahead of reliable Android labels, with a matching current step name as a temporary overlay only when no saved alias exists. Saving/deleting/importing updates settings, the in-memory row/filter and a same-package editor-draft callback without another ADB query; choosing also persists the current name before returning it, while cancel never changes package/activity. Android library transfer is a separate deterministic `.androidappnames` schema (`Provider=AndroidAdb`, package/activity/friendly name); the existing MEmu `.memuappnames` contract is untouched. MainWindow shows the selected friendly name read-only, and command builders ignore that display metadata.
- Android health uses the smallest exact-serial `adb -s SERIAL get-state` check at bounded lifecycle checkpoints. It does not rerun `adb devices -l`, reload model/resolution/DPI metadata or rediscover other Android devices on the execution hot path. MEmu core identity and `MEmuHeadless` probes never apply to Android.

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
- `ApplicationSettings` schema 9 lưu đường dẫn MEMUC/ADB, tên ứng dụng tùy chỉnh, alias Android theo exact serial, launch spacing, policy preflight, provider-qualified target → script mapping, legacy MEmu index mapping, script dùng chung và preference bố cục riêng của Control Center (window size/maximized, tỷ lệ splitter thiết lập–runtime và `RecentListRatio`). Width splitter pixel của schema 6 vẫn được đọc để migrate sau khi Grid có `ActualWidth` usable; save mới chỉ ghi ratio. Hai splitter runtime đều dùng native WPF với Star definitions; persistence chỉ capture Actual size khi đóng và restore Star ratio sau `Loaded`, không can thiệp trong drag/resize. Target-scope/concurrency cùng object bố trí cửa sổ MEmu của schema cũ bị bỏ qua khi load và không được ghi lại. Cấu hình máy cục bộ này không thuộc document kịch bản hoặc `.memuscript`.
- Khi thêm field settings, mọi writer phải bảo toàn field không thuộc trách nhiệm của nó; không được dựng lại object chỉ chứa path hoặc một nhóm setting rồi làm mất cấu hình chạy.

## 8. Execution semantics

- Kịch bản chạy từng bước đúng thứ tự.
- Các bước bị tắt hoặc ghi chú không được thực thi và phải có trạng thái phù hợp.
- Chính sách dừng/tiếp tục khi lỗi là thuộc tính của từng bước.
- `ScriptExecutionEngine` tiếp tục chỉ chạy tuần tự trên đúng một target. Scheduler đa target nằm ở lớp trên và gọi engine độc lập cho mỗi target.
- Scheduler preflight mỗi target qua provider tương ứng: MEmu dùng một `listvms` discovery và một batch core-identity snapshot read-only cho cả admission pass; Android dùng đúng một lightweight transport snapshot `adb devices -l` cho group, match exact serial/state và không đọc model/resolution/DPI/orientation, rồi dùng exact-serial state ở runtime. Scheduler không tự khởi động instance hoặc thay đổi thiết bị và không có hard product target limit; capacity thực tế phụ thuộc CPU/RAM/GPU/USB/ADB/runtime, còn các scale workload trong test chỉ là fixtures hồi quy.
- Mặc định target đang tắt, mất hoặc không hợp lệ được ghi `Unavailable` và bỏ qua. Policy tùy chọn có thể dừng batch trước khi target hợp lệ nào được chạy.
- Mỗi lời gọi scheduler là một launch group độc lập. Target hợp lệ đầu tiên bắt đầu ngay; mỗi target tiếp theo chỉ chờ fixed/random launch spacing của group rồi gọi engine, không chờ target trước hoàn tất. ViewModel điều phối nhiều group và reserve provider-qualified `TargetKey` để chống nhận trùng active/waiting.
- Random spacing lấy mẫu mới cho từng lần khởi chạy sau máy đầu tiên. Delay phải dùng abstraction hỗ trợ `CancellationToken` để unit test không chờ thời gian thật.
- Mỗi launch group có batch token riêng và mỗi instance có token liên kết riêng. Backend giữ group/session isolation, nhưng UI hiện chỉ expose dừng từng instance, các instance đã chọn hoặc toàn bộ; không có action dừng riêng một group. Dừng instance chỉ hủy token instance; Dừng tất cả mới lặp qua mọi session.
- Stop request được ghi nhận đồng bộ trên runtime item trước khi cancel token: row hiển thị `Đang dừng…`, khóa lệnh Stop và giữ reservation đến khi execution/session cleanup terminal. Progress non-terminal đến muộn không được mở lại Stop hoặc xóa feedback; terminal update cuối cùng thay thế trạng thái stop-requested.
- Scheduler tuần tự hóa việc nhận Stop với terminal commit theo từng instance. Stop được nhận trước commit buộc terminal `Cancelled`; terminal đã commit trước làm Stop bị từ chối và UI không phát feedback `Đang dừng…` giả.
- MainWindow close trong khi execution/cleanup còn active phải khóa admission mới, gọi cùng Stop-all cancellation semantics, giữ cửa sổ/application sống và chỉ approve WPF Close sau khi mọi session terminal cùng reservation đã release. Close lặp lại trong lúc resolve/cleanup là idempotent và không được đi xuyên qua WPF `Closing` reentrancy.
- Exception hoặc kết quả thất bại của một engine invocation phải được chuyển thành kết quả riêng và không làm fault/cancel các target khác theo mặc định.
- Health được kiểm tra hữu hạn tại preflight, trước process-backed step, sau Delay/composite Delay và ngay trước terminal `Succeeded`; không polling và không thêm sleep. Core của đúng instance đã xác nhận mất/exited làm target `Unavailable` và ngăn bước mới. `Unknown` được phép tiếp tục để probe sau có thể phục hồi, nhưng `Unknown` tại final boundary thành `Failed` với message xác minh health, không thành false `Succeeded` hay false core-dead.
- Mọi target hợp lệ đã nhận vào group được admission đúng một lần nếu không có cancellation.
- Progress nhiều target luôn mang `LaunchGroupId`, provider-qualified `TargetKey`, provider/identifier và compatibility `InstanceIndex`; UI pump coalesce theo `(LaunchGroupId, TargetKey)` và không dùng chung scalar step status/log cho các target hoặc lần chạy lại.
- Tọa độ lưu trong step được chuyển nguyên vẹn cho từng engine invocation. Coordinate mapper chỉ dùng khi capture; scheduler không scale, clamp hoặc truy vấn resolution để biến đổi tọa độ lúc chạy.
- Execution result phải giữ thời gian bắt đầu/kết thúc, exit code, stdout, stderr và lệnh đã thực thi.
- Contract terminal theo target dùng `Succeeded`, `Failed`, `Unavailable` và `Cancelled`; UI tương ứng hiển thị **Thành công**, **Lỗi**, **Không khả dụng** và **Đã hủy**. `Unavailable` áp dụng khi target không thể admission/preflight hoặc core của đúng instance được xác nhận đã mất trong run, không phải status `Đã bỏ qua` riêng.

## 9. Multi-instance UI state

- `MainViewModel` là state dùng chung duy nhất cho MainWindow và Control Center. `RunControlPanel` có visual tree riêng, được tạo mới cùng mỗi `ControlCenterWindow`; chỉ `DataContext` được chia sẻ, không di chuyển/reuse `UIElement` từ MainWindow hoặc window đã đóng.
- Window manager giữ tối đa một Control Center đang mở, chỉ restore/activate cửa sổ hiện có, bỏ reference khi `Closed`, và tạo window mới sau khi đóng hoặc khởi tạo/`Show` thất bại trước khi window trở thành live. Nếu `Show`/`Activate` ném sau khi HWND đã tồn tại, manager vẫn giữ reference để không tạo duplicate. Lệnh mở chặn exception ở UI boundary, ghi đầy đủ vào `application-error.log` và báo người dùng mà không làm đóng MainWindow. Global `DispatcherUnhandledException` cũng ghi log nhưng giữ `Handled=false` để không âm thầm nuốt lỗi không liên quan. `Application.MainWindow`/shutdown mode và vòng đời singleton của ViewModel/scheduler không đổi.
- `SelectedEditorTarget` là target provider-qualified duy nhất cho preview, app picker và capture, độc lập với selection/assignment của Control Center. `SelectedInstance` chỉ là compatibility focus cho thao tác riêng của MEmu; Android không được giả làm `MemuInstance` và app picker Android luôn dùng serial từ `SelectedEditorTarget`.
- Run target dùng collection ViewModel riêng; checkbox chỉ chọn instance hiện đang chạy, chưa reserved cho thao tác hiện tại, và được bỏ ngay khi target được nhận thành công hoặc refresh cho thấy instance đã tắt.
- Runtime active được nhóm và tra cứu theo `(LaunchGroupId, TargetKey)` ở backend nhưng UI luôn trình bày một DataGrid phẳng; item chỉ giữ trạng thái target, bước cuối có ý nghĩa, message bounded và state cần cho cancellation/tóm tắt. Projection search/filter dùng lại reference item, tháo subscription khi item rời collection. Counter group/aggregate cập nhật theo transition thay vì quét lại toàn bộ collection. Khi scheduler terminal, ViewModel tạo snapshot chỉ chứa scalar, mô tả hữu hạn và tóm tắt bounded, tháo subscription rồi loại group/target khỏi active collections theo thời gian tuyến tính. Snapshot được thêm newest-first vào collection RAM tối đa 20, không giữ full log/execution/task, không persist và có lệnh xóa riêng không tác động active session.
- Refresh discovery được phép khi execution active. Target đang reserved nhưng tạm vắng khỏi kết quả discovery vẫn được giữ trong `RunTargets` với reservation hiện tại; refresh không tạo admission mới, không đổi session/token và loại row đó ngay khi reservation kết thúc nếu snapshot discovery mới nhất vẫn không còn target.
- Callback phải khớp launch group còn active để progress đến muộn không ghi vào lần chạy lại. Registry active theo `TargetKey` chống một target nằm trong hai group active/waiting.
- UI vẫn cho đổi target, dropdown script và chỉnh thư viện khi group chạy; admission snapshot của group không đổi. Editor/persistence phải tự bảo vệ draft và race, không được sửa snapshot đang chạy.
- Mỗi launch group clone toàn bộ library đúng một lần vào `ExecutionScriptLibrarySnapshot` đóng gói, không expose graph nội bộ và không còn liên hệ với model editor. Chế độ một kịch bản và gán riêng đều resolve root script từng target từ snapshot chung đó.
- `MultiInstanceExecutionRequest.ScriptsByTarget` giữ root admission theo provider-qualified `TargetKey` và `ScriptLibrarySnapshot` là nguồn snapshot dùng chung cho group. `ScriptsByInstance` chỉ còn là compatibility input cho MEmu. Ngay trước engine invocation, scheduler materialize chỉ closure root/composite cần thiết thành graph mutable riêng cho target; không target nào dùng chung `ScriptDefinition`, step, composite item hoặc result. Fallback về `Script` giữ tương thích API; update/result vẫn mang script ID và tên để runtime UI không ghép nhầm.

## 10. Input-capture geometry

- `ScreenPoint` và `ScreenRectangle` là model Core dùng chung cho overlay, viewport và screen bounds; chúng không đại diện cho cấu hình bố trí cửa sổ.
- MEmu coordinate capture dùng window handle/PID đã discovery từ `listvms`, đối chiếu handle với process đích và đọc client/child bounds hiện tại bằng Win32 read-only.
- `MemuViewportSelector` loại toolbar/child nhỏ, còn `MemuCoordinateMapper` fit theo guest aspect ratio và ánh xạ screen point sang guest point. Viewport được đọc lại trong phiên capture để theo resize, DPI và letterbox.
- Hook tap/swipe chỉ capture và suppress input theo chính sách hiện hành; subsystem này không gọi API move/resize/focus/restore cửa sổ và không thay đổi execution engine.
- Android coordinate capture decode PNG bằng WPF với native pixel dimensions làm source of truth. Pure uniform-image mapper chuyển DIPs trong actual image rectangle sang `0..width-1`/`0..height-1`, loại letterbox/outside points và tính lại marker khi resize; capture chỉ điền field Tap/Hold/Swipe hiện có và không gửi lệnh input.

## 11. UI composition và step clipboard

- MainWindow chỉ sở hữu editor và summary counts. Control Center là presentation duy nhất cho run/stop, bảng active phẳng và tối đa 20 kết quả gần đây; hai tab cấp cao tách active khỏi recent, tab recent dùng native row splitter giữa list/detail. Backend vẫn định danh launch group nhưng UI history dùng `RunDescription`, không nhấn mạnh group card/label. Tất cả dùng cùng `MainViewModel`, scheduler và session registry.
- `RunTargets` giữ `IExecutionTarget` theo provider-qualified `TargetKey`; checkbox `IsSelected` chỉ chọn mục cho thao tác chạy hoặc gán kịch bản hiện tại.
- Step clipboard nằm trong lifetime của `MainViewModel`, chứa deep-clone snapshot không tham chiếu script nguồn. Paste clone lần nữa để cấp ID mới; Undo entry được ghi vào history của script đích. `MainWindow` chỉ route Ctrl+C/Ctrl+V/Ctrl+Z/Delete tới các `ICommand` editor hiện có khi focus không nằm trong TextBox, PasswordBox hoặc ComboBox editable; selection bước hợp lệ vẫn dùng được khi DataGrid mất focus. Control nhập liệu giữ hành vi clipboard/Undo/Delete native và không có logic mutation danh sách song song trong code-behind.

## 12. Composite scripts

- `ScriptDefinition.Kind` dùng discriminator chuỗi `Regular`/`Composite`; field vắng mặt mặc định `Regular`. Regular chỉ có `Steps`; Composite chỉ có `CompositeItems` polymorphic `scriptReference`/`delay`.
- `ScriptLibraryValidator` là trust boundary dùng cho store, transfer và admission: ID không rỗng/trùng, reference phải tồn tại và trỏ đúng Regular, nested composite bị từ chối.
- `CompositeScriptExecutionEngine` bọc `ScriptExecutionEngine`: child script tiếp tục dùng engine tuần tự hiện có; reference policy chỉ quyết định đi tiếp sau child failure, cancellation luôn dừng. `CompositeExecutionContext` mang composite/item/occurrence/child/step identity nhưng snapshot kết quả gần đây chỉ giữ đường dẫn lỗi gọn.
- ViewModel tạo một `ExecutionScriptLibrarySnapshot` cho toàn library trước scheduler admission và group dùng chung nguồn snapshot đóng gói đó. Scheduler tạo execution graph riêng cho từng target từ snapshot; sửa library sau click không ảnh hưởng execution active và runtime mutation của một target không xuyên sang target khác.
- Clipboard composite và clipboard step là hai buffer khác kiểu. Import/export validate toàn bundle trước mutation; export composite lấy closure child Regular và copy import remap script, step, item cùng reference.

## 13. Chrome CDP và migration bước đã loại bỏ

- `ISpecializedStepExecutor` chỉ route bước đóng tab Chrome; model/selector/execution hiện hành không còn hai bước Android maintenance đã rút. Persistence và transfer tiền xử lý discriminator legacy thành `NoteStep` bị tắt, giữ ID/tên/thứ tự; lần save kế tiếp chỉ ghi discriminator `note`.
- Chrome orchestration ở Infrastructure; Core giữ abstraction Modern browser WebSocket và Legacy HTTP JSON riêng. Modern dùng `Target.getTargets`/`Target.closeTarget`; Legacy dùng `/json/list` và `/json/close/{encodedTargetId}`.
- Chỉ `ChromeProtocolCapabilityException` cho phép chuyển Modern sang Legacy. Lỗi đóng target, verification còn page, timeout và cancellation không được dùng làm điều kiện fallback.
- Cả hai strategy chỉ đóng `type=page`, xác minh đúng 0 page và không thu thập/log URL. MEMUC route ADB theo cú pháp `memuc -i INDEX adb "COMMAND"`; forward dùng `tcp:0`, luôn có cleanup hữu hạn trong `finally` và không dùng state/port static giữa instance.
- Editor mutation không phụ thuộc `IsExecuting`; scheduler admission vẫn nhận deep snapshot root script và library theo instance nên thay đổi khi active chỉ ảnh hưởng lượt sau.
