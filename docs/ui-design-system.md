# MEmu Script Studio — WPF UI Design System

Tài liệu này là nguồn chuẩn cho UI/XAML của ứng dụng. Mục tiêu là một bàn điều hành Windows gọn, rõ và đủ dày để quản lý 30–60 giả lập; không thay đổi chức năng trong [`product-spec.md`](product-spec.md).

## 1. Nguyên tắc

- Dùng token trong `ResourceDictionary`; không đặt màu, font, padding hoặc control template lặp lại trực tiếp trong từng view.
- Mọi `Button`, `ToggleButton`, `TextBox`, `ComboBox`, `CheckBox`, `RadioButton`, `ListBox`, `DataGrid`, `TabControl`, `ScrollBar` và `ToolTip` phải có implicit style hoặc named variant dựa trên base style. Không để control WPF mặc định chưa style.
- Chữ thường phải đạt tương phản tối thiểu `4.5:1`, chữ lớn và icon thiết yếu tối thiểu `3:1`. Focus, trạng thái và validation không chỉ dựa vào màu.
- Không dùng chữ trắng trên xanh trung bình. Chữ trắng chỉ dùng trên nền xanh đậm đã kiểm tra; xanh sáng/trung bình phải dùng chữ xanh-đen.
- Chuyển động chỉ dùng cho feedback ngắn 120–180 ms; tôn trọng cài đặt giảm chuyển động của Windows.
- Dấu ấn thị giác duy nhất là **launch-group rail**: vạch dẫn 3 px, mã nhóm và trạng thái dạng chữ/icon liên kết các dòng của cùng một lần chạy. Các phần còn lại giữ trung tính.

## 2. Color tokens

### Light

| Vai trò | Token | Giá trị | Cặp chữ chuẩn |
| --- | --- | --- | --- |
| Nền ứng dụng | `CanvasColor` | `#F3F6F8` | `#16232D` |
| Surface chính | `SurfaceColor` | `#FFFFFF` | `#16232D` — 16.00:1 |
| Surface phụ | `SurfaceAltColor` | `#EAF0F4` | `#16232D` |
| Chữ chính | `TextPrimaryColor` | `#16232D` | — |
| Chữ phụ | `TextSecondaryColor` | `#475866` | trên trắng — 7.35:1 |
| Viền | `BorderColor` | `#C8D3DB` | — |
| Primary | `PrimaryColor` | `#CFEAF5` | `#08354A` — chữ tối trên nền xanh sáng |
| Selected | `SelectedColor` | `#D9EEF7` | `#08354A` — 10.83:1 |
| Focus | `FocusColor` | `#C2410C` | dùng làm ring, không làm nền chữ nhỏ |
| Danger | `DangerColor` | `#B42318` | trắng — 6.57:1 |
| Disabled | `DisabledSurfaceColor` | `#E2E8EC` | `#6B7780` — 3.71:1 |

Status dùng cả glyph + nhãn: Success `#146C43/#DDF4E8`, Warning `#8A4B00/#FFF1CC`, Error `#B42318/#FDE7E5`, Running `#0B5C7A/#D9EEF7`, Neutral `#52606D/#EAF0F4`.

### Dark

| Vai trò | Nền | Chữ |
| --- | --- | --- |
| Canvas | `#11181D` | `#F3F7F9` |
| Surface | `#182229` | `#F3F7F9` — 14.99:1 |
| Surface phụ | `#202D35` | `#D8E2E7` |
| Viền | `#384A55` | — |
| Primary | `#87C9E8` | `#082633` — 8.65:1 |
| Selected | `#234C60` | `#F3F7F9` |
| Focus | `#FFB86B` | ring |
| Danger | `#FF8A80` | `#3B0804` — 7.53:1 |

Không giảm opacity của toàn hàng để biểu diễn trạng thái terminal. Disabled dùng token riêng cho background, foreground và border.

## 3. Typography, spacing và kích thước

- Font UI: `Segoe UI`; dữ liệu index, ID, command và log: `Cascadia Mono`, fallback `Consolas`. Không thêm font tải từ Internet.
- Cỡ chữ: caption `12`, body/data `13`, label/button `13`, section `16`, window title `22`; line height tương ứng khoảng `16/18/20/28`.
- Weight: Regular 400 cho nội dung, Semibold 600 cho tiêu đề và hành động; không dùng Bold cho toàn bảng.
- Spacing scale duy nhất: `4, 8, 12, 16, 24`. Khoảng giữa control cùng nhóm `8`; giữa nhóm `16`; margin cửa sổ `16`.
- Padding: input `10,6`; button `12,6`; card `12`; dialog/card chính `16`.
- Chiều cao: compact control `30`, control chuẩn `34`, primary toolbar `36`, DataGrid row `32`, header `34`, tab `36`, status badge `22`. Hit area icon-only tối thiểu `30×30` và có `AutomationProperties.Name` + tooltip.

## 4. Component rules

### Button

- `PrimaryButton`: nền Primary, chữ OnPrimary; hover tối hơn ở light hoặc sáng hơn ở dark; pressed thay đổi rõ 8–12%; focus ring 2 px bên ngoài.
- `SecondaryButton`: surface phụ, chữ chính, viền; hover tăng tương phản nền/viền. Đây là mặc định cho thao tác thường.
- `DangerButton`: nền Danger và chữ tương phản; thao tác xóa dữ liệu hoặc kết quả vẫn cần xác nhận.
- `DisabledButton`: dùng disabled tokens, giữ nhãn đọc được, không hover/pressed và không dùng opacity cho cả visual tree.
- Icon phải đi cùng nhãn với hành động không phổ quát. Không dùng emoji làm icon.

### DataGrid

- Luôn bật row/column virtualization, recycling và scrolling theo pixel cho danh sách lớn; không bọc `DataGrid` trong `ScrollViewer` ngoài.
- Header cố định, row cao 32, zebra rất nhẹ; số/index canh phải hoặc dùng monospace. Hover và selected phải khác nhau; focus cell/row nhìn thấy bằng ring 2 px.
- Bulk selection có checkbox header, action bar hiện số mục chọn và hành động hàng loạt. Empty/loading/error là row/panel riêng, không để bảng trắng không giải thích.

### Tab, badge và group card

- Tab là điều hướng cấp trang: header 36, selected có text đậm + indicator 2 px; hover không dịch chuyển layout; focus có ring.
- Status badge cao 22, padding ngang 8, corner radius 11; luôn có glyph + text, không chỉ chấm màu.
- Group card dùng surface, viền 1 px, corner radius 6, padding 12. Launch group card thêm group rail 3 px và header chứa mã nhóm, script, thời gian, tổng trạng thái cùng hành động dừng nhóm.

## 5. Interaction states

- `Hover`: đổi surface/viền, không đổi kích thước hay font weight gây layout shift.
- `Selected`: dùng selected surface + text đậm vừa + indicator; không dùng xanh trung bình với chữ trắng.
- `Focus`: ring 2 px màu Focus, `FocusVisualStyle` dùng chung, tab order khớp thứ tự nhìn; mọi chức năng có đường bàn phím.
- `Pressed`: feedback tức thời bằng tone nền/viền; không animation width/height.
- `Disabled`: nhãn vẫn đọc được, nêu lý do bằng tooltip khi hữu ích; không trông giống selected hoặc terminal.
- `Validation/Error`: viền + glyph + thông báo cạnh field; không chỉ đổi màu viền.

## 6. Bố cục cửa sổ

### MainWindow — editor workspace

- Thanh trên: đường dẫn/kết nối MEmu, instance focus cho preview/capture và nút mở Control Center.
- Các control trên thanh đầu dùng cùng chiều cao chuẩn `34`, căn giữa theo trục dọc; tên instance và trạng thái chạy nằm ở hai cột nội dung riêng, dùng ellipsis + tooltip cho tên dài.
- Thân: danh sách kịch bản `280–320 px` | bảng bước co giãn | inspector `340–400 px`; splitter có vùng kéo tối thiểu 8 px.
- Header bảng bước tách tiêu đề và trạng thái clipboard thành hai cột `Auto`/`*`; trạng thái chỉ dùng mẫu gọn `Clipboard: X bước từ “Tên kịch bản”`, không giữ hướng dẫn phím tắt dài thường trực.
- Command preview nằm cuối inspector và có thể thu gọn. MainWindow không chứa bản sao cấu hình chạy đa máy hoặc bảng execution đầy đủ.
- Thanh trạng thái đáy chỉ hiển thị kết nối, dirty/save và số group active; thông báo thao tác/lỗi nằm ở dòng trạng thái gọn bên dưới.

### Control Center — operations workspace

- Tab cấp một duy nhất trong phase hiện tại: `Đang hoạt động`.
- `Chạy`: cột cấu hình/target bên trái; group/runtime ở phần co giãn bên phải. Mỗi launch group là card mặc định thu gọn; header giữ tên/mã, mô tả, các bộ đếm và lệnh dừng. Chỉ card được mở mới materialize bảng instance virtualized/recycling; không giữ panel full log lớn thường trực và không nhân đôi ở MainWindow.
- `Kết quả lần chạy gần nhất`: card gọn trong tab `Đang hoạt động`, hiển thị tổng hợp và chỉ liệt kê instance thất bại/đã hủy; không tạo panel full log hoặc danh sách nhiều phiên.
- Tại chiều rộng nhỏ, panel chi tiết xuống hàng hoặc thu gọn; không ép ba cột cố định gây cắt nội dung.

## 7. Mật độ cho 30–60 giả lập

- Dùng DataGrid ảo hóa, filter và bulk action; không dùng `WrapPanel` hoặc danh sách card cao cho toàn bộ 60 mục.
- Cột ưu tiên: chọn, index, tên, trạng thái, script, thao tác. Cột ít dùng đưa vào details hoặc menu.
- Selection được giữ theo instance index khi refresh/filter; action bar luôn hiển thị số mục đang chọn.
- Không dùng font dưới 12, row dưới 30 hoặc icon-only hàng loạt để tăng mật độ.

## 8. WPF implementation guardrails

- Tách dictionaries tối thiểu thành Colors, Typography, Controls, DataGrid và Tabs; merge từ `App.xaml` để secondary window nhận cùng resource.
- Named variant phải `BasedOn` base style. Template giữ `AutomationProperties`, keyboard focus, validation adorner và disabled behavior.
- Mọi raw color/font/spacing mới trong view phải được đưa về token, trừ visual đặc thù one-off đã có lý do trong comment hoặc design review.
- Visual acceptance bắt buộc ở light/dark, 1280×720 trở lên, keyboard-only, Windows scaling 100/125/150%, và trạng thái empty/loading/error/disabled/executing.

Resource triển khai bắt buộc nằm trong `Themes/Colors.*.xaml`, `Typography.xaml`, `Controls.xaml`, `DataGrid.xaml`, `Tabs.xaml` và được merge từ `App.xaml`. Named styles chuẩn là `PrimaryButtonStyle`, `SecondaryButtonStyle`, `DangerButtonStyle`, `ToolbarButtonStyle`, `DataGridStyle`, `TabStyle`, `StatusBadgeStyle`, `GroupCardStyle`; view không tự tạo biến thể màu chữ trắng trên xanh trung bình.

Runtime visual acceptance của redesign phải được kiểm tra sau automated verification: Control Center ở normal/maximized, dropdown script chung và script từng máy, active/latest result, checkbox và bulk action trên danh sách 30–60 máy đại diện. Automated XAML test không thay thế kiểm tra contrast, clipping, keyboard focus và DPI thực tế. Overlay lấy tọa độ vẫn phải được kiểm tra riêng cho viewport, resize, letterbox và DPI mà không thay đổi geometry cửa sổ MEmu.
