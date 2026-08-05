MEmu Script Studio Portable
===========================

Yêu cầu
-------
- Windows 10 hoặc Windows 11, 64-bit.
- Không cần cài đặt .NET Runtime.
- MEmu và memuc.exe không đi kèm trong gói này.

Mở ứng dụng
-----------
1. Giải nén toàn bộ file ZIP vào một thư mục riêng.
2. Chạy MEmuScriptStudio.exe trong thư mục vừa giải nén.
3. Không di chuyển riêng file EXE ra khỏi các DLL và file runtime đi kèm.

Chọn memuc.exe
--------------
Trong thanh trên cùng, chọn "Chọn memuc.exe…" rồi trỏ tới memuc.exe trong thư mục cài đặt MEmu. Lựa chọn này được lưu cho những lần mở sau.

Tạo shortcut Desktop
--------------------
Chạy "Create Desktop Shortcut.cmd" ngay trong thư mục Portable. Script tạo hoặc cập nhật shortcut của người dùng hiện tại, không yêu cầu quyền Administrator.

Cập nhật sang bản Portable mới
------------------------------
1. Đóng MEmu Script Studio.
2. Giải nén ZIP phiên bản mới vào một thư mục mới; không chép riêng EXE đè lên bản cũ.
3. Mở bản mới và chạy lại "Create Desktop Shortcut.cmd" nếu muốn shortcut trỏ tới thư mục mới.
4. Sau khi xác nhận bản mới hoạt động, có thể xóa thư mục Portable cũ.

Dữ liệu và kịch bản
-------------------
Dữ liệu được lưu ngoài thư mục Portable tại:

  %LOCALAPPDATA%\MEmuScriptStudio

Trong đó settings.json chứa cài đặt và scripts.json chứa thư viện kịch bản. Xóa hoặc thay thư mục Portable không tự xóa các file này, vì vậy cập nhật theo hướng dẫn trên không làm mất kịch bản.

Gỡ ứng dụng và xóa dữ liệu
-------------------------
Đây là ứng dụng Portable nên không có mục Uninstall trong Windows.

Để xóa ứng dụng nhưng giữ dữ liệu:
1. Đóng MEmu Script Studio.
2. Xóa thư mục Portable đã giải nén.
3. Xóa shortcut MEmu Script Studio trên Desktop nếu có.
4. Giữ nguyên thư mục %LOCALAPPDATA%\MEmuScriptStudio.

Để xóa hoàn toàn, thực hiện các bước trên rồi xóa thêm thư mục %LOCALAPPDATA%\MEmuScriptStudio. Trong thư mục này, scripts.json chứa thư viện kịch bản; settings.json chứa cài đặt và đường dẫn memuc.exe. Hãy sao lưu thư mục dữ liệu trước khi xóa nếu còn cần kịch bản hoặc cài đặt cũ.

Xem phiên bản
-------------
Nhấp chuột phải MEmuScriptStudio.exe, chọn Properties (Thuộc tính), rồi xem tab Details (Chi tiết).

Windows SmartScreen
-------------------
Ứng dụng hiện chưa được ký số nên Windows SmartScreen có thể hiển thị cảnh báo. Chỉ chạy file khi tải từ nguồn phát hành tin cậy và đã đối chiếu SHA-256 với file checksum đi kèm. Nếu không xác minh được nguồn hoặc checksum, không chạy ứng dụng.
