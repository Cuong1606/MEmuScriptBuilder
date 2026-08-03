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
