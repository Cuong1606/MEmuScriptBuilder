# Audit và kế hoạch redesign UI/UX WPF

Trạng thái tài liệu: `superseded` bởi D-033. Nội dung dưới đây được giữ làm hồ sơ lịch sử của redesign trước giai đoạn tinh gọn; không phải product requirement hoặc kế hoạch triển khai hiện hành. Product spec, architecture, UI design system và D-033 là nguồn chuẩn hiện tại.

## 1. Baseline đã kiểm tra

- Branch: `main`.
- HEAD: `1204c5508a38c11b74757f0c2ef503fadc19439c` — `Stabilize Control Center runtime opening`.
- Worktree sạch trước khi tạo tài liệu: `git status --porcelain=v1` không có output.
- Kiến trúc hiện tại giữ một `MainViewModel` singleton dùng chung cho `MainWindow` và một `ControlCenterWindow`; scheduler, window planner và layout service cũng là singleton qua DI.
- Không mở ứng dụng, không gọi `memuc.exe`, không build/test trong đợt audit.

## 2. Kết luận audit

| Vấn đề | Bằng chứng hiện tại | Kết luận |
| --- | --- | --- |
| Thiếu chọn kịch bản ở “Một kịch bản cho tất cả” | `RunControlPanel.xaml` chỉ hiện ComboBox trong mode gán riêng; `ResolveAssignedScripts` dùng `SelectedScript` của editor | Lỗi UX và coupling state: Control Center phụ thuộc selection ở MainWindow. |
| Chức năng/bảng trạng thái trùng | `MainWindow.xaml` còn hai tab vận hành `Collapsed`, nút Run/Stop và bảng `InstanceRuns`/log; Control Center có bản tương đương | Cần trả MainWindow về editor-only và gom vận hành vào Control Center. |
| “Dừng nhóm đã chọn” dừng tất cả | UI cũ suy group qua `SelectedInstanceRun`, state này bị chia sẻ giữa hai visual tree và tự đổi theo row | Đã thay bằng nút ở header group truyền trực tiếp `LaunchGroupId`; regression 2 group × 2 target đã passed. |
| Bảng execution dài dần | Mỗi run gọi `InstanceRuns.Add`; không có remove/clear/archive | Hành vi append vô hạn là có thật và đang được test như behavior hiện tại. |
| Thiếu quản lý/xóa lịch sử | Không có history model/store/command; result có timestamp nhưng bị bỏ sau khi tạo status message | Cần tách active runtime khỏi persistent history, có retention và xóa rõ ràng. |
| Quản lý thứ tự không phù hợp 60 instance | `WindowLayoutPanel` dùng `ListBox` toàn bộ `RunTargets`, `MaxHeight=220`; reorder global; page chỉ áp lúc arrange | Cần DataGrid ảo hóa, projection theo trang và thao tác bulk/to-position. |
| Grid/focus resize sai Android render | Layout chỉ đọc `GetWindowRect`; input capture mới đọc client/child viewport; verify resize chỉ so outer bounds ±2 px | Nguyên nhân kiến trúc: outer frame được coi là Android viewport. Cần geometry abstraction dùng chung và verify viewport. |
| Màu chữ nút khó đọc | Accent `#146C94`, nhiều primary button hard-code `Foreground="White"`; Button base không có hover/focus/disabled template | Cần token/theme và styled control states theo design system. |
| Không copy/paste bước giữa script | Buffer/clone/paste qua script và test đã tồn tại; UI chỉ có shortcut khi focus trong StepsGrid | Thiếu affordance, không thiếu engine. Thêm command/nút/menu và feedback nguồn/số bước. |

Các symbol chính:

- Shell/views: `MainWindow.xaml(.cs)`, `ControlCenterWindow.xaml(.cs)`, `Views/RunControlPanel.xaml(.cs)`, `Views/WindowLayoutPanel.xaml(.cs)`.
- State: `ViewModels/MainViewModel.cs`, `MainViewModel.Workspace.cs`, `ScriptItemViewModels.cs`.
- Scheduler: `Core/Execution/MultiInstanceExecutionScheduler.cs`.
- Planner/layout: `Core/MEmu/WindowLayoutServices.cs`, `Infrastructure/MEmu/WindowsMemuWindowLayoutService.cs`.
- Viewport capture: `Core/MEmu/AndroidPackageParsers.cs`, `Infrastructure/MEmu/WindowsMemuInputCaptureService.cs`.

## 3. Kiến trúc đích

### 3.1. Root state và workspace

Giữ `MainViewModel` làm root singleton. Đợt này tách collection/command theo workspace trong hai partial class, chưa tạo thêm scheduler, registry hoặc ViewModel root thứ hai:

```text
MainViewModel
├── editor state                         -> MainWindow
├── run/active/history state             -> Control Center
├── layout order/page projection         -> Control Center
└── one scheduler + session registry + window layout service
```

Không tạo scheduler hoặc runtime registry thứ hai. Có thể tách class theo từng phase; nếu chưa tách ngay, public surface và collection vẫn phải đi theo bốn workspace để XAML không tiếp tục bind vào một flat ViewModel khổng lồ.

### 3.2. MainWindow và Control Center

```text
MainWindow — editor
┌──────────────── path / focus instance / open Control Center ────────────────┐
│ Scripts  │ Steps (virtualized)        │ Step inspector + command preview     │
└────────── save/dirty · connection · active groups · last error ─────────────┘

Control Center — operations
┌────────────── Chạy ──────────────┬──────── Bố cục ────────┬── Lịch sử ─────┐
│ mode + script + target selection │ order/page/grid/focus │ filter/delete  │
│ launch groups + instance detail  │ measured viewport     │ group details │
└─────────────────────────────────────────────────────────────────────────────┘
```

- Xóa hẳn hai tab vận hành `Collapsed`, layout drag handlers và bảng runtime/log khỏi MainWindow.
- MainWindow không còn `Chạy mục đã chọn`; giữ entry point `Mở Trung tâm điều khiển`, status summary và nút khẩn cấp `Dừng tất cả` chỉ khi đang chạy.
- Control Center là nơi duy nhất sở hữu run configuration, active group table, layout controls, history và log details. Đóng cửa sổ không dừng group.

### 3.3. Script chạy chung tách khỏi editor selection

- Thêm `CommonRunScriptId`/`CommonRunScript` trong `RunWorkspace`; ComboBox luôn hiện khi mode `Một kịch bản cho tất cả`.
- `SelectedScript` chỉ là kịch bản đang chỉnh trong MainWindow. Đổi script chạy chung không tự đổi editor hoặc làm mất draft.
- `ValidateScriptAssignments`, `ResolveAssignedScripts`, `CanRun` và snapshot dùng `CommonRunScript`, không dùng `SelectedScript` trong one-script mode.
- Header launch group luôn hiển thị script snapshot đã nhận; thay ComboBox sau khi chạy không đổi group cũ.

### 3.4. Launch group và execution history

Thay flat `InstanceRuns` bằng hai nguồn có vòng đời rõ:

- `ActiveLaunchGroups`: collection group-level; mỗi group chứa instance rows. Chỉ chứa queued/waiting/running. Command `StopGroupCommand` nằm trên chính group item và nhận `LaunchGroupId` tường minh.
- `ExecutionHistory`: group terminal chỉ tồn tại trong phiên ứng dụng. Chọn group xem instance/step/log details.
- Khi completion đến, remove group khỏi active rồi chuyển object group sang history ngay; settings I/O chậm không giữ group terminal trong bảng active.
- `StopAllGroupsCommand` là command global duy nhất lặp mọi session. Đổi tên API/UI để không dùng `StopAll()` cho cả nghĩa “toàn session” và “toàn ứng dụng”.
- History actions: xóa mục chọn, xóa theo filter và xóa tất cả; chỉ áp dụng record terminal, đều có xác nhận. Active group không thể bị xóa.
- Không tạo history store/file; không nhét log vào `ApplicationSettings` hay `.memuscript`. Giữ 100 group gần nhất trong memory và không xóa active state.
- Log persistence phải giữ nguyên quy tắc scrub secret; không ghi giá trị biến `IsSecret` hoặc dữ liệu ngoài diagnostics vốn đã được phép.

### 3.5. Quản lý 30–60 giả lập

- Dùng projection `VisibleLayoutTargets` theo trang/filter trên `RunTargets`; ListBox bật virtualization/recycling và chỉ nhận một trang hoặc tập filter, không render cả 60 item mặc định.
- Tạo `LayoutOrderProjection` từ custom order + planner preview. Projection phân biệt:
  - `GlobalPosition`: vị trí bền vững trong toàn bộ 30–60 target;
  - `PageIndex` và `SlotIndex`: kết quả theo cấu hình items-per-page hiện tại;
  - `VisibleManagementPage`: trang đang quản lý, không tự gọi Win32.
- Page size quản lý theo items-per-page của layout; Auto chưa có plan dùng fallback 12. Selection giữ trên item theo instance index khi đổi trang/filter.
- Move up/down/vị trí trong trang vẫn cập nhật global order; thêm chuyển/drag sang trang đích, đầu/cuối trang và sort trang theo tên/index.
- Chỉ `Xếp lưới` mới gọi layout service. Đổi trang/filter quản lý không tự di chuyển cửa sổ.

### 3.6. Geometry đúng cho MEmu

Tạo abstraction dùng chung cho layout và coordinate capture, ví dụ `IMemuWindowGeometryService`, trả `MemuWindowGeometrySnapshot`:

```text
Outer window (GetWindowRect)
┌─ non-client chrome ────────────────────────────────────────────────────────┐
│ Client area (GetClientRect + ClientToScreen)                              │
│ ┌─ toolbar / side controls ─┬─ Android render child / effective viewport ┐│
│ └───────────────────────────┴──────────────────────────────────────────────┘│
└────────────────────────────────────────────────────────────────────────────┘
```

Snapshot bắt buộc phân biệt:

- `OuterBounds`: toàn bộ top-level MEmu window, là rectangle duy nhất `SetWindowPos` nhận.
- `ClientBounds`: vùng client đã đổi sang screen coordinates.
- `RenderChildHandle` và `RenderChildBounds`: child render được chọn sau khi loại toolbar/child nhỏ.
- `AndroidViewport`: phần render thực dùng cho aspect/letterbox và coordinate capture.
- `ChromeInsets`: outer trừ client; `ToolbarInsets`: client trừ effective render host. Không giả định các inset giống nhau giữa instance, DPI hoặc theme.
- Identity: HWND top-level/child, PID và timestamp generation để phát hiện handle tái sử dụng/stale snapshot.

Planner tiếp tục thuần, nhưng input của size planning chuyển sang kích thước viewport/chrome metrics:

1. Chọn kích thước Android viewport tối đa vừa ô và giữ guest aspect ratio.
2. Cộng toolbar/client/non-client inset để suy ra outer bounds cần gửi cho `SetWindowPos`.
3. Clamp theo work area trên **outer bounds**, không trên viewport.
4. Move-only giữ outer size hiện tại và không gửi resize.

Focus dùng cùng phép đổi viewport → outer. `Return to grid` phục hồi exact outer bounds đã chụp, sau đó đọc lại cả outer/client/viewport.

### 3.7. Kích thước cố định và xác nhận resize

Không đọc hoặc thay đổi setting “Kích thước cố định” của MEmu. Chỉ suy ra resize bị giới hạn từ read-back:

1. Trước thao tác, xác nhận top-level HWND vẫn thuộc PID dự kiến và chụp đủ outer/client/render/viewport.
2. Gọi `SetWindowPos` đúng một lần cho attempt hiện tại.
3. Poll read-back có giới hạn ngắn bằng async delay; yêu cầu hai snapshot liên tiếp ổn định, không block UI.
4. Chỉ báo `Succeeded` khi:
   - outer position/size đạt plan trong tolerance hệ thống;
   - client area hợp lệ;
   - render child vẫn đúng identity hoặc được resolve lại hợp lệ;
   - Android viewport đạt kích thước/aspect mục tiêu trong tolerance và không phải toolbar;
   - các viewport của page không overlap theo rectangle dự kiến.
5. `SetWindowPos=true` hoặc outer bounds khớp nhưng viewport không đổi/không đạt phải là `ViewportResizeRejected`, không phải success.
6. Với `AutoFit`, rejection làm giảm items-per-page rồi lập plan lại. Với `Một trang duy nhất`, rollback exact outer bounds và báo không thể xếp. Với focus, giữ state chưa-focus nếu verification fail.

Status UI hiển thị requested outer, actual outer và actual viewport cho target đang chọn; message nêu rõ “khung cửa sổ đã đổi nhưng vùng Android không đổi” khi đúng case.

### 3.8. Step clipboard

- Giữ buffer nội bộ và clone/ID semantics hiện tại.
- Expose `CopyStepsCommand`, `PasteStepsCommand`, `CopiedStepCount` và `CopiedFromScriptName` để XAML bind trực tiếp.
- Thêm `Sao chép`, `Dán (n)` trong toolbar bảng bước và context menu; tooltip ghi `Ctrl+C`/`Ctrl+V`.
- Đổi sang script đích rồi bấm Dán hoạt động ngay cả khi StepsGrid chưa có focus. Nếu có draft chưa lưu, dùng confirmation hiện tại và không mutation khi người dùng từ chối.
- Không dùng Windows clipboard và không thay đổi D-015.

## 4. File cần thay đổi khi triển khai

| Nhóm | File hiện có | File mới dự kiến / trách nhiệm |
| --- | --- | --- |
| Theme | `App.xaml` | `Themes/Colors.Light.xaml`, `Colors.Dark.xaml`, `Typography.xaml`, `Controls.xaml`, `DataGrid.xaml`, `Tabs.xaml` |
| Main shell | `MainWindow.xaml`, `MainWindow.xaml.cs` | Xóa operational duplicate/handlers layout; thêm editor toolbar và compact status |
| Control Center | `ControlCenterWindow.xaml(.cs)` | Thêm tab Lịch sử, responsive grid và shared theme |
| Run UI | `Views/RunControlPanel.xaml(.cs)` | Common script selector, group-level active view, details/log |
| Layout UI | `Views/WindowLayoutPanel.xaml(.cs)` | DataGrid ảo hóa, management paging, page/slot projection, viewport metrics |
| History UI | — | `Views/ExecutionHistoryPanel.xaml(.cs)` |
| Root/state | `MainViewModel.cs`, `MainViewModel.Workspace.cs`, `ScriptItemViewModels.cs` | Workspace state, command clipboard, common script, active group/history |
| Core execution | `Core/Models/ExecutionModels.cs`, `MultiInstanceExecutionModels.cs` | Group/history records; giữ scheduler one-session cancellation isolation |
| Core layout | `Core/Models/WindowLayoutModels.cs`, `Core/MEmu/WindowLayoutServices.cs`, `AndroidPackageParsers.cs` | Geometry snapshot contracts, viewport-aware planning và pure verification rules |
| Infrastructure geometry | `WindowsMemuWindowLayoutService.cs` | Probe outer/client/children/render và validation viewport-aware |
| Persistence | `JsonSettingsStore.cs` | Schema 5 cho common script ID; bảo toàn field hiện có |
| DI | `App.xaml.cs` | Đăng ký geometry/history services và workspace dependencies |
| Tests | Core/Infrastructure test files hiện có | Thêm history store, geometry resolver và WPF design-system tests |

Tên/file mới là đề xuất; phải chốt ở đầu phase triển khai, tránh refactor tên không phục vụ acceptance criteria.

## 5. Luồng UI đích

### 5.1. Chạy một script cho tất cả

1. Mở Control Center → tab Chạy.
2. Chọn `Một kịch bản cho tất cả`.
3. ComboBox `Kịch bản sẽ chạy` xuất hiện ngay dưới mode; chọn script không đổi editor.
4. Chọn target bằng DataGrid/bulk selection, xem summary target + script + spacing.
5. Bấm Chạy; group card mới xuất hiện với snapshot script và instance rows.
6. Group hoàn tất được chuyển khỏi Active sang Lịch sử; active table không dài dần.

### 5.2. Dừng group

1. Chọn group card/header hoặc bấm `Dừng nhóm` ngay trên group.
2. Confirmation nêu mã group và số instance running/waiting.
3. Chỉ session có `LaunchGroupId` đó nhận cancellation; group khác không đổi trạng thái.
4. `Dừng tất cả nhóm` là danger action riêng, nêu tổng group/instance và chỉ xuất hiện ở toolbar global.

### 5.3. Lịch sử

1. Tab Lịch sử mặc định hiển thị group gần nhất, filter theo text/status/date/script.
2. Chọn group để xem instance/step/log; không tải toàn bộ log vào mọi row bảng.
3. Xóa mục chọn/xóa theo filter/xóa tất cả có xác nhận và báo số record.
4. Restart bắt đầu history rỗng; history không persist.

### 5.4. Bố cục 60 instance

1. Cấu hình sort/items-per-page/columns/size/display ở toolbar.
2. DataGrid hiển thị page/slot dự kiến; đổi management page không move window.
3. Chọn nhiều row, move block hoặc nhập global position/page đích; preview cập nhật.
4. Bấm Xếp lưới; UI hiển thị requested/actual viewport và số target accepted/rejected.
5. Focus/Return dùng đúng target identity và viewport-aware verification.

### 5.5. Copy/paste giữa script

1. Chọn một hoặc nhiều bước ở script A; bấm Sao chép hoặc Ctrl+C.
2. Status/toolbar hiển thị `Đã sao chép n bước từ A`.
3. Chọn script B; bấm `Dán (n)` hoặc Ctrl+V.
4. Bước được clone theo thứ tự với ID mới, autosave một lần và tạo một Undo entry.

## 6. Migration

- Nâng `ApplicationSettings.CurrentSchemaVersion` từ 4 lên 5, thêm `CommonRunScriptId` trong `MultiInstanceRunSettings`.
- Khi load schema ≤4 hoặc field null:
  - ưu tiên script editor đang chọn nếu ID còn tồn tại;
  - nếu không, chọn script hợp lệ đầu tiên;
  - nếu thư viện rỗng, để null và disable Run với hướng dẫn rõ.
- Không thay đổi JSON script hoặc `.memuscript`; clipboard không persist.
- Custom layout order, page, display, original placements và assignment per-instance hiện có phải được giữ nguyên qua schema migration.
- History chỉ trong memory nên không có schema/file/migration; restart bắt đầu danh sách rỗng.
- Khi implementation chốt hành vi mới, decision log phải:
  - supersede phần append vô hạn của D-027;
  - refine D-028 để viewport read-back là điều kiện success;
  - củng cố D-029: MainWindow editor-only, Control Center sở hữu vận hành/history.
- Migration phải load legacy JSON có field lạ, save lại schema 5 mà không mất field thuộc settings writer khác.

## 7. Kế hoạch triển khai theo phase

### Phase 0 — Reproduction và contract tests

- Bổ sung case hai group đồng thời, mỗi group ít nhất hai target; dừng group A, xác nhận toàn bộ A cancelled và toàn bộ B tiếp tục/completes.
- Chụp contract hiện tại cho cross-script clipboard, settings schema 4 và layout outer-only để tránh regression ngoài ý muốn.
- Chưa đổi UI trước khi case cancel isolation có bằng chứng.

### Phase 1 — Design tokens và shell ownership

- Tạo resource dictionaries/implicit styles theo design system.
- Redesign MainWindow editor-only; xóa XAML/handler vận hành ẩn và bảng runtime trùng.
- Chuẩn hóa Control Center tab shell; giữ shared `MainViewModel` và single-window manager.
- Visual/WPF tests cho resources, binding mode và ownership control.

### Phase 2 — Run workspace, group model và history

- Thêm common script selection + settings schema 5.
- Tạo group-level active state và cancellation command tường minh.
- Tạo history group/tab, retention và delete trong phiên.
- Chuyển completion từ active sang history; kiểm tra table không tăng vô hạn.

### Phase 3 — Layout list mật độ cao

- Tạo projection page/slot/global position và management paging/filter.
- Bulk reorder/to-position/to-page, persist custom global order.
- Unit tests với 30/60/61 targets và page boundary.

### Phase 4 — Viewport-aware grid/focus

- Tạo geometry probe trong window layout adapter; coordinate capture tiếp tục tự resolve viewport hiện tại.
- Chuyển planner request sang viewport + insets; thêm stable read-back verification/fixed-size result.
- Regression cho focus/return, parking, multi-monitor, DPI metrics và stale HWND/PID.
- Đây là phase có rủi ro runtime cao nhất; không ghép với visual styling nhỏ.

### Phase 5 — Clipboard affordance và polish

- Expose command/nút/context menu, source/count feedback và keyboard parity.
- Hoàn thiện empty/loading/error/focus/disabled/dark-light states.
- Full automated verification, code review, sau đó mới xin runtime smoke test riêng.

Mỗi phase phải build/test xanh trước phase sau theo `docs/agent/workflow.md`; không triển khai toàn bộ trong một diff lớn.

## 8. Automated tests dự kiến

### Core

- Scheduler: cancellation isolation 2×2 target; stop instance không stop group; stop all global được ViewModel fan-out đúng.
- History model: group snapshot giữ timestamps, script/instance/step diagnostics và không chứa secret variable values.
- Planner: 30/60/61 target, auto/custom/all, page/slot/global order, custom columns, no overlap.
- Geometry math: viewport → outer với asymmetric chrome/toolbar; letterbox; landscape/portrait; clamp work area.

### Infrastructure

- Fake platform trả outer/client/multiple child render rectangles; toolbar không được chọn.
- Outer đổi nhưng renderer không đổi → `ViewportResizeRejected`.
- Delayed child settle → chỉ success sau hai stable reads; timeout hữu hạn và cancellation hoạt động.
- Fixed-size, move-only, focus/return, single-page rollback, parking và HWND/PID reuse.
- History retention và delete semantics trong phiên.
- Settings schema 4→5 và concurrent settings writers không làm mất layout/assignment/path.

### App/WPF

- MainWindow không còn run configuration, layout list hoặc full runtime grid; status summary mở Control Center.
- Control Center có selector one-script, Active/Layout/History views và fresh visual tree với shared state.
- Stop group command mang group ID; selection row/log không đổi target cancellation.
- Active collection về 0 sau completion; history tăng theo group, delete cập nhật selection/detail an toàn.
- Layout projection 60 row giữ selection qua page/filter và reorder đúng global position.
- Copy/Paste buttons và shortcuts gọi cùng command; qua script giữ thứ tự, ID mới, một autosave/Undo entry.
- Mọi control family nhận implicit/named style; read-only binding vẫn `Mode=OneWay`; keyboard focus có `FocusVisualStyle`.

## 9. Runtime acceptance criteria

Các mục này chỉ được đánh dấu `passed` sau khi build/test xanh và người dùng cho phép smoke test bằng `scripts/launch-smoke.ps1`; các mục MEmu cần MEmu thật.

### UI/UX

1. Ở 1280×720, 1920×1080 và Windows scaling 100/125/150%, MainWindow không bị cắt và chỉ chứa editor; Control Center resize/maximize được.
2. Light/dark đều đọc rõ. Primary/secondary/danger/disabled/hover/selected/focus khác nhau; không có chữ trắng khó đọc trên xanh trung bình và không còn control WPF mặc định chưa style.
3. Keyboard-only đi được theo thứ tự nhìn, focus ring rõ, shortcut và nút có cùng behavior.

### Run/history

4. One-script mode chọn script ngay trong Control Center; chạy đúng snapshot đó trên mọi target dù editor/selector đổi sau admission.
5. Hai group, mỗi group nhiều target: dừng selected group chỉ hủy group đó; group còn lại tiếp tục. Dừng tất cả mới hủy mọi group.
6. Chạy lặp 20 group không làm active table dài thêm sau completion; 20 group xuất hiện trong history theo retention.
7. Xóa một/xóa group hoàn tất/xóa tất cả history đúng số record, có xác nhận, không xóa active group; restart bắt đầu history rỗng.

### 30–60 instance

8. Với 60 target, search/filter/page theo page size, chọn nhiều, move block, nhập vị trí trong trang và chuyển page phản hồi được, selection không mất sai và không cần cuộn một danh sách 60 hàng.
9. Trang/ô preview khớp plan áp dụng; đổi management page không tự move cửa sổ.

### Grid/focus trên MEmu thật

10. Khi “Kích thước cố định” tắt, sau grid/focus cả outer, client và Android viewport read-back đạt target; overlay tap/swipe bám đúng viewport sau resize.
11. Toolbar/chrome không bị nhận nhầm là Android viewport; test cả cửa sổ portrait, landscape, letterbox và ít nhất hai mức DPI.
12. Khi “Kích thước cố định” bật hoặc renderer không đổi, UI báo resize bị từ chối; không báo đã xếp/tập trung thành công. Move-only vẫn di chuyển mà không gửi resize.
13. Focus rồi Return phục hồi đúng outer bounds/page/slot; capture tiếp tục dùng cùng instance/window identity.

### Clipboard

14. Chọn nhiều bước ở script A, copy bằng nút hoặc Ctrl+C, đổi sang B và paste bằng nút hoặc Ctrl+V: đúng thứ tự, ID mới, autosave một lần, Undo một lần; draft confirmation không bị bỏ qua.

## 10. Ngoài phạm vi kế hoạch

- Không thay đổi resolution/DPI/orientation/settings Android/MEmu.
- Không tự tắt “Kích thước cố định”.
- Không auto-start instance, không helper APK, không OCR/computer vision, không scale tọa độ lúc execution.
- Không đổi WPF/.NET 8/MVVM/DI hoặc thêm dependency UI nếu chưa giải thích và được duyệt.
