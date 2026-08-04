# Agent Workflow

## 1. Source ownership

Agent chính là agent duy nhất được sửa source code, trừ khi người dùng yêu cầu rõ ràng cách phân công khác. Agent phụ chỉ được nghiên cứu, rà soát, chạy kiểm tra không làm thay đổi source hoặc đề xuất patch; agent chính chịu trách nhiệm áp dụng và kiểm tra thay đổi source.

## 2. Quy trình cho mỗi yêu cầu phát triển

1. Đọc toàn bộ `AGENTS.md`.
2. Đọc [`../project-state.md`](../project-state.md) khi tiếp tục công việc, rồi nạp đúng tài liệu được định tuyến trong `AGENTS.md`.
3. Kiểm tra cấu trúc repository và trạng thái code hiện tại; không tự suy đoán tính năng đã tồn tại.
4. Với nhiệm vụ lớn hoặc chưa rõ, dùng `project_explorer` để lập bản đồ entry point, luồng thực thi, dependency, file/symbol liên quan và rủi ro. Agent này chỉ đọc và không đề xuất triển khai.
5. Xác định phạm vi, tiêu chí chấp nhận và rủi ro. Trước thay đổi lớn, trình bày kế hoạch ngắn.
6. Agent chính tự sửa phần liên quan; không xóa chức năng đang hoạt động và không đổi công nghệ chính khi chưa được chấp thuận.
7. Sau khi viết code, dùng `qa_verifier` để restore, build, test và kiểm tra acceptance criteria với bằng chứng lệnh/exit code/kết quả.
8. Sau QA, dùng `code_reviewer` để review diff, ưu tiên correctness, regression, security, async/threading, process, cancellation, timeout, MEMUC và thiếu test.
9. Agent chính xác nhận finding, sửa lỗi hợp lệ, rồi yêu cầu `qa_verifier` chạy lại test bị ảnh hưởng.
10. Tối đa 3 vòng sửa–kiểm tra cho cùng một vấn đề. Nếu không tiến triển, dừng và báo blocker.
11. Xem lại diff, kiểm tra không có thay đổi ngoài phạm vi và chỉ kết luận khi đạt Definition of Done trong [`verification.md`](verification.md).
12. Cập nhật [`../project-state.md`](../project-state.md) và [`../decisions.md`](../decisions.md) nếu cần.
13. Báo rõ:
   - File đã tạo và sửa.
   - Lệnh đã chạy.
   - Exit code và kết quả build/test.
   - Phần chưa hoàn thành, chưa chạy, không thể kiểm tra hoặc đang bị chặn.

## 2.1. Quyền và giới hạn của custom agents

- `project_explorer`: `read-only`; chỉ khảo sát và trả bằng chứng có đường dẫn/symbol, không sửa hoặc triển khai.
- `code_reviewer`: `read-only`; chỉ review diff, không sửa source và không đưa nhận xét style không ảnh hưởng chất lượng.
- `qa_verifier`: `workspace-write` để tạo `bin/`, `obj/`, `TestResults/` và log kiểm thử; bị cấm sửa file do con người viết trong `src/`, `tests/` hoặc nơi khác và không được tự sửa test.
- Không agent nào được chạy MEmu thật nếu người dùng chưa cho phép rõ ràng trong nhiệm vụ hiện tại.

## 3. Triển khai theo giai đoạn

Không xây toàn bộ ứng dụng trong một thay đổi khổng lồ. Mỗi giai đoạn phải build và test thành công trước khi chuyển sang giai đoạn tiếp theo.

### Giai đoạn 1 — Nền tảng và MEmu discovery

- Tạo solution.
- Tạo kiến trúc project.
- Tạo model.
- Tạo process runner.
- Cấu hình đường dẫn `memuc.exe`.
- Lấy và hiển thị danh sách máy ảo.
- Viết test cho command builder và parser.

### Giai đoạn 2 — Quản lý kịch bản

- Tạo trình quản lý kịch bản.
- Thêm các loại bước cơ bản.
- Lưu và đọc JSON.
- Thêm validation.
- Tạo template Chrome.

### Giai đoạn 3 — Execution engine

- Chạy tuần tự.
- Delay.
- Cancel.
- Timeout.
- Log.
- Trạng thái từng bước.

### Giai đoạn 4 — Hoàn thiện sản phẩm

- Hoàn thiện giao diện.
- Import/export JSON.
- Export `.bat`.
- Kết quả lần chạy gần nhất dạng snapshot gọn.
- Dark/light mode.
- Kiểm thử toàn bộ luồng.

## 4. Nhiệm vụ chỉ liên quan tài liệu

Khi yêu cầu chỉ là tạo hoặc tổ chức tài liệu:

- Không viết source code ứng dụng.
- Không cài dependency.
- Không chạy lệnh thay đổi hệ thống.
- Đọc lại file vừa tạo hoặc sửa để kiểm tra tính đầy đủ.
- Hiển thị đường dẫn file và tóm tắt yêu cầu đã hiểu.
- Có thể đề xuất kế hoạch cho bước tiếp theo, nhưng phải chờ yêu cầu mới bắt đầu triển khai.
- Ghi rõ build/test là `not run` nếu không phù hợp với thay đổi tài liệu; không diễn đạt như thể chúng đã chạy.

## 5. Kỷ luật phạm vi

- Không tự thêm chức năng ngoài [`../product-spec.md`](../product-spec.md).
- Không dùng nhu cầu thẩm mỹ để thay đổi hành vi sản phẩm.
- Nếu yêu cầu mới mâu thuẫn với quyết định hiện tại, nêu mâu thuẫn và xin xác nhận khi lựa chọn làm thay đổi đáng kể phạm vi.
- Một thay đổi tài liệu không tự cấp quyền triển khai source, cài đặt công cụ hoặc điều khiển MEmu.
