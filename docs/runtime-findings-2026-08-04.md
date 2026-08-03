# Runtime findings — 2026-08-04

Các finding dưới đây đã được xác nhận bằng runtime smoke test thủ công. Tài liệu chỉ ghi nhận lỗi và chia phạm vi xử lý; chưa kết luận nguyên nhân từ source hoặc automated tests.

## Phase A — Sửa state/UI, chưa resize MEmu

1. Nút **Dán** không dán bước sang kịch bản khác.
2. Tự động phân trang sai.
3. **Tùy chỉnh số cửa sổ/trang** và **Một trang** không rebuild layout đúng.
4. **Sắp theo tên/index** và chuyển trang không hoạt động.
5. Số cột, kích thước render và số cửa sổ/trang chưa được command sử dụng đúng.
6. MainWindow còn hai nút mở Control Center.
7. Group card bị lệch và cột bị cắt.
8. Xóa nhiều mục lịch sử chỉ xóa một mục.
9. Đổi nhãn hành động thành **Gán kịch bản đang chọn cho tất cả**.

Phase A chỉ sửa state, command routing, collection selection, wording và XAML/layout của ứng dụng. Không thử resize, focus hoặc restore cửa sổ MEmu trong phase này.

## Phase B — Nghiên cứu và sửa Grid/Focus bằng MEmu thật

1. Grid/Focus thay đổi outer window nhưng nội dung Android không resize đúng.
2. Khôi phục bố cục chậm; không được polling geometry nền.

Phase B phải tái hiện và đo outer/client/Android render viewport trên MEmu thật, sau đó sửa Grid/Focus dựa trên bằng chứng runtime. Không coi thay đổi outer window hoặc automated fake-platform tests là xác nhận thành công.

