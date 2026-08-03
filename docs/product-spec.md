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

### 2.0. Khởi động ứng dụng

- Cửa sổ chính phải được tạo và hiển thị trước khi bắt đầu khởi tạo bất đồng bộ để người dùng luôn nhận được phản hồi trực quan ngay cả khi tải dữ liệu chậm.
- Trong khi khởi tạo, cửa sổ hiển thị rõ “Đang khởi tạo…” và vô hiệu hóa các chức năng chưa sẵn sàng. Khi hoàn tất, loading biến mất và workspace hoạt động bình thường.
- Lỗi khởi tạo không được để lại process không có UI: cửa sổ chính tiếp tục hiển thị thông báo lỗi dễ hiểu và lỗi được ghi vào startup log cục bộ.

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
- Checkbox chỉ chọn các mục cho thao tác hiện tại. Có lệnh “Chạy mục đã chọn” và “Chạy tất cả còn lại”; target chạy độc lập với instance đang focus để xem trước lệnh, chọn ứng dụng hoặc lấy tọa độ.
- Kịch bản có thể gắn mặc định với một máy ảo cụ thể hoặc yêu cầu chọn máy khi chạy.
- Chế độ chạy nhiều máy giữ lựa chọn dùng một kịch bản hiện tại cho tất cả, đồng thời hỗ trợ gán một kịch bản riêng cho từng giả lập.
- Cho phép chọn nhiều giả lập để gán cùng một kịch bản và có thao tác gán kịch bản hiện tại cho toàn bộ giả lập.
- Không giả định máy ảo đầu tiên luôn có index `0`.
- Không tự khởi động máy ảo đang tắt.

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
- Cho phép chạy cùng kịch bản trên một hoặc nhiều máy ảo.
- Mỗi lần bấm chạy tạo một launch group độc lập. Có thể nhận group mới khi group cũ đang chạy hoặc chờ; không nhận trùng một instance đang hoạt động/chờ ở group khác.
- Máy hợp lệ đầu tiên của mỗi group bắt đầu ngay. Trước mỗi máy tiếp theo trong chính group đó, scheduler chờ khoảng khởi chạy cố định hoặc một giá trị ngẫu nhiên mới; delay bằng 0 cho phép khởi chạy ngay và không phụ thuộc group khác hay target trước đã hoàn tất.
- Mặc định máy ảo đang tắt, bị mất hoặc không hợp lệ tại preflight được đánh dấu “Không khả dụng / Bỏ qua”; các target hợp lệ vẫn tiếp tục. Tùy chọn “Dừng toàn bộ nếu có giả lập không hợp lệ” mặc định tắt.
- Nếu người dùng không dừng, mọi target đã được nhận vào group phải chạy đúng một lần. Lỗi của một instance mặc định không dừng instance khác; instance đã hoàn tất/hủy có thể được chọn chạy lại thành runtime item mới.
- Trạng thái, trạng thái bước và log phải được giữ riêng theo từng instance.
- Cho phép dừng một instance, một group hoặc toàn bộ group đang hoạt động; target chưa khởi chạy không được bắt đầu sau khi nhận cancellation tương ứng và trạng thái terminal cũ không bị sửa lại.
- Chạy đa instance không tự scale, clamp hoặc biến đổi tọa độ Chạm, Nhấn giữ và Vuốt theo độ phân giải target.
- Trước khi bắt đầu phiên, scheduler phải chụp snapshot đúng kịch bản đã gán cho từng giả lập; sửa hoặc đổi selection sau đó không được làm đổi nội dung phiên đang chạy.
- Không làm đóng băng giao diện trong khi chạy.
- Hỗ trợ `CancellationToken`.
- Đặt timeout riêng cho từng lệnh.

Cấu hình chạy gần nhất được lưu trong `ApplicationSettings`, không nằm trong JSON kịch bản:

- Chế độ khoảng cách cố định/ngẫu nhiên và các giá trị mili giây.
- Tùy chọn dừng toàn bộ nếu có target không hợp lệ.
- Chế độ gán kịch bản và mapping instance index → script ID.

### 2.7. Không gian điều hành cửa sổ đa giả lập

- Quản lý grid chỉ được thay đổi vị trí và kích thước cửa sổ Windows của MEmu; trước khi thao tác phải đối chiếu window handle vẫn thuộc PID MEmu đã discovery. Không thay đổi độ phân giải, DPI, hướng màn hình, index thật hoặc cấu hình Android/MEmu.
- Danh sách bố cục hỗ trợ chọn nhiều, kéo-thả, nút lên/xuống và nhập vị trí để đổi thứ tự như một nhóm; có sắp xếp theo index, tên hoặc thứ tự tùy chỉnh.
- Số cửa sổ mỗi trang có ba chế độ: Tự động phân trang, Số lượng tùy chỉnh hoặc Một trang duy nhất. Số cột có chế độ tự động hoặc tùy chỉnh; số hàng luôn được tính tự động và không có giới hạn cứng theo số lượng cửa sổ/cột ngoài giới hạn số nguyên và tài nguyên hệ thống.
- Cho phép chọn màn hình Windows. Grid phải dùng work area của màn hình để không che taskbar và hỗ trợ tọa độ desktop nhiều màn hình.
- Cửa sổ thuộc trang không hiển thị được đưa ra ngoài các vùng màn hình đang dùng, ở các vị trí đỗ riêng không chồng nhau; process và kịch bản của chúng tiếp tục chạy.
- Kích thước cửa sổ có ba chế độ: Giữ nguyên kích thước (chỉ di chuyển), Tự động vừa ô (giữ tỷ lệ) hoặc Tùy chỉnh (khung rộng × cao tối đa, mặc định giữ tỷ lệ). Cửa sổ được căn giữa trong ô và có khoảng cách không âm giữa các ô.
- Auto-fit phải tính kích thước, thử resize bằng Windows API rồi đọc lại bounds thực tế. Khi MEmu không thu nhỏ đủ, tự giảm số cửa sổ hiệu lực mỗi trang và tạo thêm trang; không cần nút phát hiện kích thước tối thiểu.
- Nếu resize bị từ chối, ứng dụng chỉ cảnh báo người dùng kiểm tra tùy chọn “Kích thước cố định” của MEmu; không tự thay đổi tùy chọn này. Chế độ chỉ di chuyển vẫn phải hoạt động mà không gửi yêu cầu resize.
- Chế độ tập trung fit cửa sổ theo tỷ lệ vào work area để quan sát hoặc lấy tọa độ, giữ nguyên window handle và instance target; “Trở lại lưới” phải phục hồi đúng bounds/trang/ô trước focus. Overlay Chạm, Vuốt và Nhấn giữ tiếp tục tính viewport/bounds thực tế sau resize.
- MainWindow giữ trình soạn thảo; một Control Center có thể resize/maximize chứa hai tab Chạy nhiều máy và Bố cục, dùng chung đúng một MainViewModel/runtime state. Mở lại chỉ activate cửa sổ đang có; đóng Control Center không dừng group.
- Lưu trong `ApplicationSettings`: trang, thứ tự, chế độ/số cửa sổ mỗi trang, chế độ/số cột, chế độ/kích thước, khoảng cách, màn hình và bố cục gốc đã chụp. Có thao tác Xếp lưới, Trở lại lưới và Khôi phục bố cục ban đầu.
- Không thuộc đợt này: kịch bản tổng hợp A+B, tự scale tọa độ, helper APK và tự khởi động máy ảo đang tắt.

### 2.8. Quản lý kịch bản

- Tạo mới, đổi tên, nhân bản và xóa có xác nhận.
- Tìm kiếm và sắp xếp.
- Lưu tự động và hiển thị ngày cập nhật gần nhất.
- Lưu dữ liệu cục bộ.
- Import/export kịch bản dưới dạng JSON.
- Export thành file `.bat` để chạy ngoài ứng dụng.
- JSON phải có version để hỗ trợ nâng cấp cấu trúc dữ liệu sau này.

### 2.9. Mẫu kịch bản

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

### 4.1. Tiêu chí chấp nhận chạy đa giả lập

1. Chọn được nhiều target hoặc toàn bộ danh sách và vẫn giữ một instance focus riêng cho preview/capture.
2. Preflight không tự khởi động instance; target không khả dụng được bỏ qua mặc định hoặc chặn toàn bộ theo tùy chọn.
3. Mỗi launch group có máy đầu tiên bắt đầu ngay; delay chỉ áp dụng giữa các target trong cùng group. Group mới không chờ group cũ, và một instance không thể đồng thời thuộc hai group active/waiting.
4. Mọi target hợp lệ được chạy đúng một lần nếu không bị người dùng hủy.
5. Lỗi hoặc dừng riêng một instance không mặc định ảnh hưởng instance khác; dừng tất cả ngăn mọi lần khởi chạy mới.
6. UI giữ trạng thái, trạng thái bước, command preview, thời gian, exit code, stdout và stderr riêng theo instance.
7. Cấu hình chạy được khôi phục sau restart từ `ApplicationSettings` và không xuất hiện trong `.memuscript`.
8. Command tọa độ dùng nguyên giá trị của kịch bản trên mọi target, không có phép scale ngầm.
9. Ở chế độ gán riêng, mỗi target chạy đúng snapshot kịch bản đã gán và UI hiển thị tên kịch bản/trạng thái/log riêng.
10. Checkbox được bỏ sau thao tác gán/chạy/di chuyển thành công; runtime giữ số đang chạy, đang chờ và số group, đồng thời cho chạy lại target terminal thành item mới.

### 4.2. Tiêu chí chấp nhận không gian điều hành cửa sổ

1. Có thể sắp xếp và di chuyển một hoặc nhiều giả lập bằng kéo-thả, mũi tên hoặc vị trí nhập mà không đổi index thật.
2. Planner tự tính hàng/cột/trang theo mọi chế độ cấu hình, không tạo ô chồng nhau và dùng work area của màn hình đã chọn.
3. Auto-fit đọc lại bounds; resize bị giới hạn làm giảm số cửa sổ hiệu lực mỗi trang hoặc tăng số trang và tạo cảnh báo “Kích thước cố định”.
4. Chế độ chỉ di chuyển không yêu cầu resize; không có lệnh thay đổi cấu hình MEmu/Android.
5. Tập trung rồi trở lại giữ đúng trang/ô; capture tọa độ tiếp tục dùng window handle và viewport thực tế.
6. Bố cục và cấu hình được khôi phục từ settings; khôi phục bố cục ban đầu dùng vị trí/kích thước đã chụp trước lần xếp lưới đầu tiên.

Việc kết luận các tiêu chí liên quan đến MEmu phải tuân thủ yêu cầu smoke test trong [`agent/verification.md`](agent/verification.md).
