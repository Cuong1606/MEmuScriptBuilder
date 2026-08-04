# UI/UX Guidelines

Đọc tài liệu này trước khi thiết kế hoặc sửa giao diện. Luôn đọc thêm phần chức năng liên quan trong [`../product-spec.md`](../product-spec.md); thiết kế không được thay đổi phạm vi hoặc hành vi sản phẩm.

## 1. Định hướng

Ứng dụng cần có giao diện Windows desktop hiện đại, rõ ràng và không giống một công cụ dòng lệnh thô. Tính dễ hiểu, khả năng kiểm soát và trạng thái thực thi rõ ràng quan trọng hơn hiệu ứng trang trí.

## 2. Bố cục hiện hành

### MainWindow

- Thanh công cụ kết nối MEmu và mở Control Center.
- Thư viện kịch bản, danh sách bước và trình chỉnh sửa thuộc tính theo ba vùng co giãn.
- Khu vực xem trước lệnh thực tế sẽ được chạy; không nhân đôi trạng thái chạy hoặc full log.

### Control Center

- Chỉ có tab `Đang hoạt động`, gồm thiết lập chạy, danh sách target, các launch group active và `Kết quả lần chạy gần nhất`.
- Không có History nhiều phiên, Trang và thứ tự hoặc điều khiển window-layout.
- Active detail chỉ hiển thị trạng thái/bước hiện tại cần thiết; kết quả gần nhất là snapshot gọn, không giữ full log.

Không bắt người dùng tự viết toàn bộ cú pháp cho lệnh phổ biến. Chế độ lệnh thô vẫn phải có cho người dùng nâng cao và phải cảnh báo rõ khi lệnh có khả năng nguy hiểm.

## 3. Yêu cầu trải nghiệm

- Có dark mode và light mode.
- Responsive trong phạm vi cửa sổ desktop.
- Hoạt động tốt từ độ phân giải 1280×720 trở lên.
- Có empty state và loading state.
- Có thông báo lỗi dễ hiểu và hướng dẫn hành động tiếp theo khi phù hợp.
- Không lạm dụng hiệu ứng chuyển động.
- Không dùng màu sắc làm tín hiệu trạng thái duy nhất; kết hợp text, icon hoặc hình dạng.
- Hỗ trợ keyboard navigation cơ bản và focus state dễ nhận biết.
- Nút/hành động nguy hiểm phải khác biệt rõ ràng.
- Trạng thái Chưa chạy, Đang chạy, Thành công, Thất bại, Đã bỏ qua và Đã hủy phải phân biệt được mà không chỉ dựa vào màu.
- UI không được đóng băng trong khi thực thi lệnh.
- Các thao tác xóa phải có xác nhận theo product spec.

## 4. Trình tạo bước

Mỗi bước phải thể hiện hoặc cho phép truy cập rõ ràng:

- Tên và loại bước.
- Trạng thái bật/tắt.
- Chính sách tiếp tục hoặc dừng khi lỗi.
- Chạy thử, nhân bản và xóa.
- Kéo thả hoặc nút lên/xuống để đổi thứ tự.
- Các trường tham số phù hợp với loại bước.
- Preview sau khi thay biến, cùng lỗi dễ hiểu nếu còn biến thiếu.

## 5. Design skills

Khi thực sự thiết kế hoặc sửa giao diện và các skill đã được cài:

- Dùng `$frontend-design` để định hướng phong cách và chất lượng giao diện.
- Dùng `$ui-ux-pro-max` để kiểm tra design system, usability, accessibility và tính nhất quán.

Hai skill chỉ hỗ trợ thiết kế. Chúng không được thêm, bớt hoặc thay đổi chức năng đã quy định trong product spec.

## 6. Review checklist

- Bố cục vẫn dùng được ở 1280×720 và các kích thước desktop lớn hơn.
- Light/dark theme đều dễ đọc.
- Empty, loading, error, disabled và executing state đã được xử lý.
- Keyboard navigation và focus state cơ bản hoạt động.
- Trạng thái không chỉ dựa vào màu.
- Hành động nguy hiểm có phân biệt và xác nhận phù hợp.
- Command preview phản ánh đúng logic thực thi.
- Không có thay đổi chức năng chỉ để phù hợp với thiết kế.
