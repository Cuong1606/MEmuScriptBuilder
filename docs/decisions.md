# Decision Log

## D-031 — Phase A khóa layout ở chế độ chỉ di chuyển

- Ngày: 2026-08-04
- Trạng thái: `accepted`; tạm thời vô hiệu hóa phần resize/focus/restore của D-026, D-028 và D-029 cho đến Phase B.
- Bối cảnh: Smoke test MEmu thật xác nhận thay đổi outer window chưa chứng minh Android render viewport resize đúng. Phase A chỉ được sửa state/UI và không được tiếp tục thử resize, focus hoặc restore.
- Quyết định: Mọi settings kích thước Auto/Custom được chuẩn hóa thành `MoveOnly` khi ViewModel hoặc production layout service sử dụng. Arrange chỉ gửi lệnh di chuyển giữ nguyên width/height. Command và control cho Tự động vừa ô, Khung tối đa, Tập trung, Trở lại lưới và Khôi phục bố cục bị khóa; public service Focus/Return/Restore cũng từ chối trước khi probe hoặc đổi bounds. Implementation geometry cũ được giữ lại cho nghiên cứu Phase B nhưng không có public production route nào gọi được.
- Hệ quả: Phase A không thay đổi resolution, DPI, orientation, index MEmu, tọa độ kịch bản hoặc cấu hình Android/MEmu. Resize/focus/restore chỉ được mở lại sau nghiên cứu và smoke test MEmu thật trong Phase B.

## D-030 — Control Center sở hữu visual tree riêng và có failure boundary

- Ngày: 2026-08-03
- Trạng thái: `accepted`; củng cố D-029 sau runtime crash.
- Bối cảnh: WPF mặc định tạo binding `TwoWay` cho `Run.Text`; binding vào `CurrentLayoutPageDisplay` read-only chỉ phát nổ khi Control Center render, thoát toàn bộ process vì exception dispatcher chưa được chặn tại lệnh mở.
- Quyết định: Hai tab Chạy nhiều máy/Bố cục là hai `UserControl` instance riêng trong mỗi Control Center, cùng kế thừa một `MainViewModel` nhưng không chia sẻ `UIElement`. Mọi binding hiển thị vào property read-only phải khai báo `Mode=OneWay`. Manager activate instance đang mở, bỏ cache khi `Closed` hoặc khi open thất bại trước lúc window thành live, và tạo instance mới sau close; nếu `Show`/`Activate` ném sau khi HWND đã tồn tại thì vẫn giữ reference để không tạo duplicate. UI command chứa lỗi mở, ghi full exception vào `application-error.log` và báo trong MainWindow.
- Hệ quả: Lỗi constructor/`InitializeComponent`/`Show` của secondary window không được làm đóng MainWindow hoặc dispose ViewModel/scheduler. Global `DispatcherUnhandledException` ghi context và stack trace nhưng không đánh dấu handled cho lỗi ngoài boundary; lỗi không liên quan không bị nuốt âm thầm.

## D-027 — Phiên chạy động gồm nhiều launch group độc lập

- Ngày: 2026-08-03
- Trạng thái: `accepted`; thay phần concurrency/slot của D-024 và mở rộng D-025.
- Bối cảnh: Runtime cần nhận nhóm mới trong khi nhóm cũ đang chạy/chờ, nhưng không được chạy trùng cùng instance và không được để callback cũ ghi vào lần chạy lại.
- Quyết định: Mỗi `Start` của scheduler là một launch group có ID, script snapshot và cancellation riêng. Target đầu tiên của từng group chạy ngay; fixed/random delay chỉ nằm giữa các target trong cùng group và không chờ completion. ViewModel giữ registry group/session và reserve instance index active/waiting; runtime item định danh bằng `(LaunchGroupId, InstanceIndex)`, được append thay vì xóa. Không còn model/UI/settings target-scope hoặc concurrency; schema settings tăng lên 4, JSON legacy vẫn load nhưng field cũ bị bỏ khi save.
- Hệ quả: UI có thể nhận “Chạy mục đã chọn” và “Chạy tất cả còn lại” khi group khác hoạt động, giữ số đang chạy/chờ/group, hủy đúng group/instance và cho target terminal chạy lại thành item mới. Script/dropdown có thể đổi sau admission mà không làm đổi snapshot.

## D-028 — Grid và focus luôn dựa trên tỷ lệ cùng bounds read-back

- Ngày: 2026-08-03
- Trạng thái: `accepted`; tinh chỉnh D-026.
- Bối cảnh: Ép riêng width/height làm méo cửa sổ dọc; focus toàn work area cũng làm sai tỷ lệ và việc tái tính grid không đảm bảo trả đúng bounds trước focus.
- Quyết định: Auto fit và focus fit tỷ lệ bounds thực tế vào khung, căn giữa; custom size là khung tối đa với `PreserveAspectRatio` mặc định bật. Service lưu exact pre-focus bounds và read-back khi phục hồi. “Một trang duy nhất” không được tự giảm items/page; nếu read-back cho thấy không vừa/chồng lấn thì trả warning và gợi ý phân trang/custom count.
- Hệ quả: Move-only vẫn chỉ gửi di chuyển. Sai lệch resize/focus không được báo thành công; không thay đổi resolution, DPI, orientation, Android config hoặc setting “Kích thước cố định” của MEmu.

## D-029 — Control Center là secondary window dùng chung state

- Ngày: 2026-08-03
- Trạng thái: `accepted`.
- Bối cảnh: Hai vùng Chạy nhiều máy/Bố cục quá hẹp trong MainWindow nhưng không được tạo scheduler hoặc runtime state thứ hai.
- Quyết định: MainWindow giữ editor và mở một Control Center resizable/maximizable có hai tab điều hành. Window manager chỉ tạo một secondary window tại một thời điểm, activate instance đang có và tạo view mới sau khi đóng; mọi lần đều dùng cùng `MainViewModel`.
- Hệ quả: Đóng Control Center không dừng launch group. `Application.MainWindow` và `ShutdownMode.OnMainWindowClose` không thay đổi.

Tài liệu này lưu các quyết định bền vững đã chốt. Không dùng để ghi log tiến trình; trạng thái hiện tại thuộc [`project-state.md`](project-state.md).

## Quy ước

Mỗi quyết định mới nên ghi ngày, trạng thái, bối cảnh, quyết định và hệ quả. Trạng thái dùng `accepted`, `superseded` hoặc `pending`.

## D-001 — Ứng dụng desktop cục bộ

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Bối cảnh: Sản phẩm cần tạo và chạy kịch bản dành cho MEmu trên máy người dùng.
- Quyết định: Xây dựng Windows desktop app; dữ liệu lưu cục bộ, không có server/cloud, tài khoản hoặc đồng bộ trực tuyến trong phiên bản đầu tiên.
- Hệ quả: Persistence và execution phải hoạt động offline; không gửi dữ liệu ra Internet.

## D-002 — Technology baseline

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Quyết định: Dùng C#, .NET 8, WPF, MVVM, Dependency Injection của .NET và `System.Text.Json`.
- Hệ quả: Không dùng Electron, Python hoặc web server cho phiên bản đầu tiên. Dependency MVVM bên ngoài chỉ được thêm sau khi giải thích và được chấp thuận theo workflow.

## D-003 — Thực thi MEMUC theo bước độc lập

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Bối cảnh: Kịch bản cần trạng thái, timeout, cancellation và log theo từng bước.
- Quyết định: Gọi trực tiếp `memuc.exe` cho từng bước thông thường, không dùng `cmd.exe` hoặc ghép `&&`; delay dùng `Task.Delay`.
- Hệ quả: Command builder và preview phải dùng cùng ngữ nghĩa; process runner phải thu stdout/stderr, exit code và hỗ trợ timeout/cancellation.

## D-004 — Triển khai theo bốn giai đoạn

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Quyết định: Triển khai tuần tự theo nền tảng/MEmu discovery, quản lý kịch bản, execution engine và hoàn thiện sản phẩm.
- Hệ quả: Mỗi giai đoạn phải build và test thành công trước khi chuyển tiếp; không xây toàn bộ ứng dụng trong một thay đổi lớn.

## D-005 — Phân tầng tài liệu Codex

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Bối cảnh: `AGENTS.md` ban đầu chứa cả quy tắc ổn định và đặc tả dài, làm tăng context cho mọi nhiệm vụ.
- Quyết định: Giữ `AGENTS.md` làm guardrail/router; chuyển chi tiết sang `product-spec`, các tài liệu `docs/agent/`, `project-state` và decision log.
- Hệ quả: Agent chỉ nạp tài liệu chuyên biệt theo loại nhiệm vụ. Không đặt hướng dẫn Markdown trong `.codex/rules`; thư mục đó chỉ dành cho execution-policy terminal.

## D-006 — Quyền sửa source code

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Quyết định: Agent chính là agent duy nhất được sửa source code, trừ khi người dùng yêu cầu rõ ràng cách phân công khác.
- Hệ quả: Agent phụ chỉ nghiên cứu, review hoặc đề xuất source patch; agent chính áp dụng và chịu trách nhiệm verification.

## D-007 — Biểu diễn `ScriptStep`

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Bối cảnh: Mỗi loại bước có dữ liệu và quy tắc validation riêng; model cần mở rộng mà không tạo một class chứa nhiều trường không áp dụng.
- Quyết định: Dùng abstract base class `ScriptStep` và derived type cho từng loại bước. Khai báo discriminator ổn định bằng metadata polymorphism của `System.Text.Json`.
- Hệ quả: Logic và validation có thể đặt theo từng derived type, model dễ mở rộng. JSON cần giữ discriminator `$type`; migration phải xử lý khi đổi tên discriminator hoặc type. Giai đoạn 1 chỉ tạo các type nền tảng, Android shell, delay và note; các loại bước MVP còn lại được bổ sung theo giai đoạn.

## D-008 — Yêu cầu khởi tạo tài liệu ban đầu

- Ngày: 2026-08-02
- Trạng thái: `superseded`
- Bối cảnh: `AGENTS.md` cũ có mục hướng dẫn một lần sau khi tạo file: đọc lại file, không tạo source, không cài dependency, không thay đổi hệ thống, báo đường dẫn, tóm tắt yêu cầu và đề xuất Giai đoạn 1 nhưng chờ chỉ thị.
- Quyết định: Mốc khởi tạo đó đã hoàn tất. Các nguyên tắc còn áp dụng được giữ thành quy tắc chung cho nhiệm vụ chỉ-tài-liệu trong `AGENTS.md` và `docs/agent/workflow.md`.
- Hệ quả: Không mất ràng buộc lịch sử, nhưng hướng dẫn một lần không còn làm dài context mặc định.

## D-009 — Custom subagents cấp dự án

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Bối cảnh: Cần tách khảo sát, verification và review khỏi trách nhiệm viết source của agent chính.
- Quyết định: Dùng `project_explorer` và `code_reviewer` với sandbox `read-only`; dùng `qa_verifier` với `workspace-write` chỉ để tạo artifact build/test. Không pin model để các agent kế thừa model của phiên chính.
- Hệ quả: Agent chính vẫn là writer source duy nhất. QA và review chạy theo thứ tự sau implementation, tối đa 3 vòng sửa–kiểm tra; không custom agent nào được tự chạy MEmu thật nếu người dùng chưa cho phép.

## D-010 — MVP được triển khai theo vertical slice tích hợp

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Bối cảnh: Người dùng yêu cầu mốc tiếp theo phải cho phép tạo một kịch bản nhiều bước, chọn một instance và chạy tuần tự ngay trong ứng dụng; phần tạo kịch bản và execution engine không được tách thành hai giai đoạn độc lập.
- Quyết định: Kết hợp phạm vi quản lý kịch bản/bước và execution engine thành các vertical slice nhỏ, mỗi slice build được, nhưng bàn giao chung trong một milestone MVP tích hợp.
- Hệ quả: Quyết định này thay thế thứ tự tách biệt của Giai đoạn 2 và 3 trong D-004 cho milestone hiện tại, không thay đổi công nghệ, ranh giới an toàn hoặc phạm vi bị cấm. MVP chỉ được tuyên bố hoàn thành sau runtime smoke test trên MEmu thật do người dùng cho phép/xác nhận.

## D-011 — Ghi tọa độ one-shot không phải macro recorder

- Ngày: 2026-08-02
- Trạng thái: `superseded`
- Bối cảnh: Người dùng yêu cầu chủ động bấm “Lấy tọa độ” hoặc “Ghi thao tác vuốt” rồi thực hiện đúng một thao tác trên vùng Android để điền tham số bước.
- Quyết định: Cho phép capture one-shot có trạng thái hiển thị, Esc để hủy và suppress input để không thực thi tap/swipe. Không ghi liên tục, không chạy ẩn, không lưu chuỗi sự kiện ngoài một tap hoặc một swipe và không mở rộng thành macro recorder.
- Hệ quả: Tính năng phải dùng đúng instance/window handle, khóa target trong lúc capture, loại khung/toolbar, quy đổi theo guest resolution và dọn toàn bộ hook hữu hạn. Mọi mở rộng sang continuous recording vẫn thuộc phạm vi bị cấm nếu chưa có yêu cầu mới.

## D-012 — Vuốt dùng phiên chọn hai điểm có overlay

- Ngày: 2026-08-02
- Trạng thái: `accepted`
- Bối cảnh: Một thao tác kéo one-shot tự tính thời gian không cho phép điều chỉnh chính xác điểm đầu/cuối và thiếu phản hồi trực quan.
- Quyết định: Bước Vuốt dùng phiên chọn hai điểm giới hạn: chuột trái chọn điểm đầu, chuột phải chọn điểm cuối, có thể chọn lại, Enter xác nhận và Esc hủy. Overlay click-through hiển thị marker, tọa độ và hướng vuốt; thời gian vuốt do người dùng nhập riêng.
- Hệ quả: Cả hai click chọn điểm và cặp phím xác nhận/hủy phải được suppress trước khi tháo hook. Overlay và mapping phải theo viewport Android thực tế khi resize/DPI/letterbox thay đổi. Đây vẫn là input assistance cho đúng một bước, không phải continuous macro recorder.

## D-013 — Chạm dùng phiên chọn có overlay và xác nhận

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Capture Chạm one-shot cũ trả kết quả ngay sau click đầu tiên, không cho người dùng kiểm tra tọa độ hoặc chọn lại trước khi áp dụng vào editor.
- Quyết định: Bước Chạm dùng phiên chọn giới hạn tương tự Vuốt: chuột trái chọn hoặc chọn lại một tọa độ, overlay click-through hiển thị marker và tọa độ guest, Enter xác nhận và Esc hủy.
- Hệ quả: Click chọn và cặp phím Enter/Esc phải được suppress đầy đủ để không truyền thao tác vào MEmu. Phiên tiếp tục khóa target/editor cho đến khi xác nhận hoặc hủy; đây vẫn là input assistance cho một bước, không phải macro recorder.

## D-014 — Bước nhập văn bản có thể gồm hai process tuần tự

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Tùy chọn “Nhấn Enter sau khi nhập” cần gửi text rồi mới gửi Enter, nhưng không được dùng `cmd.exe`, nối `&&` hoặc báo thành công khi thao tác đầu thất bại.
- Quyết định: Một `InputTextStep` có thể tạo chuỗi tối đa hai lệnh `memuc.exe` độc lập: `input text ...` và, khi tùy chọn được bật, `input keyevent KEYCODE_ENTER`. Execution engine chỉ chạy lệnh sau khi lệnh trước exit 0; preview liệt kê cả hai lệnh.
- Hệ quả: Timeout, cancellation, stdout, stderr và lỗi phải được thu theo từng process. Nếu một process lỗi hoặc bị gián đoạn, các process sau không chạy và diagnostics của các process đã hoàn tất vẫn được giữ. Thuộc tính JSON mới mặc định `false` để tương thích dữ liệu cũ.

## D-015 — Clipboard và di chuyển nhiều bước dùng selection có thứ tự

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Người dùng cần thao tác nhiều bước qua Ctrl+C/Ctrl+V, kéo-thả và nút lên/xuống, kể cả giữa các kịch bản.
- Quyết định: Clipboard bước là buffer nội bộ của ứng dụng, lưu snapshot theo thứ tự hiển thị; mỗi lần dán clone lại để tạo ID mới. Tập chọn được di chuyển như một khối và giữ thứ tự tương đối.
- Hệ quả: Không đọc/ghi clipboard Windows. Mutation chỉ autosave một lần và phải khôi phục selection WPF sau reorder.

## D-016 — Editor có dirty state và bảo vệ draft

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Ctrl+S phải lưu cả giá trị TextBox chưa mất focus và không được báo đã lưu khi có thay đổi mới hoặc khi đổi context.
- Quyết định: Ctrl+S flush binding source rồi chạy cùng command Lưu bước. Editor dùng version để chỉ clear dirty cho đúng snapshot đã persist; thao tác đổi bước/kịch bản hoặc mutation làm đổi context phải xác nhận trước khi bỏ draft.
- Hệ quả: Nếu người dùng từ chối, selection và collection không thay đổi. Nếu nội dung đổi trong lúc save, trạng thái vẫn là chưa lưu.

## D-017 — Dán clipboard Android là chuỗi process có điều kiện

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Android hỗ trợ paste clipboard bằng keyevent và có thể cần Enter sau đó, nhưng ứng dụng không được đọc clipboard Windows hoặc nối shell command.
- Quyết định: `AndroidClipboardPasteStep` chạy `input keyevent 279`; khi bật tùy chọn, chạy riêng `input keyevent 66` chỉ sau exit 0 của lệnh dán.
- Hệ quả: Preview liệt kê từng process; lỗi dán ngăn Enter và diagnostics được giữ theo cùng nguyên tắc D-014.

## D-018 — Nhận ứng dụng foreground bằng truy vấn read-only

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Cần chọn ứng dụng đang mở mà không tự thao tác launcher, OCR hoặc helper APK.
- Quyết định: Truy vấn component foreground trên đúng instance bằng `dumpsys activity activities`, fallback `dumpsys window windows`; chỉ parse các dòng có marker foreground đã biết. Tên hiển thị thủ công được lưu cục bộ theo package và ưu tiên hơn label Android.
- Hệ quả: UI luôn hiển thị package/Activity để người dùng xác nhận. Không tự bấm icon, không cài APK và không đưa mapping settings vào export kịch bản.

## D-019 — `.memuscript` là định dạng trao đổi có schema và trust boundary

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Kịch bản cần trao đổi độc lập với thư viện/settings/log cục bộ và xử lý ID trùng rõ ràng.
- Quyết định: Dùng JSON `.memuscript` với format marker và schema version; export selected/all, import validate toàn bộ trước mutation và cho chọn tạo bản sao/ghi đè/bỏ qua theo ID kịch bản.
- Hệ quả: Tạo bản sao sinh ID script và step mới; ghi đè giữ ID nhập. Giá trị biến secret bị scrub ở cả export và import; log, settings máy cá nhân và dữ liệu ngoài script không thuộc document.

## D-020 — Undo/Redo danh sách bước theo phiên và theo kịch bản

- Ngày: 2026-08-03
- Trạng thái: `superseded` bởi D-023
- Bối cảnh: Người dùng cần hoàn tác/làm lại các mutation danh sách bước, gồm thao tác hàng loạt, mà không chiếm Ctrl+Z/Ctrl+Y native của TextBox.
- Quyết định: Mỗi kịch bản có history riêng tối đa 50 entry chỉ trong phiên. Mỗi thao tác thêm/lưu, bật/tắt, nhân bản nhiều, xóa nhiều, dán nhiều hoặc di chuyển nhóm tạo một entry; mutation mới xóa redo stack. Ctrl+Z/Ctrl+Y chỉ dùng history khi focus phù hợp trong DataGrid.
- Hệ quả: History không được persist sau khi đóng ứng dụng. Undo/Redo khôi phục thứ tự, model ID và selection rồi autosave đúng một lần. TextBox tiếp tục dùng native text undo/redo; import ghi đè hoặc xóa kịch bản phải loại history tương ứng.

## D-021 — Thư viện tên ứng dụng là mapping toàn cục có trao đổi riêng

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Tên hiển thị thủ công cần dùng lại giữa mọi giả lập và sau khi restart, nhưng thao tác Chọn ứng dụng không được tự lưu ngầm. Thư viện cũng cần trao đổi độc lập với kịch bản.
- Quyết định: Mapping package → tên nằm trong settings toàn cục. Chỉ nút Lưu tên, Xóa tên đã lưu hoặc import thành công mới persist; Ctrl+S trong dialog tương đương Lưu tên. Nút Chọn chỉ trả package, Activity và tên hiện tại, không tự thay đổi thư viện. Import/export dùng JSON `.memuappnames` với format marker `MEmuScriptStudio.ApplicationNames`, schema version 1 và danh sách package/tên.
- Hệ quả: File được validate toàn bộ trước mutation và package trùng trong file bị từ chối. Xung đột với thư viện hiện tại được xử lý bằng Ghi đè, Bỏ qua hoặc Hủy toàn bộ; Hủy không lưu thay đổi từng phần. `.memuappnames` không chứa instance, Activity, đường dẫn `memuc.exe`, log hoặc dữ liệu kịch bản.

## D-022 — Async startup giữ shutdown tường minh cho đến khi có MainWindow

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: `App.OnStartup` là `async void`. Việc await toàn bộ khởi tạo trước khi tạo cửa sổ khiến process tồn tại với `MainWindowHandle=0` đủ lâu để smoke automation hiểu nhầm là treo, dù cửa sổ cuối cùng vẫn xuất hiện.
- Quyết định: `App.xaml` tiếp tục dùng `OnExplicitShutdown` trong bootstrap. Ứng dụng resolve đúng một MainWindow, gán `Application.MainWindow`, chuyển sang `OnMainWindowClose`, gọi `Show()` đúng một lần, đợi `ContentRendered` đầu tiên rồi mới await `MainViewModel.InitializeAsync`. ViewModel cung cấp loading/readiness/error state để khóa workspace và hiển thị phản hồi trong cửa sổ. Startup exception sau khi cửa sổ đã hiện được ghi log và trình bày trong cửa sổ thay vì shutdown process; lỗi phục hồi được cũng phải ghi startup log trước khi cho phép tiếp tục.
- Hệ quả: Đóng cửa sổ chính vẫn kết thúc ứng dụng theo hành vi WPF hiện có. Smoke launcher dùng sự tồn tại của HWND làm điều kiện `READY`, refresh/in thêm `Responding` và title nhưng không dùng độ bận tạm thời của UI để kết luận treo. Sau `READY` phải dừng; timeout không cho phép tự kill/restart hoặc chẩn đoán mở rộng.

## D-023 — History danh sách bước chỉ hỗ trợ Undo

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Runtime smoke xác nhận Ctrl+Y không hoạt động ổn định và người dùng không cần tính năng Làm lại. Redo vì vậy bị loại khỏi phạm vi thay vì duy trì thêm stack và shortcut riêng.
- Quyết định: Mỗi kịch bản chỉ có Undo history tối đa 50 entry trong phiên. Ctrl+Z hoàn tác dán, xóa, nhân bản, bật/tắt, di chuyển và các mutation bước đang được ghi history; mỗi thao tác hàng loạt vẫn là một entry. Không đăng ký Ctrl+Y hoặc Ctrl+Shift+Z, không có Redo stack/command. Thao tác mới sau Undo được ghi như history bình thường.
- Hệ quả: Trạng thái vừa hoàn tác không thể được làm lại qua ứng dụng. TextBox giữ native text undo/redo và ứng dụng không chặn Ctrl+Y khi focus nằm trong TextBox. Xóa/import ghi đè kịch bản vẫn loại Undo history tương ứng; history không persist sau restart.

## D-024 — Scheduler đa instance bao bọc engine một-instance

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Người dùng cần chạy cùng một kịch bản trên nhiều giả lập với giới hạn đồng thời, fixed/random launch spacing, preflight, log/cancellation riêng và không làm lỗi một instance dừng instance khác.
- Quyết định: Giữ `ScriptExecutionEngine` stateless để chạy tuần tự trên một target; thêm scheduler phía trên. Scheduler preflight bằng `listvms`, bỏ qua target không khả dụng mặc định hoặc dừng batch theo tùy chọn. Target đầu tiên bắt đầu ngay; mỗi target sau phải đợi slot trống rồi mới chờ một fixed/random delay mới. Mỗi target có token liên kết riêng với batch token và progress luôn mang instance index.
- Hệ quả: Trạng thái/log step không còn dùng chung giữa các target. Cấu hình chạy gần nhất nằm trong `ApplicationSettings` schema 2 và không thuộc `.memuscript`; mọi settings writer phải bảo toàn cấu hình này. Không tự khởi động instance và không scale tọa độ khi chạy. Quyết định này mở rộng D-010 cùng các dòng product/architecture cũ nói chưa cần song song, nhưng không thay đổi process safety của D-003.

## D-025 — Gán và snapshot kịch bản theo instance

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Một phiên đa giả lập cần vừa giữ chế độ một kịch bản cho tất cả, vừa cho từng instance chạy một kịch bản khác mà không bị thay đổi selection/editor trong lúc admission chờ.
- Quyết định: UI persist mapping instance index → script ID trong `ApplicationSettings`; trước phiên chạy, ViewModel resolve và clone một snapshot riêng cho từng target. `MultiInstanceExecutionRequest` mang `ScriptsByInstance`; scheduler chọn snapshot theo index và đưa script ID/tên vào progress/result.
- Hệ quả: `ScriptExecutionEngine` vẫn stateless và chỉ chạy một script trên một instance. Concurrency, launch spacing, cancellation và tọa độ nguyên trạng không đổi. Mapping máy cục bộ không thuộc `.memuscript`; script bị xóa làm mapping tương ứng trở thành chưa gán thay vì fallback ngầm.

## D-026 — Grid cửa sổ chỉ dùng bounds Windows

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Cần quan sát nhiều cửa sổ MEmu theo trang, auto-fit và tập trung nhưng không được thay đổi cấu hình giả lập hoặc index thật.
- Quyết định: Tách planner thuần ở Core khỏi Win32 adapter ở Infrastructure. Chỉ đọc màn hình/work area và window bounds, rồi gọi `SetWindowPos`; các lời gọi Win32 chạy ngoài WPF dispatcher. Mỗi HWND được đối chiếu lại với PID đã discovery và mọi move/resize/restore đều read-back hai chiều. Trang ngoài màn hình được đỗ ở các vị trí riêng ngoài mọi work area, không hide/minimize/chồng cùng vị trí. Bố cục gốc lưu theo instance index trong settings schema 3 và được bổ sung khi có instance mới.
- Hệ quả: Chế độ chỉ di chuyển không gửi resize. Auto/custom resize bị MEmu từ chối sẽ giảm items-per-page và cảnh báo người dùng về “Kích thước cố định”, không tự sửa setting. Handle bị tái sử dụng hoặc thao tác/restore không được Windows chấp nhận phải bị bỏ qua và báo cảnh báo. Focus giữ cùng handle nên overlay capture tiếp tục map theo viewport thực tế; không thêm coordinate scaling, helper APK hoặc auto-start instance.

## D-027 — Control Center sở hữu toàn bộ operations state và history trong phiên

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: MainWindow và Control Center cùng hiển thị lệnh chạy, bảng runtime và log, còn các lần chạy terminal bị append vô hạn vào một collection. Chế độ một script cũng không cho chọn script độc lập trong Control Center.
- Quyết định: MainWindow chỉ giữ editor và summary counts. Control Center có ba tab Đang hoạt động/Lịch sử/Bố cục, dùng đúng một `MainViewModel` và scheduler. Chế độ chung dùng `CommonRunScript` persist ID trong settings schema 5 và snapshot lúc click. Group terminal rời active vào history trong phiên giới hạn 100; history không persist và có các lệnh xóa không đụng active group.
- Hệ quả: Chạy lại tạo group/ID mới nhưng bảng active không dài vô hạn. Tên `Nhóm 01…` là presentation; GUID chỉ hiện trong detail. Đóng Control Center không dừng session và manager vẫn single-instance.

## D-028 — Cancellation group được route bằng GroupId tường minh

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Runtime đã xác nhận thao tác “Dừng nhóm đã chọn” từng dừng tất cả; suy group từ selected instance/checkbox tạo coupling với focus và hai visual tree.
- Quyết định: Header từng launch-group card truyền trực tiếp `LaunchGroupId` vào `StopGroupCommand`. Command chỉ lookup đúng `MultiInstanceExecutionSession` và gọi token batch của session đó. Dừng instance dùng token instance; Dừng tất cả mới lặp qua toàn bộ session.
- Hệ quả: Selection/focus không còn tham gia routing cancellation. Regression bắt buộc có ít nhất hai group cùng active và xác nhận dừng A không đổi trạng thái hoặc token của B.

## D-029 — Render viewport là đơn vị chuẩn của grid/focus

- Ngày: 2026-08-03
- Trạng thái: `accepted`, thay thế phần xác nhận geometry của D-026
- Bối cảnh: `GetWindowRect` top-level có thể thay đổi trong khi Android viewport không resize, chrome/toolbar tạo vùng đen và focus/restore chỉ outer bounds không chứng minh thành công.
- Quyết định: Adapter probe outer rect, DWM extended frame, client rect và toàn bộ child window/class/visibility/bounds để chọn render viewport. Planner fit tỷ lệ render vào cell rồi cộng chrome để tính outer. Apply/focus/restore read-back outer/client/render trong tolerance; focus lưu cả trang và park các window khác. Diagnostic geometry là opt-in và ngắn gọn.
- Hệ quả: Outer-only “success” bị xem là resize rejected và nêu khả năng “Kích thước cố định”. Không thay đổi resolution, DPI, orientation, index, tọa độ script hoặc MEmu setting. Kết luận thực tế vẫn cần runtime smoke MEmu.

## D-030 — Thứ tự toàn cục có projection theo trang và clipboard bước cấp ứng dụng

- Ngày: 2026-08-03
- Trạng thái: `accepted`
- Bối cảnh: Một list 60 máy khó quản lý; clipboard bước tồn tại về logic nhưng thiếu routing/focus và affordance khi đổi script.
- Quyết định: `RunTargets` là thứ tự toàn cục; page/current/all/search chỉ là projection. Move/drag nhóm sửa thứ tự toàn cục, giữ relative order rồi tái chia theo page size. Clipboard bước sống theo `MainViewModel`, copy snapshot deep-clone, paste clone ID mới vào script đích và ghi Undo ở đích. Shortcut chỉ route khi grid bước focus; TextBox giữ native clipboard.
- Hệ quả: Đổi page size không đổi index thật hoặc thứ tự toàn cục. Không cần persistence page assignment riêng. Script nguồn không dirty do copy và clipboard dùng lại sau khi chuyển script/dán nhiều lần.

## D-032 — Script Studio chỉ quản lý trang, thứ tự và selection

- Ngày: 2026-08-04
- Trạng thái: `accepted`, thay thế phần điều khiển geometry cửa sổ của D-026 và D-029 trong UI sản phẩm
- Bối cảnh: Runtime cho thấy MEmu native phù hợp hơn để chịu trách nhiệm resize, kích thước, khoảng cách và fit màn hình; trang/thứ tự vẫn cần quản lý ổn định cho 30–60 instance.
- Quyết định: Tab Control Center đổi thành `Trang và thứ tự`. Bulk move lấy đúng giao `IsLayoutSelected`, projection đang hiển thị, trang active và target eligible; selection Run Control tiếp tục chỉ dùng `IsSelected`. Các command trang/thứ tự chỉ sửa state/order/settings, không gọi Arrange, window-layout service, Focus/Restore hoặc Win32 geometry. UI không cung cấp Auto resize, Custom size, Khung tối đa hay Restore geometry.
- Hệ quả: MEmu native sở hữu toàn bộ window resize/fit. Script Studio giữ page-size, current page và custom order persisted, đồng thời cung cấp select-all-visible, clear-visible, clear-all, selection counts và virtualization/recycling. Code/service geometry cũ có thể còn tồn tại để bảo toàn worktree lịch sử nhưng không được route từ UI hoặc command trang/thứ tự.

## D-033 — Tinh gọn sản phẩm quanh thiết lập và kết quả chạy

- Ngày: 2026-08-04
- Trạng thái: `accepted`, thay thế D-026, D-027, D-029 và D-032 trong phạm vi UI/production bị loại; thay thế phần trang/thứ tự của D-030
- Bối cảnh: Manual runtime test đã xác nhận editor, Run Control, History, sort và paging hiện tại hoạt động đúng, đồng thời MEmu không bị move/resize/delay. Trước phase tiếp theo, sản phẩm cần giảm phạm vi vận hành và loại các nhánh không còn phục vụ luồng chính.
- Quyết định:
  - Bỏ toàn bộ `Trang và thứ tự`.
  - Bỏ History đầy đủ; chỉ giữ `Kết quả lần chạy gần nhất`.
  - Bỏ window layout, resize, focus và restore khỏi production.
  - Control Center chỉ tập trung vào `Thiết lập chạy`, `Đang chạy` và `Kết quả gần nhất`.
  - Không đặt giới hạn cứng số instance.
  - Giữ bước `Dán Clipboard Android` theo D-017.
  - Kịch bản gộp chỉ được chứa kịch bản thường và bước `Chờ`; không được chứa một kịch bản gộp khác.
  - Dự kiến bổ sung ba bước: `Xóa ứng dụng gần đây`, `Xóa cache ứng dụng an toàn`, và `Đóng tất cả tab Chrome và giữ một tab mới trống`.
- Hệ quả: Phase tinh gọn phải xóa các route UI/production và state không còn dùng thay vì tiếp tục duy trì như tính năng ẩn. Kết quả gần nhất thay thế mô hình History trong trải nghiệm sản phẩm. Giới hạn đồng thời vẫn là thiết lập điều phối chạy, không phải giới hạn cứng tổng số instance. Thiết kế và tiêu chí chi tiết của kịch bản gộp cùng ba bước dự kiến phải được chốt trong phase triển khai; checkpoint này chưa sửa production code hoặc bắt đầu phase đó.
