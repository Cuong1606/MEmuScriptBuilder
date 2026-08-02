# MEmu Script Studio — Product Specification

## 1. Mục tiêu sản phẩm

Xây dựng một ứng dụng Windows desktop giúp người dùng tạo, lưu, chỉnh sửa và chạy các lệnh hoặc kịch bản dành riêng cho trình giả lập Android MEmu thông qua `memuc.exe`.

Ứng dụng không phải công cụ ghi macro bằng hình ảnh. Người dùng xây dựng kịch bản bằng các bước lệnh rõ ràng, sau đó chạy trực tiếp trên một hoặc nhiều máy ảo MEmu.

Ví dụ kịch bản hợp lệ khi biểu diễn dưới dạng batch:

```bat
memuc.exe -i 0 execcmd "am force-stop com.android.chrome"
timeout /t 2 /nobreak >nul
memuc.exe -i 0 execcmd "am start -n com.android.chrome/com.google.android.apps.chrome.Main"
```

Trong ứng dụng, ví dụ này phải được biểu diễn thành ba bước độc lập:

1. Chạy Android shell command `am force-stop com.android.chrome`.
2. Chờ 2 giây.
3. Chạy Android shell command `am start -n com.android.chrome/com.google.android.apps.chrome.Main`.

Thực thi nội bộ phải gọi `memuc.exe` trực tiếp cho từng bước và dùng delay của C#; không ghép chuỗi lệnh bằng `&&`.

## 2. Phạm vi phiên bản đầu tiên

### 2.1. Cấu hình MEmu

- Tự động tìm vị trí `memuc.exe` nếu có thể.
- Cho phép người dùng chọn thủ công file `memuc.exe`.
- Lưu đường dẫn đã chọn trong cấu hình ứng dụng.
- Kiểm tra file tồn tại trước khi chạy lệnh.
- Hiển thị trạng thái kết nối với MEmu.
- Không hard-code một đường dẫn cài đặt duy nhất.

### 2.2. Danh sách máy ảo

- Chạy `memuc listvms` để lấy danh sách máy ảo.
- Hiển thị index, tên, trạng thái đang chạy/đã tắt và PID nếu dữ liệu trả về có PID.
- Có nút làm mới danh sách.
- Cho phép chọn một hoặc nhiều máy ảo để chạy kịch bản.
- Kịch bản có thể gắn mặc định với một máy ảo cụ thể hoặc yêu cầu chọn máy khi chạy.
- Không giả định máy ảo đầu tiên luôn có index `0`.

### 2.3. Trình tạo kịch bản

Mỗi kịch bản gồm nhiều bước có thứ tự. Các loại bước phải hỗ trợ:

1. Android shell command qua `memuc.exe -i INDEX execcmd "COMMAND"`.
2. MEMUC command trực tiếp.
3. Mở ứng dụng bằng package/activity.
4. Dừng ứng dụng bằng package name.
5. Nhấn phím Android: Back, Home, Menu, Volume up và Volume down.
6. Nhập văn bản.
7. Chạm theo tọa độ bằng Android shell `input tap X Y`.
8. Vuốt bằng Android shell `input swipe X1 Y1 X2 Y2 DURATION`.
9. Chờ theo mili giây hoặc giây.
10. Lệnh tùy chỉnh.
11. Ghi chú không thực thi.

Mỗi bước cần có:

- Tên và loại bước.
- Các trường tham số tương ứng.
- Công tắc bật/tắt bước.
- Tùy chọn tiếp tục hoặc dừng khi bước lỗi.
- Nút chạy thử riêng bước đó.
- Nút nhân bản và nút xóa.
- Kéo thả hoặc nút lên/xuống để thay đổi thứ tự.

### 2.4. Trình soạn thảo

- Có danh sách bước ở bên trái hoặc giữa.
- Có bảng thuộc tính của bước đang chọn.
- Có khu vực xem trước lệnh thực tế sẽ được chạy.
- Không bắt người dùng tự viết toàn bộ cú pháp cho lệnh phổ biến.
- Vẫn có chế độ lệnh thô cho người dùng nâng cao.
- Cảnh báo trước khi chạy lệnh thô có khả năng nguy hiểm.

### 2.5. Biến trong kịch bản

Hỗ trợ tối thiểu:

```text
{{instanceIndex}}
{{instanceName}}
{{packageName}}
{{activityName}}
{{url}}
{{text}}
```

- Cho phép khai báo biến riêng cho từng kịch bản.
- Cho phép nhập giá trị biến trước khi chạy.
- Hiển thị lỗi nếu còn biến chưa có giá trị.
- Không thay thế biến một cách mơ hồ.
- Có phần xem trước lệnh sau khi thay biến.

### 2.6. Chạy kịch bản

- Thực thi từng bước đúng thứ tự và hiển thị bước đang chạy.
- Trạng thái bước gồm: Chưa chạy, Đang chạy, Thành công, Thất bại, Đã bỏ qua và Đã hủy.
- Có nút Chạy, Tạm dừng nếu khả thi, Dừng và Chạy lại.
- Ghi thời gian bắt đầu/kết thúc, exit code, standard output, standard error và lệnh đã thực thi.
- Cho phép chạy cùng kịch bản lần lượt trên nhiều máy ảo.
- Phiên bản đầu tiên chưa cần chạy song song trên nhiều máy ảo.
- Không làm đóng băng giao diện trong khi chạy.
- Hỗ trợ `CancellationToken`.
- Đặt timeout riêng cho từng lệnh.

### 2.7. Quản lý kịch bản

- Tạo mới, đổi tên, nhân bản và xóa có xác nhận.
- Tìm kiếm và sắp xếp.
- Lưu tự động và hiển thị ngày cập nhật gần nhất.
- Lưu dữ liệu cục bộ.
- Import/export kịch bản dưới dạng JSON.
- Export thành file `.bat` để chạy ngoài ứng dụng.
- JSON phải có version để hỗ trợ nâng cấp cấu trúc dữ liệu sau này.

### 2.8. Mẫu kịch bản

Cung cấp sẵn:

- Khởi động lại Chrome.
- Mở một ứng dụng.
- Dừng rồi mở lại ứng dụng.
- Mở một URL trong Chrome.
- Nhấn Home.
- Nhập văn bản.
- Chạm vào tọa độ.
- Vuốt màn hình.
- Chạy Android shell command tùy chỉnh.

Template “Khởi động lại Chrome” phải dùng đúng ba bước logic:

```text
am force-stop com.android.chrome
delay 2000 ms
am start -n com.android.chrome/com.google.android.apps.chrome.Main
```

## 3. Ngoài phạm vi mặc định

Không thêm nếu người dùng chưa yêu cầu rõ ràng:

- Ghi thao tác chuột hoặc ghi macro từ hành động trực tiếp.
- Chụp màn hình, OCR, nhận diện hình ảnh, computer vision hoặc tìm nút bằng hình ảnh.
- Điều khiển trình giả lập bằng AI.
- Dịch vụ server/cloud, tài khoản người dùng hoặc đồng bộ trực tuyến.
- Theo dõi bí mật.
- Tự động tải hoặc cài phần mềm bên ngoài.
- Thay đổi cấu hình máy ảo ngoài yêu cầu của kịch bản.
- Quản lý quảng cáo, tài khoản hoặc dữ liệu trình duyệt.

Ứng dụng chỉ tạo và thực thi lệnh MEMUC cục bộ trên máy tính của người dùng.

## 4. Tiêu chí chấp nhận MVP

MVP chỉ được coi là hoàn thành khi người dùng có thể:

1. Chọn đúng file `memuc.exe`.
2. Xem danh sách máy ảo MEmu.
3. Chọn một máy ảo.
4. Tạo kịch bản gồm force-stop Chrome, chờ 2 giây và mở lại Chrome.
5. Xem trước các lệnh.
6. Lưu kịch bản.
7. Đóng và mở lại ứng dụng mà kịch bản vẫn còn.
8. Chạy kịch bản.
9. Xem trạng thái và log của từng bước.
10. Dừng một kịch bản đang chạy.
11. Export kịch bản thành JSON và `.bat`.
12. Build ứng dụng thành công trên Windows.

Việc kết luận các tiêu chí liên quan đến MEmu phải tuân thủ yêu cầu smoke test trong [`agent/verification.md`](agent/verification.md).
