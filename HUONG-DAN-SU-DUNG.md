# Hướng dẫn nhanh MEmu Script Studio

## Cài lần đầu

Mở **Kết nối / Cài đặt thiết bị**, rồi bấm **Kiểm tra kết nối**.

- **MEmu: Sẵn sàng**: ứng dụng đã tìm thấy `memuc.exe`.
- **ADB: Sẵn sàng**: ứng dụng đã có ADB để kết nối điện thoại/boxphone.
- Đường dẫn đang dùng được hiển thị ngay dưới từng trạng thái.

Nếu chưa tìm thấy công cụ, dùng nút **Chọn…** để chọn file thủ công.

## Nếu dùng MEmu

1. Cài MEmu và mở MEmu ít nhất một lần.
2. Mở MEmu Script Studio. Ứng dụng sẽ tự tìm `memuc.exe`.
3. Khởi động máy ảo cần dùng, rồi bấm **Kiểm tra kết nối**.

Bạn không cần tự cấu hình ADB để chạy kịch bản trên MEmu.

## Nếu dùng điện thoại/boxphone

Bạn **không cần cài Android SDK hoặc Platform Tools**. Bản Portable đã có ADB.

1. Dùng cáp USB có truyền dữ liệu để nối thiết bị với máy tính.
2. Trên Android, bật **Tùy chọn nhà phát triển** rồi bật **USB debugging / Gỡ lỗi USB**.
3. Mở khóa màn hình. Khi thấy câu hỏi **Cho phép gỡ lỗi USB?**, bấm **Cho phép** để xác nhận RSA.
4. Trong ứng dụng, bấm **Kiểm tra kết nối**.

Nếu Windows không nhận thiết bị, hãy cài **USB driver chính hãng** của điện thoại/boxphone, sau đó rút cắm lại cáp.

## Khi thiết bị chưa kết nối được

- `unauthorized`: mở khóa màn hình và xác nhận hộp thoại RSA. Nếu cần, thu hồi quyền gỡ lỗi USB rồi kết nối lại.
- `offline`: rút cắm lại cáp, thử cổng USB/cáp dữ liệu khác và giữ màn hình mở khóa.
- Không thấy thiết bị: kiểm tra USB debugging, cáp dữ liệu và driver USB của hãng.

MEmu Script Studio không tự xác nhận RSA và không tự đổi serial. Mọi lệnh Android luôn chạy đúng serial của thiết bị đã chọn.

## Tạo và chạy kịch bản cơ bản

1. Tạo kịch bản mới trong thư viện.
2. Chọn **Thiết bị soạn thảo**.
3. Tạo bước, ví dụ **Chạm** hoặc **Chờ**, rồi bấm **Thêm bước**.
4. Khi sửa bước đã có, bấm **Lưu bước**.
5. Mở **Trung tâm điều khiển**, chọn đúng thiết bị, gán kịch bản và chạy.

Nên thử với một thiết bị trước khi chạy nhiều thiết bị.

## Gỡ ứng dụng / Reset hoàn toàn

- **Chỉ gỡ bản Portable:** đóng MEmu Script Studio rồi xóa thư mục đã giải nén. ADB đi kèm trong `tools\adb` cũng được xóa cùng thư mục này; dữ liệu cá nhân vẫn được giữ lại.
- **Reset toàn bộ dữ liệu:** đóng ứng dụng, sao lưu nếu cần, rồi xóa thư mục `%LOCALAPPDATA%\MEmuScriptStudio`. Thao tác này xóa `settings.json` (gồm cấu hình và đường dẫn MEMUC/ADB), `scripts.json` (thư viện kịch bản), thư mục `logs` và các bản sao phục hồi nếu có.
- **Recent Runs không được lưu vào thư mục dữ liệu:** lịch sử này chỉ tồn tại trong RAM và mất khi đóng ứng dụng.
- Việc gỡ/reset không xóa MEmu, Android USB driver, Android Platform Tools hay phần mềm bên ngoài.
- Muốn cài sạch lại: xóa cả thư mục Portable và `%LOCALAPPDATA%\MEmuScriptStudio`, rồi giải nén bản Portable mới.

## Nâng cao / dự phòng

Ứng dụng ưu tiên đường dẫn ADB bạn đã chọn, sau đó ADB đi kèm trong `tools/adb`. Nếu cần thay thế hoặc chẩn đoán, bạn có thể cài Android SDK Platform Tools và chọn `adb.exe` của bộ đó thủ công.
