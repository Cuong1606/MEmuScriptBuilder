# Decision Log

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
- Bối cảnh: `App.OnStartup` là `async void` và phải await khởi tạo ViewModel trước khi tạo cửa sổ. Dùng mặc định `OnLastWindowClose` trong khoảng chưa có cửa sổ làm WPF bắt đầu shutdown; MainWindow có thể hiện ngắn rồi biến mất trong khi process còn headless, không có startup exception.
- Quyết định: `App.xaml` dùng `OnExplicitShutdown` trong startup. Sau khi khởi tạo xong, ứng dụng gán tường minh `Application.MainWindow`, chuyển sang `OnMainWindowClose`, rồi gọi `Show()`.
- Hệ quả: Startup exception vẫn gọi reporter và `Shutdown(-1)`. Sau khi MainWindow được thiết lập, đóng cửa sổ chính tiếp tục kết thúc ứng dụng theo hành vi WPF thông thường; không thêm retry hoặc thay đổi logic khởi tạo khác.

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
