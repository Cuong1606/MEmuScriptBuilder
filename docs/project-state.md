# Project State

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
