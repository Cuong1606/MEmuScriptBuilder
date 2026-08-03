# MEmu Script Studio — Project Instructions

## 1. Mục tiêu và phạm vi ổn định

MEmu Script Studio là ứng dụng Windows desktop giúp người dùng tạo, lưu, chỉnh sửa và chạy các lệnh hoặc kịch bản dành cho trình giả lập Android MEmu thông qua `memuc.exe`.

Ứng dụng xây dựng kịch bản từ các bước lệnh rõ ràng và thực thi trên một hoặc nhiều máy ảo MEmu. Đây không phải công cụ ghi macro bằng hình ảnh. Phạm vi chức năng và tiêu chí MVP đầy đủ nằm trong [`docs/product-spec.md`](docs/product-spec.md).

Không xây toàn bộ ứng dụng trong một thay đổi lớn. Triển khai theo các giai đoạn trong [`docs/agent/workflow.md`](docs/agent/workflow.md), và chỉ chuyển giai đoạn sau khi giai đoạn hiện tại đã build và test thành công.

## 2. Công nghệ bắt buộc

- C#, .NET 8 và WPF.
- Kiến trúc MVVM và Dependency Injection của .NET.
- `System.Text.Json` cho JSON.
- `ProcessStartInfo` để chạy `memuc.exe`.
- `async`/`await` cho mọi quá trình thực thi lệnh.
- `CancellationToken` để dừng kịch bản.
- `ObservableCollection` cho dữ liệu hiển thị động.
- Có thể thêm thư viện MVVM ổn định chỉ khi thực sự cần thiết và phải giải thích lý do trước khi thêm dependency.
- Không dùng Electron, Python hoặc server web cho phiên bản đầu tiên.
- Không thay đổi công nghệ chính khi chưa được người dùng chấp thuận.

Kiến trúc chi tiết, ranh giới project, model và nguyên tắc process runner nằm trong [`docs/agent/architecture.md`](docs/agent/architecture.md).

## 3. Giới hạn và hành vi bị cấm

Không thêm các chức năng sau nếu người dùng chưa yêu cầu rõ ràng:

- Ghi thao tác chuột hoặc ghi macro từ hành động trực tiếp.
- Chụp màn hình, OCR, nhận diện hình ảnh, computer vision hoặc tìm nút bằng hình ảnh.
- Điều khiển trình giả lập bằng AI.
- Dịch vụ server/cloud, tài khoản người dùng hoặc đồng bộ trực tuyến.
- Theo dõi bí mật.
- Tự động tải hoặc cài phần mềm bên ngoài.
- Thay đổi cấu hình máy ảo ngoài yêu cầu của kịch bản.
- Quản lý quảng cáo, tài khoản hoặc dữ liệu trình duyệt.

Ứng dụng chỉ là công cụ cục bộ để tạo và thực thi lệnh MEMUC trên máy tính của người dùng. Không tự ý thay đổi chức năng để phục vụ thiết kế giao diện.

## 4. An toàn quan trọng nhất

- Không xóa file hoặc máy ảo MEmu.
- Không cung cấp sẵn lệnh `memuc remove`, clone, import, export hoặc reset máy ảo trong MVP.
- Không chạy lệnh khi chưa xác định được máy ảo mục tiêu.
- Cảnh báo rõ ràng trước lệnh thô có khả năng nguy hiểm.
- Gọi trực tiếp `memuc.exe` cho từng bước thông thường; không dùng `cmd.exe`, không nối lệnh bằng `&&` hoặc chuỗi shell.
- Dùng `ProcessStartInfo.ArgumentList` khi phù hợp; xử lý đúng đường dẫn có khoảng trắng và không tạo chuỗi tham số thiếu kiểm soát.
- Delay nội bộ dùng `Task.Delay`, không chạy `timeout.exe`.
- Luôn thu thập standard output, standard error và kiểm tra exit code. Không báo thành công khi process thực tế lỗi.
- Lệnh xem trước phải tương đương về logic với lệnh thực thi.
- Không lưu mật khẩu/token dạng văn bản thuần và không gửi dữ liệu ra Internet.
- Tất cả dữ liệu ứng dụng được lưu cục bộ. Không tự động ghi dữ liệu nhạy cảm vào log khi biến được đánh dấu bí mật.

Đọc phần an toàn và thực thi chi tiết trong [`docs/agent/architecture.md`](docs/agent/architecture.md) trước khi sửa command builder, process runner hoặc execution engine.

## 5. Quyền sửa source code

Agent chính là agent duy nhất được phép sửa source code, trừ khi người dùng yêu cầu rõ ràng cách phân công khác. Agent phụ, nếu được sử dụng, chỉ được nghiên cứu, rà soát hoặc đề xuất thay đổi source code; không được tự sửa source code.

Quy tắc này không mở rộng quyền thực hiện hành động nguy hiểm, thay đổi hệ thống hoặc thao tác ngoài phạm vi người dùng đã yêu cầu.

Custom agents cấp dự án:

- `project_explorer`: khảo sát repository ở chế độ `read-only`.
- `qa_verifier`: chạy restore/build/test ở chế độ `workspace-write`, nhưng bị cấm sửa file do con người viết, đặc biệt trong `src/` và `tests/`; chỉ được ghi artifact build/test.
- `code_reviewer`: review diff ở chế độ `read-only`.

## 6. Workflow cấp cao

Khi nhận yêu cầu phát triển:

1. Đọc toàn bộ file này và các tài liệu được định tuyến ở mục 7.
2. Kiểm tra cấu trúc, trạng thái code và [`docs/project-state.md`](docs/project-state.md); không giả định tính năng đã tồn tại.
3. Trình bày kế hoạch ngắn trước thay đổi lớn.
4. Chỉ sửa phần liên quan; không xóa chức năng đang hoạt động.
5. Thực hiện vòng lặp verification bắt buộc.
6. Cập nhật project state và decision log nếu trạng thái hoặc quyết định thay đổi.
7. Báo file đã tạo/sửa, lệnh đã chạy, kết quả build/test và phần chưa hoàn thành hoặc chưa thể kiểm tra.

Quy trình chi tiết nằm trong [`docs/agent/workflow.md`](docs/agent/workflow.md).

### Vòng phối hợp custom agents

1. Với nhiệm vụ lớn hoặc chưa rõ, agent chính dùng `project_explorer` trước khi sửa code.
2. Agent chính là writer duy nhất và tự thực hiện thay đổi source code.
3. Sau khi viết code, dùng `qa_verifier` để restore, build, test và kiểm tra acceptance criteria.
4. Sau QA, dùng `code_reviewer` để review diff.
5. Agent chính chỉ sửa các lỗi đã được xác nhận từ bằng chứng QA/review.
6. `qa_verifier` chạy lại các test bị ảnh hưởng sau khi sửa.
7. Tối đa 3 vòng sửa–kiểm tra cho cùng một vấn đề; nếu không tiến triển, dừng và báo blocker.
8. Chỉ kết luận hoàn thành khi đạt Definition of Done trong [`docs/agent/verification.md`](docs/agent/verification.md).

## 7. Định tuyến tài liệu theo nhiệm vụ

| Loại nhiệm vụ | Phải đọc trước khi làm |
| --- | --- |
| Tiếp tục từ cuộc trò chuyện mới | [`docs/project-state.md`](docs/project-state.md), sau đó [`docs/decisions.md`](docs/decisions.md) nếu có quyết định liên quan |
| Thay đổi chức năng, phạm vi hoặc tiêu chí MVP | [`docs/product-spec.md`](docs/product-spec.md) |
| Thay đổi cấu trúc code, model, project, process runner hoặc persistence | [`docs/agent/architecture.md`](docs/agent/architecture.md) |
| Thay đổi giao diện hoặc hành vi UI | [`docs/agent/ui-guidelines.md`](docs/agent/ui-guidelines.md) và phần chức năng liên quan trong product spec |
| Bắt đầu một thay đổi triển khai | [`docs/agent/workflow.md`](docs/agent/workflow.md) |
| Chuẩn bị kết luận hoàn thành | [`docs/agent/verification.md`](docs/agent/verification.md) |
| Chuẩn bị compact hoặc chuyển hội thoại | [`docs/agent/context-management.md`](docs/agent/context-management.md) |

Chỉ nạp tài liệu liên quan đến nhiệm vụ hiện tại, ngoại trừ file này và `project-state.md` luôn phải được đọc khi tiếp tục công việc.

## 8. Verification và Definition of Done

Tuân thủ vòng lặp và cách ghi bằng chứng trong [`docs/agent/verification.md`](docs/agent/verification.md).

- Không tuyên bố hoàn thành nếu chưa build thành công.
- Không tuyên bố test passed nếu test chưa thực sự chạy.
- Phân biệt rõ `passed`, `failed`, `not run` và `blocked`.
- Không xóa, bỏ qua hoặc làm yếu test để nhận kết quả xanh.
- Mọi kết luận verification phải có lệnh, exit code và kết quả.
- Test tự động không thay thế smoke test trên MEmu thật.
- Không tuyên bố tích hợp MEmu hoạt động nếu chưa thực sự chạy trên MEmu.
- Mỗi giai đoạn và MVP chỉ hoàn thành khi đạt Definition of Done tương ứng trong verification và product spec.

## 9. Quản lý context và bàn giao

`AGENTS.md` chỉ chứa quy tắc ổn định. Trạng thái hiện tại phải được duy trì trong [`docs/project-state.md`](docs/project-state.md); quyết định bền vững được ghi trong [`docs/decisions.md`](docs/decisions.md).

Trước khi chuyển hội thoại hoặc compact, và khi context đạt khoảng 70–80%, tạo checkpoint theo [`docs/agent/context-management.md`](docs/agent/context-management.md). Checkpoint phải giữ mục tiêu hiện tại, quyết định đã chốt, file đã sửa, test gần nhất, lỗi chưa xử lý, blocker và bước tiếp theo; không chép log terminal dài hoặc nội dung lặp lại.

## 10. Nhiệm vụ chỉ tổ chức tài liệu

Khi người dùng chỉ yêu cầu tạo hoặc tổ chức tài liệu:

- Không tạo source code, không cài dependency và không thực hiện thay đổi hệ thống.
- Đọc lại toàn bộ file vừa tạo hoặc sửa.
- Báo đường dẫn file, tóm tắt nội dung và nêu rõ những gì chưa chạy hoặc chưa kiểm tra.
- Nếu đề xuất bước triển khai tiếp theo, phải chờ yêu cầu mới bắt đầu.

## 11. `.codex/rules`

Không đặt hướng dẫn Markdown vào `.codex/rules`. Thư mục đó chỉ dành cho execution-policy kiểm soát lệnh terminal.

## 12. Runtime smoke test khi mở ứng dụng

- Build phải được chạy riêng; không thêm build vào script mở ứng dụng.
- Mọi lần agent mở ứng dụng để smoke test phải dùng `scripts/launch-smoke.ps1`; không gọi trực tiếp executable, `dotnet run` hoặc một launcher khác.
- Script chỉ được gọi một lần cho mỗi lần người dùng yêu cầu mở ứng dụng. Script tự từ chối mở thêm nếu đã có process `MEmuScriptStudio.App`.
- Khi script in `READY`, dừng mọi thao tác tự động và chờ người dùng runtime smoke test thủ công.
- Khi script in `TIMEOUT`, chỉ báo blocker cùng output của script; không tự điều tra log, thread, module hoặc thực hiện chuỗi chẩn đoán kéo dài.
- Không kill, restart hoặc mở thêm process ứng dụng nếu chưa được người dùng cho phép rõ ràng.
- Không thao tác trong ứng dụng, chạy kịch bản hoặc điều khiển MEmu trừ khi người dùng yêu cầu riêng.
