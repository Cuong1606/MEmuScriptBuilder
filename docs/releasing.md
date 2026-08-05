# Phát hành bản Portable

## Tạo bản phát hành

Chạy từ thư mục gốc repository:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1 -Version 1.0.0
```

Script xác minh repository, version và icon; publish `Release` cho `win-x64` theo chế độ self-contained, nhiều file, không trimming; loại PDB; thêm README người dùng và công cụ tạo shortcut; sau đó tạo ZIP cùng checksum SHA-256. Bản Portable không yêu cầu cài .NET Runtime và không chứa `memuc.exe`.

## Cấu trúc output

```text
artifacts/portable/
├── MEmuScriptStudio-<version>-win-x64/
│   ├── MEmuScriptStudio.exe
│   ├── README.txt
│   ├── Create Desktop Shortcut.cmd
│   └── các DLL và file runtime bắt buộc
├── MEmuScriptStudio-Portable-<version>-win-x64.zip
└── MEmuScriptStudio-Portable-<version>-win-x64.zip.sha256
```

ZIP giữ thư mục `MEmuScriptStudio-<version>-win-x64` làm thư mục gốc. Không xóa riêng DLL hoặc file `.deps.json`/`.runtimeconfig.json` khỏi output publish. Toàn bộ `artifacts/` là output cục bộ, đã được gitignore và không được commit.

## Quy tắc version

- Version phát hành dùng đúng dạng số `major.minor.patch`, không có số 0 thừa, ví dụ `1.1.0`.
- Tăng `patch` cho sửa lỗi tương thích, `minor` cho chức năng tương thích ngược và `major` cho thay đổi không tương thích.
- `Version` mặc định của app nằm tại `MEmuScriptStudio.App.csproj`. Khi chuẩn bị release mới, cập nhật giá trị này một lần rồi truyền cùng version cho `publish-portable.ps1`.
- Script truyền tham số version duy nhất đó vào ProductVersion/FileVersion lúc publish và dùng nó để đặt tên output; không sửa version riêng trong publish profile hoặc script.

## Cập nhật mà không mất dữ liệu

Settings và thư viện kịch bản không nằm trong source, `bin`, publish hoặc thư mục Portable. Chúng được lưu ổn định tại:

```text
%LOCALAPPDATA%\MEmuScriptStudio\settings.json
%LOCALAPPDATA%\MEmuScriptStudio\scripts.json
```

Để cập nhật, đóng ứng dụng, giải nén bản mới vào thư mục mới và chạy bản mới. Sau khi kiểm tra, có thể xóa thư mục Portable cũ; thao tác này không xóa dữ liệu trong LocalAppData. Nếu dùng shortcut Desktop, chạy lại `Create Desktop Shortcut.cmd` từ thư mục phiên bản mới.

## Kiểm tra trước khi phân phối

Sau build/test, chạy script release chính và kiểm tra ZIP mở được, có EXE/README/CMD cùng runtime dependencies, không có PDB/source/test/log/dữ liệu người dùng. Tính lại SHA-256 của ZIP và so với file `.sha256`. Không mở executable như một phần của bước kiểm tra đóng gói tự động; runtime smoke test được thực hiện riêng theo quy trình dự án.
