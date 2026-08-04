# Project State

## Checkpoint trước phase tinh gọn sản phẩm — 2026-08-04 14:51, Asia/Saigon

### Mục tiêu hiện tại

- Khóa checkpoint ổn định của toàn bộ worktree hiện tại trước khi bắt đầu phase tinh gọn sản phẩm; session này không sửa production code và chưa bắt đầu phase mới.

### Runtime manual đã xác nhận

- `passed` — người dùng tự mở Release executable — ứng dụng mở được, không tự đóng; khi người dùng đóng ứng dụng thì process thoát.
- `passed` — kiểm tra editor — copy/paste, deep clone, Undo và Ctrl+V hoạt động đúng.
- `passed` — kiểm tra Control Center hiện tại — Run Control, History, sort và paging hoạt động đúng.
- `passed` — kiểm tra với MEmu — ứng dụng không move, resize hoặc delay MEmu.
- Từ checkpoint này, Codex không mở GUI; người dùng tự mở Release executable khi cần runtime smoke test.

### Quyết định đã chốt

- Phase tinh gọn sẽ bỏ Trang và thứ tự, History đầy đủ và toàn bộ window layout/resize/focus/restore production; Control Center tập trung vào Thiết lập chạy, Đang chạy và Kết quả gần nhất.
- Không đặt giới hạn cứng số instance; giữ bước Dán Clipboard Android. Kịch bản gộp chỉ chứa kịch bản thường và bước Chờ, không được chứa kịch bản gộp khác.
- Các bước mới dự kiến: Xóa ứng dụng gần đây, Xóa cache ứng dụng an toàn, Đóng tất cả tab Chrome và giữ một tab mới trống. Chi tiết bền vững được ghi tại D-033.

### File sửa trong session checkpoint

- `docs/project-state.md`
- `docs/decisions.md`

### Verification gần nhất

- `passed` — `dotnet build MEmuScriptStudio.sln -c Release --no-restore` — exit 0 — 0 warning, 0 error.
- `passed` — `dotnet test MEmuScriptStudio.sln -c Release --no-build` — exit 0 — Core 81/81, Infrastructure 210/210; tổng 291/291, 0 failed, 0 skipped.
- `passed` — `git diff --check` — exit 0 — không có whitespace error; chỉ có cảnh báo LF→CRLF.
- `passed` — review Git/untracked — file untracked duy nhất là `tests/MEmuScriptStudio.Infrastructure.Tests/LaunchSmokeScriptTests.cs`, đã có trong ảnh chụp worktree đầu session, phù hợp với thay đổi smoke launcher và được full test suite bao phủ; không có file untracked bất thường.

### Lỗi chưa xử lý

- Không có lỗi runtime mới được báo trong manual test này.

### Blocker

- Không có blocker đã biết cho việc tạo checkpoint.

### Bước tiếp theo

1. Ở yêu cầu tiếp theo, bắt đầu phase tinh gọn sản phẩm; không triển khai phase đó trong session checkpoint này.

## Phase A4 — final automated review passed, runtime pending, 2026-08-04, Asia/Saigon

### Trạng thái

- Đã review toàn bộ diff Phase A so với `HEAD`; không có giới hạn production 30/60. Test phân trang lớn dùng 120 instance và xác nhận 12 trang với page size 10, Previous/Next/direct navigation cùng visible checkbox selection không bị cắt ở 60.
- Một vòng remediation đã xử lý 5 finding Medium: chuyển/drag vào chính trang nguồn giờ disable/no-op thay vì reorder; thêm regression 120 instance; đổi quyết định trang/thứ tự thành D-032 để không trùng D-031; loại acceptance focus/geometry đã hết hiệu lực; làm rõ layout service/geometry chỉ còn legacy ngoài route UI trang/thứ tự hiện tại.
- Review cuối xác nhận không còn finding High/Medium actionable. Copy/paste dùng command, deep clone/ID mới và Undo ở đích; History bulk delete chỉ dùng `IsChecked` và tháo subscription; Run Control chỉ dùng `IsSelected`; page/order chỉ dùng `IsLayoutSelected`/projection visible và không gọi window-layout service.
- Chưa mở ứng dụng, chưa chạy `scripts\launch-smoke.cmd`, MEmu hoặc `memuc.exe`; chưa commit/push. Runtime visual/MEmu vẫn chờ session riêng.

### Verification

- `passed` — targeted Release remediation: 2/2, exit 0, 0 failed, 0 skipped; không warning/error.
- `passed` — code review toàn diff sau remediation: 0 High, 0 Medium actionable.
- `passed` — `dotnet build MEmuScriptStudio.sln -c Release --no-restore`, exit 0, 0 warning, 0 error.
- `passed` — `dotnet test MEmuScriptStudio.sln -c Release --no-build`, exit 0; Core 81/81, Infrastructure 202/202, tổng 283/283, 0 failed, 0 skipped.
- `not run` — executable ứng dụng, visual runtime smoke, MEmu và `memuc.exe` theo giới hạn A4.

## Phase A3.2 — Trang và thứ tự cho 30–60 instance, targeted passed, 2026-08-04, Asia/Saigon

### Trạng thái

- Control Center đổi tab/khu vực thành `Trang và thứ tự`; UI chỉ còn page-size, trang, tìm/lọc, selection, reorder và sort. Đã loại toàn bộ control Arrange, chọn màn hình/cột/khoảng cách, resize/fit, geometry, Focus/Return/Restore khỏi panel; MEmu native chịu trách nhiệm geometry cửa sổ.
- Tập bulk page/order là giao của `IsLayoutSelected`, `VisibleLayoutTargets`, trang quản lý active và target eligible (`IsRunning` + window handle hợp lệ). Highlight không thay checkbox; target ẩn do trang/search hoặc nằm ở trang khác không bị xử lý. Target ineligible bị loại khỏi projection và checkbox stale bị clear.
- Lên/xuống, đầu/cuối trang, đến vị trí, chuyển trang và drag nhóm giữ thứ tự tương đối. Commit cập nhật order, page/slot, current page, projection, selection count, `CanExecute` và persist custom order/current page; Previous/Next chỉ đổi state/persist và không còn gọi Arrange.
- Thêm Chọn tất cả đang hiển thị, Bỏ chọn đang hiển thị, Bỏ chọn tất cả, tổng số tick và số tick đang thấy. `LayoutMovePosition` phát `CanExecuteChanged` ngay khi nhập 0 ↔ số dương.
- Run Control bulk assignment chỉ lấy/xóa `IsSelected`; không dùng hoặc clear `IsLayoutSelected`. Hai danh sách target bật WPF virtualization, content scrolling và recycling; paging, navigation và visible selection được kiểm với 120 instance để khóa việc không giới hạn ở 30/60.
- Quyết định sản phẩm được ghi ở D-032 và đồng bộ vào product spec/design system. Không sửa copy/paste hoặc History; không mở executable ứng dụng, không chạy MEmu/`memuc.exe`, không commit/push.

### Verification

- `passed` — targeted Release QA/retest: 32/32, exit 0, 0 failed, 0 skipped; targeted compilation Core/Infrastructure/App/Infrastructure.Tests không phát warning/error.
- `passed` — regression riêng `LayoutManagement_VisibleSelectionCommandsUpdateCountsAndCanExecute`: 1/1, exit 0; xác nhận vị trí 0 disable và vị trí 1 enable lại command.
- `passed` — code review sau remediation: không còn finding High/Medium actionable. Finding duy nhất về `LayoutMovePosition.CanExecuteChanged` đã sửa và retest.
- `not run` — full build, full test suite, executable ứng dụng, runtime MEmu và `memuc.exe` theo giới hạn nhiệm vụ.

## Phase A3.1 — layout planning, phân trang, sắp xếp và điều hướng, targeted passed, 2026-08-04, Asia/Saigon

### Trạng thái

- Mỗi lần `Xếp lưới` tạo settings snapshot mới từ state UI hiện tại rồi yêu cầu layout service lập plan mới; items/page, columns và plan effective cũ không được truyền lại làm input.
- Effective plan chỉ còn hợp lệ cho đúng cấu hình, tập target và thứ tự vừa áp dụng. Thay đổi cấu hình hoặc số item xóa effective plan và clamp trang; page-size quản lý Auto là state riêng, nhận capacity từ plan mới và được giữ qua pure reorder để chuyển trang sau sort/move không rơi về fallback 12.
- Auto/Custom/All dùng page size hiện hành và page count tương ứng; CustomColumns ảnh hưởng Columns/Rows của plan mới. Trường hợp ba instance ở All tạo đúng một trang.
- Sắp theo tên/index hỗ trợ tăng dần và giảm dần từ UI. Sort tên dùng LINQ stable ordering nên các tên bằng nhau theo comparer giữ thứ tự tương đối; thứ tự thực tế được chuyển thành custom order và persist như trước.
- Previous/Next, đầu/cuối trang, lên/xuống, đến vị trí và chuyển trang đều clamp page hợp lệ. Các phép reorder theo trang chạy trong eligible-order rồi ghép lại vào slot eligible, giữ row instance dừng/không có HWND ở nguyên slot và giữ nhóm vừa trang trên cùng trang đích.
- Policy Phase A không đổi: production settings/service vẫn chuẩn hóa `SizeMode` về `MoveOnly`; không resize và Focus/Return/Restore vẫn bị khóa. Không sửa scheduler, execution, script editor, copy/paste hoặc History; không mở ứng dụng, MEmu hay `memuc.exe`; không commit/push.

### Verification

- `passed` — targeted Infrastructure layout tests: 36/36, exit 0; bao phủ fresh Arrange snapshot, items/page, columns, Auto/Custom/All, three-instance single page, sort name/index hai hướng và stability, Previous/Next, đầu/lùi/tiến/cuối, đến vị trí/trang, clamp sau config/item/sort, mixed eligible/stopped reorder và Phase A move-only.
- `passed` — code review read-only sau hai vòng remediation: không còn finding actionable.
- `not run` — full build, full test suite, runtime app và MEmu smoke test theo giới hạn nhiệm vụ.

## Phase A2.2 — History multi-check và UI fixes, automated targeted passed, 2026-08-04, Asia/Saigon

### Trạng thái

- Mỗi `LaunchGroupItemViewModel` có `IsChecked` độc lập với highlight `SelectedHistoryGroup`. `DeleteSelectedHistoryCommand` lấy snapshot toàn bộ item trong `ExecutionHistory` đã tick, xóa đúng tập đó và không xóa dòng chỉ đang highlight.
- `CanExecute` của lệnh xóa mục chọn cập nhật ngay khi checkbox đổi. Subscription theo dõi checkbox được tháo khi item bị xóa, clear hoặc bị loại do giới hạn 100; detail group/instance/log được clear nếu item đang xem nằm trong tập xóa.
- MainWindow chỉ còn một nút mở Control Center ở toolbar; status bar đáy chỉ còn số liệu. Nhãn và thông báo gán hàng loạt dùng “kịch bản đang chọn”.
- Cột bảng trong launch-group card dùng `Auto`/star sizing thay cho pixel cố định. Các nút thao tác thường được khóa vào style contrast hiện có; nút nguy hiểm tiếp tục dùng `DangerButtonStyle`.
- Không sửa scheduler, launch group, execution, copy/paste, Grid/Focus/layout engine; không mở ứng dụng, không chạy MEmu/`memuc.exe`, không commit/push.

### Verification

- `passed` — targeted Infrastructure tests Phase A2.2: 6/6, exit 0; bao phủ multi-check delete, unchecked/highlight khác checked, clear detail, `CanExecute`, checkbox binding, một nút Control Center, wording, flexible group columns và button styles.
- `passed` — code review read-only Phase A2.2: không có finding actionable.
- `not run` — full build, full test suite, runtime app và MEmu smoke test theo giới hạn nhiệm vụ.

## Phase A2.1 — copy/paste bước qua command/UI, automated targeted passed, 2026-08-04, Asia/Saigon

### Trạng thái

- Clipboard bước tiếp tục sống trên một `MainViewModel`; copy/paste UI và shortcut giờ chỉ đi qua `CopyStepsCommand`/`PasteStepsCommand`, còn helper triển khai là private.
- Binding thật của nút Sao chép/Dán và chọn kịch bản đã được khóa bằng regression: copy nhiều bước ở A, chuyển sang B qua binding, dán khi DataGrid không focus vẫn giữ thứ tự, deep clone/reference độc lập, ID mới và Undo một entry ở B.
- `PasteStepsCommand.CanExecuteChanged` được xác nhận sau copy và sau đổi kịch bản. Ctrl+V tại control nhập văn bản resolve về `None`, nên handler không đánh dấu handled và clipboard văn bản native được giữ nguyên.
- Không sửa Grid/Focus/layout, không mở ứng dụng, không chạy MEmu/`memuc.exe`, không commit/push.

### Verification

- `passed` — targeted Infrastructure tests copy/paste command/UI: 10/10, exit 0.
- `not run` — full build, full test suite, runtime app và MEmu smoke test theo giới hạn nhiệm vụ.

## Phase A1 — khóa an toàn Grid/Focus, automated targeted passed, 2026-08-04, Asia/Saigon

### Trạng thái

- Grid production chuẩn hóa mọi `SizeMode` cũ Auto/Custom thành `MoveOnly`; mặc định settings/ViewModel mới cũng là `MoveOnly`. Arrange chỉ di chuyển outer window và giữ nguyên width/height hiện tại.
- Command `FocusEmulator`, `ReturnToGrid` và `RestoreOriginalLayout` luôn không executable trong Phase A. Control Tự động vừa ô, Khung tối đa, width/height, giữ tỷ lệ, Tập trung, Trở lại lưới và Khôi phục bố cục có `IsEnabled=False` trực tiếp trong XAML.
- Implementation resize/focus/restore cũ vẫn nằm trong layout service để dành cho Phase B, nhưng public service boundary từ chối trước khi probe/đổi bounds; nhánh resize của Arrange không thể vào sau bước chuẩn hóa service.
- Không thêm timer/polling nền; không đổi resolution, DPI, orientation, index MEmu, tọa độ kịch bản hoặc cấu hình Android/MEmu. Không mở ứng dụng, không chạy MEmu/`memuc.exe`, không commit/push.

### Verification

- `passed` — targeted Infrastructure tests cho service/ViewModel/XAML: 20/20, exit 0; bao phủ Auto/Custom → MoveOnly, Arrange không resize, ba command bị khóa và control XAML bị disable.
- `passed` — `git diff --check`, exit 0; không có whitespace error, chỉ có cảnh báo LF→CRLF cho 9 file đã sửa.
- `not run` — full build, full test suite, runtime app và MEmu smoke test theo giới hạn nhiệm vụ.

## UI/UX redesign — automated complete, runtime MEmu pending, 2026-08-03, Asia/Saigon

### Trạng thái

- Baseline là commit `1204c5508a38c11b74757f0c2ef503fadc19439c` trên `main`; worktree sạch trước ba thay đổi tài liệu đã được người dùng duyệt. Toàn bộ thay đổi hiện chưa commit/push.
- MainWindow chỉ còn editor và summary Đang chạy/Chờ/Thất bại; Control Center là nơi duy nhất cho run/stop, launch group, active detail/log, history và layout. Manager/scheduler/MainViewModel vẫn single-instance/single-state.
- Chế độ một script có dropdown `Kịch bản dùng chung`, mặc định script editor và persist ID; mỗi lần chạy clone snapshot đúng dropdown. Chế độ per-instance giữ dropdown từng máy và gán nhóm.
- Cancellation route theo `LaunchGroupId` trực tiếp từ header group. Dừng instance/group/all có token scope riêng; regression hai group × hai instance xác nhận dừng A không hủy B.
- Runtime terminal chuyển khỏi active vào history trong phiên, tên `Nhóm 01…`, giới hạn 100 và có xóa mục chọn/group hoàn tất/toàn bộ. Active list không tăng vô hạn qua rerun.
- Layout management có Trang hiện tại/Toàn bộ, page counts/direct page, search/filter, position/top/end/sort, chuyển và drag group giữa trang giữ relative order; page size chỉ tái chia thứ tự toàn cục.
- Geometry probe đọc outer/DWM/client/child/render; planner fit render viewport và tính outer, apply/focus/restore validate outer/client/render. Focus lưu cả trang, park window khác; diagnostics geometry là opt-in. Không đổi resolution/DPI/orientation/index/script coordinates/Kích thước cố định.
- Clipboard bước là state cấp ứng dụng, có nút rõ trong editor; copy/paste cross-script giữ deep clone/ID mới/order, Undo ở đích và shortcut không chặn TextBox.
- Design resources dùng chung đã merge từ `App.xaml`; các named style chuẩn và light primary dùng chữ tối trên xanh sáng.

### Verification hiện tại

- `passed` — targeted geometry service: 12/12.
- `passed` — targeted common-script/cancellation/history: 9/9.
- `passed` — targeted Control Center/MainWindow binding: 3/3.
- `passed` — toàn fixture `MainViewModelMvpTests` ở phạm vi targeted: 109/109.
- `passed` — full Release build duy nhất: `dotnet build MEmuScriptStudio.sln --no-restore -c Release`, exit 0, 0 warning, 0 error.
- `passed` — full Release test duy nhất: `dotnet test MEmuScriptStudio.sln --no-build --no-restore -c Release`, exit 0; Core 81/81, Infrastructure 168/168, tổng 249/249; 0 failed, 0 skipped.
- `passed` — code review toàn diff phát hiện 1 High và 4 Medium. Một vòng remediation đã đối chiếu PID trước khi restore HWND, cập nhật reference common script sau import overwrite, định tuyến command theo trang filter đang xem, loại instance đã tắt khỏi preview page/slot lưới, và poll outer/client/render đến hai snapshot ổn định.
- `passed` — targeted retest sau remediation: 127/127, 0 failed, 0 skipped. Lần gọi đầu dừng ở compile do test fixture thiếu hai đối số nullable của `MemuInstance`; sửa fixture rồi command thực thi thành công trong cùng vòng remediation.
- `passed` — `git diff --check`, exit 0, không có whitespace error; Git chỉ cảnh báo 24 file sẽ đổi line ending LF sang CRLF.
- `not run` — mở ứng dụng, `scripts/launch-smoke.ps1`, `memuc.exe` và smoke MEmu thật theo yêu cầu. Geometry thực tế chưa được coi là passed cho đến runtime smoke ở bước sau.

## Control Center crash remediation — automated complete, 2026-08-03, Asia/Saigon

### Trạng thái

- Runtime crash lúc khoảng 21:55 được xác định từ Windows `.NET Runtime` event 1026: `InvalidOperationException` do binding mặc định `TwoWay` vào property read-only `MainViewModel.CurrentLayoutPageDisplay` khi Control Center render. `startup-error.log` chỉ chứa lỗi startup cũ, không liên quan.
- Control Center giờ dùng hai `UserControl` visual tree riêng cho Chạy nhiều máy và Bố cục, cùng kế thừa đúng một `MainViewModel`; binding hiển thị read-only được khai báo `Mode=OneWay`.
- Window manager giữ đúng một cửa sổ, lần mở thứ hai restore/activate, bỏ reference khi `Closed` hoặc constructor/`Show` lỗi, rồi cho phép lần mở sau tạo instance mới hợp lệ. Lỗi mở được ghi full exception vào `application-error.log`, hiển thị thông báo trong MainWindow và không thoát process.
- Global `DispatcherUnhandledException` ghi rõ context/stack trace và giữ `Handled=false`, nên không âm thầm nuốt lỗi ngoài failure boundary của lệnh mở.
- Không thay đổi `Application.MainWindow`, shutdown mode, lifetime singleton của `MainViewModel`/scheduler hoặc phiên đang chạy. Không gọi `memuc.exe`, không thao tác MEmu, chưa commit/push.

### Automated verification

- `passed` — targeted Control Center regression: 7/7, exit 0; bao phủ mở lần đầu, activate lần hai, đóng–mở lại, constructor/Show giả lập lỗi và retry, shared state/fresh visual tree, render XAML/resources/bindings và `Mode=OneWay` cho page display.
- `passed` — full Release build duy nhất: `dotnet build MEmuScriptStudio.sln --no-restore -c Release`, exit 0, 0 warning, 0 error.
- `passed` — full Release test duy nhất: `dotnet test MEmuScriptStudio.sln --no-build --no-restore -c Release`, exit 0; Core 80/80, Infrastructure 152/152, tổng 232/232; 0 failed, 0 skipped.
- Code review: 0 High, 1 Medium. Đã sửa finding manager có thể bỏ reference của window vẫn live khi `Show`/`Activate` ném, dẫn tới tạo duplicate.
- `passed` — đúng một targeted retest sau review: 9/9, exit 0; bổ sung lỗi `Show` sau khi window live và lỗi `Activate` trên window live, xác nhận manager không tạo instance thứ hai.
- Runtime launch: chỉ được chạy đúng một lần bằng `scripts/launch-smoke.ps1` sau automated verification; khi `READY` phải dừng để người dùng tự bấm mở Control Center.

## Runtime remediation — dynamic launch groups, aspect-safe grid và Control Center, 2026-08-03, Asia/Saigon

### Trạng thái

- Thay đổi đang ở worktree, chưa commit/push; giữ nguyên toàn bộ window-first startup và `scripts/launch-smoke.ps1` đang có.
- Đã bỏ phạm vi Selected/All cũ và toàn bộ giới hạn concurrency khỏi Core, App và settings. `ApplicationSettings` schema 4 vẫn đọc JSON schema cũ nhờ bỏ qua field lạ; lần save tiếp theo không ghi `TargetScope`/`MaximumConcurrencyMode`/`MaximumConcurrency`.
- Scheduler coi mỗi `Start` là một launch group độc lập: target đầu chạy ngay, delay cố định/ngẫu nhiên chỉ giữa target cùng group, không chờ completion. ViewModel giữ nhiều session, chặn instance active/waiting bị nhận trùng, append runtime item theo group, hỗ trợ chạy mục đã chọn/chạy tất cả còn lại/chạy lại target terminal và giữ cancellation riêng.
- Checkbox được bỏ cho item được nhận sau gán/chạy/di chuyển thành công. Runtime hiển thị số đang chạy, chờ khởi chạy và group; trạng thái terminal không làm mờ toàn dòng và chỉ nút Dừng không còn hợp lệ bị disable.
- Thêm Control Center resizable/maximizable với hai tab Chạy nhiều máy/Bố cục, dùng chung `MainViewModel`; manager activate cửa sổ hiện có và chỉ tạo lại sau khi đóng. MainWindow/editor, `Application.MainWindow` và shutdown mode không đổi.
- Grid Auto/Custom giữ tỷ lệ và căn giữa ô; custom dùng khung tối đa với tùy chọn giữ tỷ lệ mặc định bật. Focus giữ tỷ lệ trong work area và Trở lại lưới phục hồi exact pre-focus bounds. Một trang duy nhất không âm thầm chia trang; read-back không đạt tạo cảnh báo/gợi ý chế độ khác.
- Kéo-thả có tay cầm và insertion indicator. Dòng chưa tick di chuyển riêng; dòng đã tick di chuyển cả nhóm, giữ thứ tự tương đối, persist custom order và bỏ tick sau thành công.
- Không triển khai kịch bản tổng hợp A+B, auto-scale tọa độ, helper APK hoặc auto-start máy ảo. Không gọi `memuc.exe`, không mở ứng dụng và không điều khiển MEmu trong đợt này.

### Automated verification

- `passed` — targeted Core scheduler/grid: 15/15.
- `passed` — targeted MainViewModel hiện tại: 92/92 trước khi bổ sung regression dynamic group; regression riêng dynamic group 2/2.
- `passed` — targeted settings/window service trước regression cuối: 13/13.
- `passed` — full Release build duy nhất: `dotnet build MEmuScriptStudio.sln --no-restore -c Release`, exit 0, 0 warning, 0 error.
- `passed` — full Release test duy nhất: `dotnet test MEmuScriptStudio.sln --no-build --no-restore -c Release`, exit 0; Core 80/80, Infrastructure 145/145, tổng 225/225; 0 failed, 0 skipped.
- Code review cuối: 0 High, 3 Medium. Đã sửa cả 3 trong một vòng: loại target biến mất khỏi session universe, không ghi đè snapshot focus khi focus lặp/chuyển target, và không nhận plan một-trang bị service từ chối là thành công.
- `passed` — targeted retest sau remediation: `MainViewModelMvpTests` và `WindowsMemuWindowLayoutServiceTests`, exit 0; 107/107, 0 failed, 0 skipped. Lần chạy xác nhận dùng `--no-build`; artifact Release đã được build sau toàn bộ source/test remediation.
- `passed` — `git diff --check`, exit 0; không có whitespace error, chỉ có cảnh báo quy ước LF→CRLF.
- `not run` — mở ứng dụng/visual smoke WPF, thao tác trong ứng dụng, mọi lệnh `memuc.exe` và điều khiển MEmu; bị loại khỏi workflow theo yêu cầu hiện tại và không được suy diễn là passed.

## Window-first startup và smoke launcher — automated complete, 2026-08-03, Asia/Saigon

### Trạng thái

- Baseline là checkpoint `f5e938e7049bd4c66a913c6672a1c4c3a1a4568c` (`Implement multi-instance control room and window grid`) trên `main`; worktree sạch trước thay đổi. Thay đổi hiện chưa commit và chưa push theo yêu cầu.
- `App` resolve đúng một `MainWindow`, gán làm `Application.MainWindow`, giữ chuyển đổi `OnExplicitShutdown` → `OnMainWindowClose`, gọi `Show()` đúng một lần và đợi `ContentRendered` đầu tiên trước khi chạy `MainViewModel.InitializeAsync`.
- Cửa sổ hiển thị overlay “Đang khởi tạo…” ngay từ đầu. Workspace và command chưa sẵn sàng bị khóa bằng `CanUseApplication`; khi hoàn tất loading biến mất. Lỗi fatal được ghi startup log và giữ error overlay; lỗi phục hồi được vẫn hiển thị cảnh báo, ghi cùng log rồi cho phép workspace tiếp tục.
- `scripts/launch-smoke.ps1` không build, từ chối mở nếu app đã chạy, gọi `Start-Process` đúng một lần, refresh process ở mỗi poll và chờ tối đa 45 giây. `MainWindowHandle != 0` tạo `READY`; `Responding` và title được in để quan sát nhưng không gây false timeout. Script không kill, restart hoặc chẩn đoán mở rộng.
- `AGENTS.md` bắt buộc mọi smoke launch dùng script này; `READY` phải dừng chờ người dùng, `TIMEOUT` chỉ báo blocker.

### Automated verification

- `passed` — PowerShell parser kiểm tra `scripts/launch-smoke.ps1`, exit 0, không có syntax error; script chưa được thực thi tại thời điểm cập nhật state này.
- `passed` — targeted startup/ViewModel trước review: 99/99, exit 0.
- `passed` — full Release build duy nhất: `dotnet build MEmuScriptStudio.sln --no-restore -c Release`, exit 0, 0 warning, 0 error.
- `passed` — full Release test duy nhất: `dotnet test MEmuScriptStudio.sln --no-build --no-restore -c Release`, exit 0; Core 77/77, Infrastructure 136/136, tổng 213/213, 0 failed, 0 skipped.
- Code review: 0 High, 4 Medium. Đã sửa cả 4: đợi `ContentRendered`, dùng HWND thay vì `Responding` làm READY gate, log lỗi init phục hồi được và kiểm thử host/MainWindow/ShutdownMode bằng abstraction.
- `passed` — targeted remediation retest: 100/100, exit 0.
- `passed` — targeted App Release build sau remediation: `dotnet build src\MEmuScriptStudio.App\MEmuScriptStudio.App.csproj --no-restore -c Release`, exit 0, 0 warning, 0 error. Không chạy lại full solution build/test.
- `not run` tại thời điểm checkpoint tài liệu — runtime launch bằng `scripts/launch-smoke.ps1`; đây là bước cuối và sau `READY` agent phải dừng mọi thao tác tự động.
- `not run` — `memuc.exe`, chạy kịch bản, thao tác trong ứng dụng và điều khiển MEmu; nằm ngoài yêu cầu hiện tại.

## Không gian điều hành đa giả lập — automated complete, 2026-08-03, Asia/Saigon

### Trạng thái

- Baseline là commit `b81b1d197a3196b6175f08aeebdf02e21f64e794` (`Record multi-instance runtime smoke test`) trên `main`; worktree sạch trước khi bắt đầu. Thay đổi hiện chưa commit và chưa push theo yêu cầu.
- Bảng trạng thái runtime dùng chữ/icon trạng thái tương phản cao, không giảm opacity của dòng hoàn tất. Tên kịch bản được hiển thị theo từng giả lập; chỉ nút Dừng của target không còn dừng được bị disable.
- Giữ chế độ một kịch bản hiện tại cho tất cả và thêm chế độ script riêng theo instance. Có gán trực tiếp từng hàng, gán một script cho nhóm đã chọn và gán script hiện tại cho tất cả. Mapping được lưu trong `ApplicationSettings` schema 3.
- ViewModel resolve rồi clone snapshot riêng cho từng target trước phiên chạy. Scheduler dùng `ScriptsByInstance` theo index và mang script ID/tên trong progress/result; concurrency, fixed/random launch spacing, preflight, cancellation, log và trạng thái riêng giữ nguyên.
- Trang Bố cục hỗ trợ chọn nhiều, kéo-thả, mũi tên, nhập vị trí và sắp theo index/tên/tùy chỉnh mà không đổi index thật của MEmu.
- Grid hỗ trợ items-per-page tự động/tùy chỉnh/tất cả; cột tự động/tùy chỉnh; hàng tự tính; kích thước chỉ-di-chuyển/tự động/rộng×cao; khoảng cách; chọn màn hình; trang trước/sau; tập trung; trở lại lưới và khôi phục bố cục ban đầu.
- Planner dùng work area Windows để không che taskbar và không có giới hạn cứng số cửa sổ/cột. Trang ngoài màn hình được đỗ ở tọa độ riêng ngoài toàn bộ work area, không hide/minimize/chồng cùng vị trí nên process/script tiếp tục chạy.
- Auto-fit thử resize cho toàn bộ target, đọc lại bounds, kiểm tra overlap và giảm số cửa sổ hiệu lực mỗi trang khi MEmu không thu nhỏ đủ. Chế độ chỉ di chuyển gửi `SWP_NOSIZE`; resize bị từ chối chỉ tạo cảnh báo tắt “Kích thước cố định”, không sửa setting MEmu.
- Focus giữ đúng window handle, đồng bộ instance focus cho capture và trở lại bằng cách áp lại đúng page/grid. Overlay Chạm/Vuốt/Nhấn giữ tiếp tục tự đọc viewport/bounds hiện tại; không thêm coordinate scaling.
- Bố cục lưu sort/order, page, items-per-page, columns, size, gap, display và bounds gốc theo instance index. Settings writer tiếp tục dùng update load-latest để bảo toàn field độc lập.
- Không triển khai: kịch bản tổng hợp A+B, tự scale tọa độ, helper APK và tự khởi động máy ảo đang tắt.

### Automated verification

- `passed` — targeted Core scheduler/planner: 13/13 test, exit 0; bao phủ script đúng theo instance, tọa độ nguyên trạng, paging/hàng/cột không hard-limit, custom size và move-only.
- `passed` — targeted ViewModel/UI/settings/window service trước review: 100/100 test, exit 0; bao phủ assignment/snapshot, group reorder, focus, persistence schema 3, WPF bindings, fixed-size fallback và parking không overlap.
- `passed` — đúng một vòng targeted retest sau code review: Core scheduler/planner 13/13 và Infrastructure ViewModel/settings/window service 104/104, exit 0. Regression mới bao phủ script gán có step khi script đang mở rỗng, bổ sung baseline cho instance phát hiện muộn, HWND/PID bị tái sử dụng, fixed-size không nhận phóng lớn, vị trí đỗ dùng kích thước read-back và cảnh báo arrange/restore thất bại.
- `passed` — full Release build duy nhất: `dotnet build MEmuScriptStudio.sln --no-restore -c Release`, exit 0, 0 warning, 0 error.
- `passed` — full Release test duy nhất: `dotnet test MEmuScriptStudio.sln --no-build --no-restore -c Release`, exit 0; Core 77/77, Infrastructure 125/125, tổng 202/202; 0 failed, 0 skipped.
- `passed` — `git diff --check`, exit 0; không có whitespace error, chỉ có cảnh báo quy ước LF→CRLF.
- `not run` — mở ứng dụng/visual smoke WPF, thao tác cửa sổ MEmu thật, overlay sau focus/resize và mọi lệnh `memuc.exe`; bị loại khỏi workflow theo yêu cầu hiện tại, không được suy diễn là passed.
- `passed` — một code review toàn diff: 0 High, 7 Medium. Cả 7 Medium đã được sửa trong một vòng: parking dùng bounds thực tế và kiểm tra lỗi, read-back resize/focus hai chiều, đối chiếu HWND/PID, Win32 chạy ngoài dispatcher, điều kiện Run dùng script được gán, baseline bổ sung theo từng instance và restore báo lỗi. Không còn finding High/Medium đã biết sau targeted regression tests.

## Chạy một kịch bản trên nhiều giả lập — complete, 2026-08-03, Asia/Saigon

### Trạng thái

- Đã triển khai bộ lập lịch chạy đa giả lập, UI chọn target/cấu hình, trạng thái và log riêng từng instance, dừng riêng hoặc dừng tất cả.
- Preflight mặc định đánh dấu target tắt, mất hoặc không hợp lệ là `Không khả dụng/Bỏ qua` và tiếp tục target hợp lệ. Tùy chọn dừng toàn bộ khi có target không hợp lệ mặc định tắt. Không có luồng tự khởi động giả lập.
- Máy hợp lệ đầu tiên chạy ngay. Mỗi máy tiếp theo chỉ bắt đầu đếm khoảng cách khởi chạy cố định hoặc ngẫu nhiên mới sau khi có slot trống. Mọi target hợp lệ được đưa vào hàng đợi đúng một lần; lỗi một target mặc định không hủy target khác.
- Cấu hình chạy gần nhất được lưu trong `ApplicationSettings` schema 2, tách khỏi file kịch bản. Các writer settings dùng cập nhật load-latest có tuần tự hóa để không ghi đè cấu hình của nhau.
- Script và cấu hình chạy được chụp snapshot trước khi bắt đầu phiên. Tọa độ được chuyển nguyên trạng cho execution engine, không tự co giãn.
- Baseline trước thay đổi là commit `dfae5a638416d8752aba0826ddb5c3dcb7995caf` (`Complete daily workflow and undo fixes`), branch `main`, worktree sạch. Phần triển khai đã được checkpoint tại commit `11becf1e4115a9d6c17f54eda2c715d1c6556c8e` (`Implement multi-instance execution scheduler`).

### Automated verification

- `passed` — targeted Core scheduler: 9/9 test.
- `passed` — targeted settings persistence/migration: 4/4 test trước review; regression remediation về concurrent settings writer nằm trong targeted retest cuối.
- `passed` — targeted ViewModel regression: 85/85 test.
- `passed` — targeted UI/chạy đa máy theo cụm: 4/4, 6/6 và 3/3 test.
- `passed` — full Release build duy nhất: `dotnet build MEmuScriptStudio.sln --no-restore -c Release`, exit 0, 0 warning, 0 error.
- `passed` — full Release test duy nhất: `dotnet test MEmuScriptStudio.sln --no-build --no-restore -c Release`, exit 0; Core 73/73, Infrastructure 119/119, tổng 192/192; 0 failed, 0 skipped.
- Code review cuối không có finding High; hai finding Medium về snapshot phiên chạy và cạnh tranh giữa settings writer đã được sửa trong một vòng remediation.
- `passed` — targeted retest sau remediation: 5/5 test, exit 0; bao phủ khóa/snapshot phiên chạy trong lúc settings I/O chờ, dừng riêng, lưu cấu hình chạy và cập nhật settings đồng thời.
- `passed` — `git diff --check`, exit 0; không có whitespace error, chỉ có cảnh báo quy ước LF→CRLF.
- `passed` — Release build dùng cho runtime smoke test: exit 0, 0 warning, 0 error; ứng dụng mở thành công và phản hồi bình thường.
- `passed` — người dùng xác nhận runtime smoke test đa giả lập đã Passed.

## Cụm 1–2 runtime fixes và thư viện tên ứng dụng — automated complete, 2026-08-03, Asia/Saigon

### Trạng thái

- `passed` — targeted Cụm 1 sau khi chuyển sang Undo-only: 82/82 test `MainViewModelMvpTests` trên Release.
- `passed` — targeted Cụm 2: 16/16 test `ApplicationName`/`ApplicationPicker` trên Release.
- `passed` — `git diff --check` exit 0, không có whitespace error; chỉ có cảnh báo LF→CRLF.
- `passed` — full Release build exit 0, 0 warning, 0 error.
- `passed` — full Release test: Core 64/64, Infrastructure 113/113, tổng 177/177; 0 failed, 0 skipped.
- `passed` — code review cuối không còn finding High/Medium.
- `passed` — người dùng xác nhận runtime smoke cuối sau thay đổi Undo-only đã Passed.
- Không chạy `memuc.exe` trong workflow này.

### Phạm vi Cụm 1 đã triển khai

- Undo giữ tối đa 50 history entry riêng cho từng kịch bản và chỉ tồn tại trong phiên. Thêm/lưu, bật/tắt, nhân bản nhiều, xóa nhiều, dán nhiều và di chuyển nhóm đều tạo đúng một entry cho mỗi thao tác.
- Ctrl+Z chỉ áp dụng khi focus phù hợp trong DataGrid. Ứng dụng không đăng ký Ctrl+Y hoặc Ctrl+Shift+Z và không chặn Ctrl+Y native trong TextBox.
- Không có Redo stack hoặc command. Sau Undo, thao tác mới được ghi vào history bình thường; thao tác đã hoàn tác không thể được khôi phục qua history của ứng dụng.
- Ctrl+click không còn bị mouse handler chặn; click vùng trống hoặc Esc bỏ toàn bộ selection. Selection chỉ đi qua một luồng ViewModel để tránh cảnh báo draft lặp.
- Toggle Bật đồng bộ editor mà không tạo dirty giả; persistence được tuần tự hóa và save dùng snapshot model giữ nguyên ID.
- Nhân bản bước hỗ trợ toàn bộ tập chọn như một thao tác hàng loạt.

### Phạm vi Cụm 2 đã triển khai

- Dialog Chọn ứng dụng có nút Lưu tên, Xóa tên đã lưu, nhập và xuất thư viện; Ctrl+S chạy đúng thao tác Lưu tên sau khi flush binding.
- Nút Chọn chỉ trả package, Activity và tên đang chọn; không ghi settings ngầm. Chỉ Lưu/Xóa/import thành công mới thay đổi mapping persisted.
- Mapping package → tên dùng settings toàn cục, không phụ thuộc instance và tiếp tục tồn tại sau restart. Xóa override khôi phục label Android gốc hoặc fallback trung thực.
- `.memuappnames` là JSON riêng có format marker và schema version 1; document dùng danh sách entry để phát hiện package trùng trước mutation.
- Import validate toàn file trước khi cập nhật settings; xung đột hỗ trợ Ghi đè, Bỏ qua hoặc Hủy toàn bộ. Hủy là atomic; import thành công chỉ save settings một lần.
- Export luôn lấy toàn bộ mapping toàn cục, kể cả package không xuất hiện trên giả lập đang chọn.

### Git và bàn giao

- Phạm vi bàn giao gồm toàn bộ Đợt 1 cùng Cụm 1–2, startup lifecycle và thay đổi Undo-only; không reset, restore, discard hoặc hoàn tác worktree trong quá trình triển khai.
- Commit bàn giao dùng message `Complete daily workflow and undo fixes` và được push lên `main` theo yêu cầu người dùng.
- Lỗi cửa sổ tự đóng được tái hiện ở cả Debug và Release: không có exception mới trong `startup-error.log`, không có Windows Application Event mới và process còn sống headless. Regression test xác nhận startup dùng `OnLastWindowClose` trong khoảng `await` trước khi có MainWindow.
- Fix giữ `OnExplicitShutdown` trong async startup, sau đó gán `Application.MainWindow` và chuyển sang `OnMainWindowClose` ngay trước `Show()`.
- Final runtime smoke UI sau Undo-only đã được người dùng xác nhận `passed`. Không chạy `memuc.exe`; không suy diễn tích hợp MEmu pass từ smoke UI này.

## Đợt 1 — Hoàn thiện thao tác bước, ứng dụng và trao đổi kịch bản, 2026-08-03, Asia/Saigon

### Trạng thái

- `passed` về automated verification cho toàn bộ phạm vi Đợt 1: bước Nhấn giữ; sao chép/dán và di chuyển nhiều bước; Ctrl+S cùng trạng thái chưa lưu/đã lưu; bước Dán clipboard Android; nhận ứng dụng foreground và tên hiển thị thủ công; xuất/nhập `.memuscript`; nhãn “Ghi chú — không thực thi”.
- Nhấn giữ dùng overlay tọa độ như Chạm và tạo `input swipe X Y X Y DURATION`. Dán clipboard Android tạo `input keyevent 279`; tùy chọn Enter tạo process riêng `input keyevent 66` và chỉ chạy sau khi process dán exit 0.
- Clipboard bước là buffer nội bộ, giữ đúng thứ tự chọn, dùng được qua kịch bản khác và tạo ID mới cho mỗi lần dán. Kéo-thả và nút lên/xuống di chuyển tập chọn như một khối, giữ thứ tự tương đối và selection.
- Ctrl+S flush giá trị TextBox đang focus trước khi lưu. Editor theo dõi dirty bằng version; thay đổi phát sinh trong lúc save vẫn được đánh dấu chưa lưu. Đổi bước/kịch bản hoặc command có thể thay context phải xác nhận trước khi bỏ draft và không mutation nếu người dùng từ chối.
- Foreground app chỉ dùng truy vấn read-only `dumpsys activity activities`, fallback `dumpsys window windows`, luôn giữ đúng instance. Mapping package → tên hiển thị được lưu trong settings và việc đổi đường dẫn `memuc.exe` không ghi đè mapping.
- `.memuscript` là JSON có format marker và schema version. Có xuất kịch bản đang chọn hoặc toàn bộ thư viện; import xử lý trùng bằng tạo bản sao/ghi đè/bỏ qua. Export và import đều scrub giá trị biến `IsSecret`; không đưa log hoặc settings máy cá nhân vào file.
- Nhãn Note đã đổi thành “Ghi chú — không thực thi”; execution semantics vẫn skip và không khởi chạy process.
- Checkpoint trước Đợt 1 đã commit/push trên `main`: `c961d67b93d0fc83869e00372842da5b0adfebe7` (`WIP: checkpoint slices 3-5 before batch 1`). Phần triển khai Đợt 1 hiện chưa commit và chưa push theo yêu cầu.
- `not run` — runtime smoke test WPF cho Ctrl+C/Ctrl+V/Ctrl+S, kéo-thả nhóm, dialog import/export và app picker.
- `not run` — mọi kiểm tra Hold, clipboard Android và foreground application trên MEmu thật. Không chạy `memuc.exe`, không khởi chạy hoặc điều khiển MEmu trong Đợt 1.

### Verification

- Sau từng phần: build solution exit 0, 0 warning/0 error; các test lọc cho từng acceptance đều passed trước khi chuyển phần kế tiếp.
- `passed` — QA cuối `git diff --check` — exit 0 — không có whitespace error; chỉ có cảnh báo line ending LF→CRLF.
- `passed` — QA cuối `dotnet build MEmuScriptStudio.sln --no-restore` — exit 0 — 0 warning, 0 error.
- `passed` — QA cuối `dotnet test MEmuScriptStudio.sln --no-restore` — exit 0 — Core 64/64, Infrastructure 92/92, tổng 156/156 passed, 0 failed, 0 skipped.
- Code review toàn diff: không có finding High. Năm finding Medium qua ba vòng remediation đã được sửa và có regression test: foreground cùng package dùng Activity hiện tại; scrub secret tại import; dirty trong lúc save; xác nhận draft trước mutation/navigation; import all-skip giữ nguyên draft. Re-review cuối xác nhận không còn finding High/Medium.
- Không có blocker automated đã biết. Runtime/MEmu verification vẫn là `not run`, không được suy diễn từ automated tests.

## Slices 3–5 — Overlay tọa độ và Enter sau nhập, 2026-08-03, Asia/Saigon

### Trạng thái

- `passed` về automated verification cho Slice 3: overlay Vuốt dùng marker 8×8, đường và mũi tên hai lớp tương phản cao, nhãn tọa độ gọn và tự đổi phía/clamp trong viewport.
- `passed` về automated verification cho Slice 4: Chạm có overlay click-through hiển thị marker/tọa độ; cho phép chọn lại trước khi Enter xác nhận, Esc hủy. Click chọn và cặp phím xác nhận/hủy được suppress; editor và target vẫn khóa trong phiên capture.
- `passed` về automated verification cho Slice 5: `InputTextStep.PressEnterAfterInput` mặc định `false`, được lưu/đọc/clone và chỉnh bằng checkbox “Nhấn Enter sau khi nhập”. Khi bật, preview hiển thị hai lệnh `memuc.exe` riêng; Enter chỉ chạy sau khi nhập text exit 0. Diagnostics của lệnh nhập vẫn được giữ nếu process Enter timeout, lỗi hoặc bị hủy.
- Slice 2 đã được commit và push tại `3a38a61d9839d4e3c680dfddbbd8c53ee257fd86` với message `Implement multi-select and bulk step deletion`.
- Slices 3–5 đang ở worktree, chưa commit và chưa push theo yêu cầu.
- `not run` — visual/native smoke test overlay trên DPI/resize/letterbox thực tế, thao tác Enter sau nhập trên MEmu và mọi kiểm tra tích hợp MEmu thật.
- Không chạy `memuc.exe`, không khởi chạy hoặc thao tác máy ảo MEmu trong các Slice 3–5.

### Verification

- `passed` — verification riêng Slice 3: build solution exit 0, 0 warning/0 error; test overlay Vuốt 1/1 passed.
- `passed` — verification riêng Slice 4: build solution exit 0, 0 warning/0 error; Core 3/3 và Infrastructure 3/3 test liên quan passed.
- `passed` — verification riêng Slice 5 trước review: build solution exit 0, 0 warning/0 error; Core 13/13 và Infrastructure 4/4 test liên quan passed.
- `passed` — `dotnet restore MEmuScriptStudio.sln` — exit 0 — tất cả project up-to-date.
- `passed` — `dotnet build MEmuScriptStudio.sln --no-restore` sau remediation — exit 0 — 0 warning, 0 error.
- `passed` — `dotnet test MEmuScriptStudio.sln --no-build --no-restore` sau remediation — exit 0 — Core 57/57, Infrastructure 66/66, tổng 123/123 passed.
- Code review toàn diff Slice 3–5: không có finding High; một finding Medium về mất diagnostics của lệnh nhập khi process Enter bị gián đoạn đã được sửa, có regression test và re-review xác nhận đã đóng. Không còn finding High/Medium đã biết.
- `passed` — `git diff --check` — exit 0 — không có whitespace error; cảnh báo LF/CRLF chỉ là quy ước line ending của worktree.

## Slice 2 — Chọn nhiều và xóa nhiều bước, 2026-08-03, Asia/Saigon

### Trạng thái

- `passed` về automated verification: bảng Các bước dùng WPF `SelectionMode="Extended"` và `SelectionUnit="FullRow"`, hỗ trợ semantics Ctrl+nhấp và Shift+nhấp chuẩn của DataGrid.
- `SelectedItems` được đồng bộ vào ViewModel qua `SelectionChanged`; nút Xóa và phím Delete dùng cùng một luồng bulk delete, xác nhận đúng một lần với số bước sắp xóa, chọn dòng hợp lý sau xóa và autosave đúng một lần.
- Khi người dùng từ chối xác nhận, danh sách, selection và persistence không thay đổi. Xóa bị khóa khi script đang chạy hoặc đang lấy tọa độ.
- Kéo-thả reorder chỉ bắt đầu và hoàn tất khi đúng một bước được chọn; tập chọn nhiều không reorder và không autosave. Nút lên/xuống vẫn thao tác trên bước hiện hành như trước.
- `not run` — smoke test thao tác Ctrl/Shift, Delete và drag-drop trên UI WPF thực tế.
- Không chạy `memuc.exe`; Slice 3 chưa bắt đầu.

### Verification

- `passed` — `dotnet restore MEmuScriptStudio.sln` — exit 0 — tất cả project up-to-date.
- `passed` — `dotnet build MEmuScriptStudio.sln --no-restore` — exit 0 — 0 warning, 0 error.
- `passed` — `dotnet test MEmuScriptStudio.sln --no-build --no-restore` — exit 0 — Core 51/51, Infrastructure 62/62, tổng 113/113 passed.
- `passed` — hai regression test WPF/capture chạy lọc riêng — mỗi lệnh exit 0: đồng bộ `SelectedItems`/bỏ primary/bulk delete/next selection 1/1; khóa xóa khi capture 1/1.
- `passed` — `git diff --check` — exit 0 — không có whitespace error; chỉ có cảnh báo LF sẽ được Git đổi sang CRLF ở các file đang sửa.
- Code review phát hiện một finding Medium về thiếu integration test cho cầu nối WPF selection; đã bổ sung test, retest và re-review. Finding đã đóng, không còn finding High/Medium actionable.

## Slice 1 — Tên ứng dụng và fallback trung thực, 2026-08-03, Asia/Saigon

### Trạng thái

- `passed` về automated verification: Slice 1 không còn hiển thị package name như thể đó là tên ứng dụng thật. Label rõ ràng được trim và hiển thị; label null/rỗng/whitespace hiển thị `Chưa xác định`, trong khi package và Activity vẫn ở hai cột riêng và vẫn tìm kiếm được.
- Dialog báo số ứng dụng chưa xác định được tên khi danh sách hỗn hợp, đồng thời có trạng thái riêng cho danh sách đã resolve toàn bộ và danh sách rỗng.
- Enrichment hiện chỉ tin `nonLocalizedLabel` cụ thể. `labelRes` là resource ID, không được tự đoán thành tên; ứng dụng chỉ có label dạng resource có thể tiếp tục hiển thị `Chưa xác định` cho đến khi có cơ chế resolve đáng tin cậy.
- `not run` — runtime smoke test label/fallback trên MEmu thật, theo yêu cầu không chạy `memuc.exe` trong task này. Không tuyên bố Chrome hoặc label dạng resource đã được resolve trên MEmu thật.
- Slice 2–5 chưa bắt đầu.

### Verification

- `passed` — `dotnet restore MEmuScriptStudio.sln` — exit 0 — tất cả project up-to-date.
- Lần build đầu `failed` do process `MEmuScriptStudio.App` PID `22456` từ smoke test cũ giữ khóa DLL; sau khi xác minh đúng executable và đóng riêng process này, build được chạy lại.
- `passed` — `dotnet build MEmuScriptStudio.sln --no-restore` — exit 0 — 0 warning, 0 error.
- `passed` — `dotnet test MEmuScriptStudio.sln --no-build --no-restore` — exit 0 — Core 51/51, Infrastructure 59/59, tổng 110/110 passed.
- `passed` — `git diff --check` — exit 0 — không có whitespace error; chỉ có cảnh báo LF sẽ được Git đổi sang CRLF ở các file đang sửa.
- Code review: không có finding High/Medium actionable trong diff Slice 1.

### Phương án “chọn trực tiếp” — chưa triển khai

- Hướng khả thi nhất không dùng OCR/computer vision là cho người dùng tự mở ứng dụng trên MEmu, sau đó chọn “Nhận ứng dụng đang mở” để truy vấn read-only foreground package/Activity. Cách này xử lý được ứng dụng ngoài trang launcher hoặc nằm trong thư mục vì việc điều hướng do người dùng thực hiện.
- Giới hạn: không giải quyết label dạng resource; có thể bắt nhầm launcher, màn hình hệ thống, activity trung gian hoặc trạng thái multi-window. Cần kiểm tra đúng instance, hiển thị component để người dùng xác nhận và không tự phát sinh thao tác chạm.
- Chưa thêm nút, command hoặc truy vấn mới. Cần người dùng duyệt thiết kế và rủi ro trước khi triển khai.

## Checkpoint bàn giao trước khi đổi API — 2026-08-03, Asia/Saigon

### Trạng thái hiện tại

- Đây là checkpoint bàn giao cho session Codex mới. Chưa triển khai thêm tính năng nào trong danh sách công việc A–E bên dưới và chưa được tuyên bố các tính năng mới hoàn thành.
- Automated verification gần nhất: `passed` — `dotnet restore MEmuScriptStudio.sln` exit 0; `dotnet build MEmuScriptStudio.sln --no-restore` exit 0, 0 warning/0 error; `dotnet test MEmuScriptStudio.sln --no-build --no-restore` exit 0 — Core 49/49, Infrastructure 59/59, tổng 108/108 tests passed.
- Code review gần nhất: năm finding Medium qua hai vòng remediation đã được sửa và retest; re-review cuối không còn finding High/Medium đã biết.
- Runtime smoke test cho nhóm thay đổi mới chỉ thực hiện một phần. Không được ghi trạng thái tổng thể là Passed.

### Runtime đã kiểm tra

- `passed` — build và mở ứng dụng WPF ngày 2026-08-03 — process PID `22456`, `MainWindowHandle=4131126`, `MainWindowTitle=MEmu Script Studio`, `Responding=True`; ứng dụng tạo được cửa sổ chính và không thoát ngay khi startup.
- Các smoke test Giai đoạn 1 cũ đã được người dùng xác nhận trước đó: startup, tự phát hiện/chọn `memuc.exe`, xử lý file sai, lưu đường dẫn, hiển thị instance `MASTER` khi chạy/tắt và bố cục 1280×720.

### Runtime chưa kiểm tra hoặc chưa được xác nhận

- `not run` — lấy application label thật và fallback package-manager trên MEmu thật; cột Tên ứng dụng hiện được người dùng quan sát là chỉ lặp package name.
- `not run` — overlay chọn hai điểm vuốt trên MEmu thật, gồm độ tương phản, suppress click, chọn lại, Enter/Esc, resize, DPI và letterbox.
- `not run` — kéo-thả reorder và các phím tắt Ctrl+C/Ctrl+V/Delete bằng thao tác UI thực tế.
- `not run` — chọn nhiều bước và xóa nhiều; chức năng này chưa được triển khai.
- `not run` — overlay chọn tọa độ cho bước Chạm; chức năng này chưa được triển khai.
- `not run` — tùy chọn Nhấn Enter sau khi nhập; chức năng này chưa được triển khai.
- Không chạy thêm `memuc.exe` hoặc lệnh điều khiển MEmu trong lúc tạo checkpoint này.

### Danh sách công việc cho session mới — chưa triển khai

#### A. Tên ứng dụng trong dialog “Chọn ứng dụng”

- Khảo sát cách lấy application label thật từ Android bằng truy vấn read-only; mục tiêu hiển thị dạng `Chrome | com.android.chrome | com.google.android.apps.chrome.Main`.
- Không tự đoán tên ứng dụng. Nếu không lấy được label đáng tin cậy, hiển thị package và đánh dấu rõ là chưa xác định, không coi package là tên ứng dụng thật.
- Nếu không thể lấy label đáng tin cậy, nghiên cứu phương án “Chọn trực tiếp trên màn hình MEmu”. Thiết kế phải xử lý ứng dụng ngoài màn hình launcher và ứng dụng nằm trong thư mục/nhóm.
- Chưa triển khai chọn trực tiếp trước khi trình bày thiết kế, giới hạn và rủi ro để người dùng duyệt.

#### B. Chọn nhiều bước và xóa nhiều

- Bảng Các bước hỗ trợ Ctrl+nhấp để chọn từng dòng và Shift+nhấp để chọn một dải liên tiếp.
- Nút Xóa và phím Delete xóa toàn bộ bước đang chọn sau một lần xác nhận; thông báo phải nêu rõ số bước sắp bị xóa.
- Autosave sau khi xóa. Không cho xóa khi kịch bản đang chạy hoặc đang lấy tọa độ.
- Kéo-thả chỉ hoạt động khi chọn đúng một bước để tránh thứ tự mơ hồ.

#### C. Overlay chọn đường vuốt

- Làm đường và mũi tên nhìn rõ trên cả nền sáng và tối bằng màu tương phản cao hoặc màu sáng có viền/bóng tối rõ.
- Marker đầu/cuối nhỏ khoảng 6–8 px, có tâm chính xác và không che tọa độ; nhãn tọa độ nhỏ gọn, không che vùng thao tác.
- Giữ chuột trái chọn điểm đầu, chuột phải chọn điểm cuối, Enter xác nhận và Esc hủy.

#### D. Hiển thị tọa độ cho bước “Chạm”

- Khi chọn tọa độ chạm, mở overlay tương tự bước Vuốt; hiển thị marker nhỏ, tương phản cao, có viền/bóng và nhãn X/Y cạnh marker.
- Cho phép chọn lại trước khi xác nhận; Enter xác nhận, Esc hủy.
- Cú nhấp chọn tọa độ phải bị suppress và không được truyền xuống MEmu.

#### E. Nhấn Enter sau khi nhập văn bản

- Bước Nhập văn bản có checkbox `Nhấn Enter sau khi nhập`, mặc định tắt để không thay đổi dữ liệu cũ.
- Khi bật: nhập nội dung trước, chỉ gửi phím Enter sau khi nhập thành công; nếu nhập thất bại thì không gửi Enter.
- Command preview và log phải thể hiện rõ cả thao tác nhập và thao tác Enter.
- Persistence JSON phải lưu/đọc đúng lựa chọn này.

### An toàn và Git

- Không lưu API key, token, settings cục bộ hoặc log runtime vào Git. `.gitignore` đã loại `bin/`, `obj/`, `TestResults/`, `.vs/`, `*.user`, `*.suo`, `*.log`, `logs/`, `settings.json`, các settings local/user và `.env`.
- Bước bàn giao tiếp theo: session mới đọc `AGENTS.md`, checkpoint này và các decision liên quan; khảo sát repository trước khi đề xuất thiết kế/triển khai backlog A–E.

## Checkpoint chỉnh sửa bước — chọn hai điểm vuốt và sửa trực tiếp trong bảng, 2026-08-02

- Ghi vuốt dùng phiên chọn hai điểm: chuột trái chọn hoặc điều chỉnh điểm đầu, chuột phải chọn hoặc điều chỉnh điểm cuối, Enter xác nhận và Esc hủy. Thời gian vuốt vẫn do người dùng nhập và không bị capture ghi đè.
- Overlay topmost, click-through nằm trên viewport Android đã resolve, hiển thị marker đầu/cuối khác nhau, tọa độ guest và đường chỉ hướng. Viewport tiếp tục cập nhật trong phiên để theo resize, DPI và letterbox.
- Native hook suppress cả hai click chọn điểm. Key-down và key-up tương ứng của Enter/Esc đều bị suppress trước teardown; fallback hữu hạn ngăn phiên hook bị treo.
- Checkbox `Bật` sửa model và autosave trực tiếp, không cần `Lưu bước`. Toggle, reorder, clipboard và xóa bị khóa khi đang chạy; kéo-thả cũng bị khóa trong lúc lấy tọa độ.
- Dòng bước hỗ trợ kéo-thả với marker vị trí chèn. Sorting cột bị tắt để index hiển thị luôn trùng thứ tự execution/persistence; các nút mũi tên vẫn được giữ.
- Khi focus nằm trong bảng bước, Ctrl+C sao chép vào clipboard nội bộ, Ctrl+V chèn bản sao có ID mới sau dòng đang chọn, Delete dùng luồng xác nhận xóa hiện có. Focus trong TextBox/ComboBox được loại trừ rõ ràng.
- Dialog chọn ứng dụng hiển thị và tìm kiếm theo tên ứng dụng, package và Activity. `getappinfolist` vẫn chạy trước; launcher component và metadata label tùy chọn dùng truy vấn package manager read-only. Chỉ label rõ ràng mới được nhận; lỗi metadata fallback về package và không làm mất danh sách đã resolve.
- Execution engine không thay đổi. Không chạy lệnh điều khiển MEmu và không thực hiện truy vấn `memuc.exe` mới trong thay đổi này.
- QA cuối: restore/build/test exit 0; build 0 warning/0 error; Core 49/49, Infrastructure 59/59, tổng 108 passed.
- Code review: năm finding Medium qua hai vòng remediation đã được sửa và retest; re-review cuối không còn High/Medium.
- Runtime visual/native smoke test cho overlay, suppress click, DPI/resize/letterbox, kéo-thả bảng và label ứng dụng thật: `not run`.

## Input-assistance checkpoint — app picker và one-shot capture, 2026-08-02

- Khảo sát thật `memuc.exe -i 0 getappinfolist`: exit 0, stdout rỗng, stderr rỗng; không định nghĩa schema không có bằng chứng.
- App picker luôn ưu tiên gọi `getappinfolist`; khi không có component package/activity rõ ràng, fallback sang Android package manager `query-activities` chỉ đọc để resolve launcher Activity. Dialog có tìm kiếm và làm mới.
- OpenApp tự điền package + Activity; ForceStop chỉ điền package. Không mở hoặc dừng ứng dụng khi lấy danh sách.
- `MemuInstance` giữ window handle từ schema `listvms`; capture đối chiếu HWND với PID instance và đọc `wm size` để quy đổi physical screen pixels sang guest pixels.
- Tap/swipe capture là one-shot; low-level hook ghi và suppress chuột nên không inject hoặc truyền tap/swipe vào MEmu. Esc hủy; editor/target bị khóa trong lúc picker/capture.
- Viewport loại child nhỏ/toolbars theo containment và ngưỡng diện tích, fit theo guest aspect ratio và tính lại khi nhận từng mouse event để hỗ trợ resize/DPI/letterbox.
- Hook chạy trên thread riêng, dùng managed quit signal, tháo mouse/keyboard hook trước khi task hoàn tất; lỗi cleanup được surfaced.
- Execution engine không thay đổi.
- QA cuối: restore/build/test exit 0; build 0 warning/0 error; Core 45/45, Infrastructure 51/51, tổng 96 passed.
- Code review/re-review: không còn finding High/Medium đã biết.
- Runtime app picker fallback và coordinate capture trên cửa sổ MEmu thật: `not run`; cần người dùng cho phép và smoke test riêng trước khi tuyên bố verified.

## KeyEvent checkpoint — Ứng dụng gần đây, 2026-08-02

- Bổ sung `AndroidKeyEvent.RecentApps` với command `input keyevent 187` và nhãn `Ứng dụng gần đây`.
- Giữ `AndroidKeyEvent.Menu` tương thích với command `input keyevent 82`, đổi nhãn thành `Menu (phím cũ)`.
- Thứ tự UI: Trang chủ, Quay lại, Ứng dụng gần đây, Menu (phím cũ), Tăng âm lượng, Giảm âm lượng.
- Giữ nguyên numeric value 0–4 của các enum cũ trong JSON; giá trị mới được thêm ở 5. Test persistence xác nhận save/load không mất `RecentApps`.
- Preview và process command dùng cùng mapping; test xác nhận cùng chứa `input keyevent 187`.
- Execution engine không thay đổi; không chạy `memuc.exe`.
- QA: restore/build/test đều exit 0; build 0 warning/0 error; Core 37/37, Infrastructure 43/43, tổng 80 passed.
- Code review: không có finding actionable hoặc High.

## UI checkpoint — trình chỉnh sửa bước theo loại, 2026-08-02

- Panel thuộc tính dùng progressive disclosure: luôn giữ loại/tên/bật bước làm ngữ cảnh; chỉ hiển thị nhóm tham số liên quan đến `ScriptStepKind` đang chọn.
- `Tiếp tục nếu lỗi` và `Thời gian tối đa` chỉ hiển thị cho các bước thực thi process; Delay và Note không hiển thị tùy chọn không có tác dụng.
- Android shell có cảnh báo nguy hiểm; toàn bộ nhãn trong luồng chọn/chỉnh sửa loại bước và xem trước lệnh đã được Việt hóa.
- Execution engine không thay đổi.
- QA cuối: build exit 0, 0 warning/0 error; test exit 0, Core 36/36 và Infrastructure 42/42, tổng 78 passed.
- Code review và re-review: các finding về enum/raw label tiếng Anh đã sửa; không còn finding High/Medium đã biết.
- Chưa mở ứng dụng để visual smoke test thay đổi này và không chạy `memuc.exe`.

## Corrective checkpoint — lỗi startup MVP, 2026-08-02

- Lỗi runtime đã tái hiện foreground: WPF tạo binding mặc định `TwoWay` vào các property read-only, đầu tiên là `MainViewModel.MemucPath`, sau đó là `StepItemViewModel.IsEnabled`; exception phát sinh trong `MainWindow.Show()` và process thoát trước khi có window handle.
- Đã đặt `Mode=OneWay` rõ ràng cho các TextBox read-only và toàn bộ cột DataGrid chỉ hiển thị.
- Đã thêm regression test khởi tạo WPF resources/MainWindow và kiểm tra binding mode.
- Đã thêm startup error boundary: ghi đầy đủ `exception.ToString()` vào `%LocalAppData%\MEmuScriptStudio\logs\startup-error.log`, hiển thị MessageBox dễ hiểu, reporter không throw và shutdown luôn chạy khi startup thất bại.
- QA cuối: restore exit 0; build exit 0 với 0 warning/0 error; test exit 0, Core 36/36 và Infrastructure 24/24, tổng 60 passed.
- Runtime startup verification: PID `13232`, `MainWindowHandle=6686644`, `MainWindowTitle=MEmu Script Studio`, `Responding=True`, `HasExited=False` cả lúc đầu và sau 30 giây.
- `memuc.exe` không được gọi; chưa tiếp tục smoke test chức năng và chưa tuyên bố MVP hoàn thành.

## Checkpoint — MVP vertical slice, 2026-08-02, Asia/Saigon

### Mục tiêu và trạng thái

- Đã triển khai vertical slice chạy được về mặt automated verification: quản lý kịch bản, quản lý bước, execution engine tuần tự trên đúng một instance và giao diện WPF tích hợp.
- Chưa tuyên bố MVP hoàn thành. Runtime smoke test của giao diện và chạy kịch bản trên MEmu thật là `not run`, đang chờ người dùng thực hiện/cho phép.
- Không bắt đầu phần hoàn thiện, mở rộng hoặc chạy song song nhiều instance.

### Phạm vi đã triển khai

- Tạo, đổi tên, nhân bản, xóa có xác nhận và tự động lưu kịch bản cục bộ bằng JSON; lần chạy đầu tạo template `Khởi động lại Chrome`.
- Thêm, sửa, nhân bản, xóa, bật/tắt, di chuyển bước và `continue on error` cho 9 loại bước MVP.
- Chạy tuần tự trên một instance; delay dùng `Task.Delay`; command dùng trực tiếp `memuc.exe` qua process runner, không dùng `cmd.exe` hoặc `&&`.
- Trạng thái bước, command preview, exit code, stdout, stderr, thời gian chạy, timeout và cancellation được đưa vào kết quả/log UI.
- Raw Android shell yêu cầu xác nhận trước khi chạy. Package/activity/input text có validation để structured step không chèn metacharacter shell.
- UI khóa chọn kịch bản/instance trong lúc chạy và bỏ qua progress callback đến muộn từ run cũ.
- Lỗi đọc/ghi script, gồm lỗi lưu template lần đầu, không làm đóng ứng dụng; template vẫn dùng được trong phiên hiện tại.

### Verification gần nhất

- `passed` — `dotnet restore MEmuScriptStudio.sln` — exit 0 — tất cả project up-to-date.
- `passed` — `dotnet build MEmuScriptStudio.sln --no-restore` — exit 0 — 0 warning, 0 error.
- `passed` — `dotnet test MEmuScriptStudio.sln --no-build --no-restore` — exit 0 — Core 36/36, Infrastructure 23/23; tổng 59 passed, 0 failed, 0 skipped.
- `passed` — `code_reviewer` review toàn bộ diff và re-review remediation — 2 High và 3 Medium ban đầu đã sửa; finding Medium cuối về `$NAME` trong structured identifier đã sửa và retest; không còn finding High đã biết.
- `passed` — `git diff --check` trước review — exit 0; cảnh báo LF/CRLF chỉ là quy ước line ending.
- `not run` — mở ứng dụng WPF MVP mới và runtime smoke test trên MEmu thật.
- `not run` — thực thi template Chrome hoặc bất kỳ command MEmu nào trong milestone này.

### Giới hạn và blocker

- Blocker để tuyên bố MVP hoàn thành: cần người dùng smoke test giao diện và execution trên MEmu thật.
- Autosave dùng temp file + replace và tránh JSON dở dang trong lỗi ứng dụng thông thường; chưa xác minh độ bền trước mất điện đột ngột ở mức flush-to-disk/directory metadata.
- Không có blocker automated build/test đã biết.

### Bước tiếp theo

1. Người dùng smoke test trên MEmu thật theo checklist được bàn giao.
2. Nếu có lỗi runtime thực tế, mở corrective cycle chỉ cho lỗi có bằng chứng.
3. Không bắt đầu tính năng hoàn thiện hoặc mở rộng trước yêu cầu mới.

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
