# MEmu Script Studio — WPF UI Design System

Tài liệu này là nguồn chuẩn cho UI/XAML của ứng dụng productivity/operations native Windows WPF. Mục tiêu là một bàn điều hành gọn, rõ và đủ dày để quản lý 30–60 giả lập; không thay đổi chức năng trong [`product-spec.md`](product-spec.md). Source hiện tại chỉ merge `Colors.Light.xaml`; dark mode không thuộc current MVP.

## 1. Nguyên tắc

- Dùng token trong `ResourceDictionary`; không đặt màu, font, padding hoặc control template lặp lại trực tiếp trong từng view.
- Mọi `Button`, `ToggleButton`, `TextBox`, `ComboBox`, `CheckBox`, `RadioButton`, `ListBox`, `DataGrid`, `TabControl`, `ScrollBar` và `ToolTip` phải có implicit style hoặc named variant dựa trên base style. Không để control WPF mặc định chưa style.
- Chữ thường phải đạt tương phản tối thiểu `4.5:1`, chữ lớn và icon thiết yếu tối thiểu `3:1`. Focus, trạng thái và validation không chỉ dựa vào màu.
- Không dùng chữ trắng trên xanh trung bình. Chữ trắng chỉ dùng trên nền xanh đậm đã kiểm tra; xanh sáng/trung bình phải dùng chữ xanh-đen.
- Chuyển động chỉ dùng cho feedback ngắn 120–180 ms; tôn trọng cài đặt giảm chuyển động của Windows.
- Giữ visual trung tính, ưu tiên hierarchy và khả năng quét bảng. UI hiện tại không có launch-group card/rail.

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

Status luôn có nhãn text; glyph là optional và màu không được là tín hiệu duy nhất. Palette hiện hành: Success `#146C43/#DDF4E8`, Warning `#8A4B00/#FFF1CC`, Error `#B42318/#FDE7E5`, Running `#0B5C7A/#D9EEF7`, Neutral `#52606D/#EAF0F4`.

Không giảm opacity của toàn hàng để biểu diễn trạng thái terminal. Disabled dùng token riêng cho background, foreground và border. Nếu dark mode được đưa lại vào scope trong tương lai, palette/template/contrast và runtime switching phải được thiết kế và verify như một feature mới; không suy ra support từ tài liệu cũ.

## 3. Typography, spacing và kích thước

- Font UI: `Segoe UI`; dữ liệu index, ID, command và log: `Cascadia Mono`, fallback `Consolas`. Không thêm font tải từ Internet.
- Cỡ chữ: caption `12`, body/data `13`, label/button `13`, section `16`, window title `22`; line height tương ứng khoảng `16/18/20/28`.
- Weight: Regular 400 cho nội dung, Semibold 600 cho tiêu đề và hành động; không dùng Bold cho toàn bảng.
- Spacing scale duy nhất: `4, 8, 12, 16, 24`. Khoảng giữa control cùng nhóm `8`; giữa nhóm `16`; margin cửa sổ `16`.
- Padding: input `10,6`; button `12,6`; card `12`; dialog/card chính `16`.
- Chiều cao: compact control `30`, control chuẩn `34`, primary toolbar `36`, DataGrid row `32` hoặc `36` tùy surface hiện hành, header `34`, tab `36`, status badge `22`. Hit area icon-only tối thiểu `30×30` và có `AutomationProperties.Name` + tooltip.

## 4. Component rules

### Button

- `PrimaryButton`: nền Primary, chữ OnPrimary; hover/pressed thay đổi tone rõ 8–12%; focus ring 2 px bên ngoài.
- `SecondaryButton`: surface phụ, chữ chính, viền; hover tăng tương phản nền/viền. Đây là mặc định cho thao tác thường.
- `DangerButton`: nền Danger và chữ tương phản; thao tác xóa dữ liệu hoặc kết quả vẫn cần xác nhận.
- `DisabledButton`: dùng disabled tokens, giữ nhãn đọc được, không hover/pressed và không dùng opacity cho cả visual tree.
- Icon phải đi cùng nhãn với hành động không phổ quát. Không dùng emoji làm icon.

### DataGrid

- Luôn bật row/column virtualization và recycling cho danh sách lớn; chọn content/pixel scrolling có chủ đích và không bọc `DataGrid` trong `ScrollViewer` ngoài.
- Header cố định; row dùng 32 hoặc 36 theo surface. Không bắt buộc zebra; hover, selected và focus phải phân biệt rõ, số/index canh phải hoặc dùng monospace khi phù hợp.
- Bulk selection hiện dùng checkbox từng row cùng action bar/số mục chọn; header checkbox chỉ thêm khi semantics của bảng phù hợp. Empty/loading/error là row/panel riêng, không để bảng trắng không giải thích.

### Tab, badge và group card

- Tab là điều hướng cấp trang: header 36, selected có text đậm + indicator 2 px; hover không dịch chuyển layout; focus có ring.
- Status badge cao 22, padding ngang 8, corner radius 11; luôn có text, glyph là optional và không được chỉ dùng màu/chấm màu.
- Group card dùng surface, viền 1 px, corner radius 6, padding 12. Control Center hiện tại không trình bày launch group thành card; runtime active luôn dùng bảng phẳng.

## 5. Interaction states

- `Hover`: đổi surface/viền, không đổi kích thước hay font weight gây layout shift.
- `Selected`: dùng selected surface + text đậm vừa + indicator; không dùng xanh trung bình với chữ trắng.
- `Focus`: ring 2 px màu Focus, `FocusVisualStyle` dùng chung, tab order khớp thứ tự nhìn; mọi chức năng có đường bàn phím.
- `Pressed`: feedback tức thời bằng tone nền/viền; không animation width/height.
- `Disabled`: nhãn vẫn đọc được, nêu lý do bằng tooltip khi hữu ích; không trông giống selected hoặc terminal.
- `Validation/Error`: viền + glyph + thông báo cạnh field; không chỉ đổi màu viền.

## 6. Bố cục cửa sổ

### MainWindow — editor workspace

- Thanh trên: thiết bị soạn thảo cho preview/capture, làm mới và nút mở Control Center luôn hiện; đường dẫn `memuc.exe`/`adb.exe` cùng thao tác cấu hình ít dùng nằm trong khu vực “Kết nối / Cài đặt thiết bị” thu gọn.
- Các control trên thanh đầu dùng cùng chiều cao chuẩn `34`, căn giữa theo trục dọc; tên thiết bị và trạng thái sẵn sàng nằm ở hai cột nội dung riêng trong bộ chọn, dùng ellipsis + tooltip cho tên dài.
- Thân: ba cột Star cho thư viện kịch bản | bảng bước | inspector, với tỷ lệ mặc định `5:8:7` và MinWidth lần lượt `240/340/320` DIPs. Hai `GridSplitter` 8 DIPs resize `PreviousAndNext` theo hai hướng, không cho Steps/Properties collapse về 0; double-click trả về tỷ lệ mặc định. Đây là minimum usability hiện hành, không phải fixed total width hay MaxWidth sản phẩm.
- Header bảng bước tách tiêu đề và trạng thái clipboard thành hai cột `Auto`/`*`; trạng thái chỉ dùng mẫu gọn `Clipboard: X bước từ “Tên kịch bản”`, không giữ hướng dẫn phím tắt dài thường trực.
- Command preview nằm cuối inspector và có thể thu gọn. MainWindow không chứa bản sao cấu hình chạy đa máy hoặc bảng execution đầy đủ.
- Thanh trạng thái đáy chỉ hiển thị kết nối, dirty/save và số group active; thông báo thao tác/lỗi nằm ở dòng trạng thái gọn bên dưới.

### Control Center — operations workspace

- Hai tab cấp một: `Đang hoạt động` và `Kết quả gần đây`.
- Tab active: cột cấu hình/target bên trái; runtime dùng toàn bộ chiều cao phần co giãn bên phải. Mọi instance active nằm trong một DataGrid phẳng virtualized/recycling, có search index/tên/script và filter trạng thái; không tạo card theo launch group, không giữ panel full log lớn thường trực và không nhân đôi ở MainWindow.
- Tab recent: danh sách phía trên và detail bounded của lượt chọn phía dưới dùng native row `GridSplitter`, MinHeight lần lượt 140/160 DIPs và Star ratio được restore sau Loaded; giữ tối đa 20 snapshot RAM newest-first và không tạo full log viewer. Cột chính dùng `RunDescription` với nhãn `Kịch bản / lần chạy`, không nhấn mạnh launch group.
- Cột thiết lập chạy và runtime được ngăn bằng native `GridSplitter` resize realtime, có grip luôn nhìn thấy, vùng hover rõ và MinWidth 360/300 DIPs. Hai cột giữ Star sizing; không có custom DragDelta/SizeChanged clamp. Double-click chỉ trả về Star ratio mặc định. Ở chiều cao nhỏ, hai nhóm cấu hình chạy có thể thu gọn để giữ viewport target; cửa sổ không dùng MinHeight 720 cứng.
- Tại chiều rộng nhỏ, giữ hành động quan trọng reachable bằng sizing/scroll hợp lý. Workaround extent của Active Instances hiện còn là known issue trong `project-state.md`, không phải mẫu để sao chép.

## 7. Mật độ cho 30–60 giả lập

- Dùng DataGrid ảo hóa, filter và bulk action; không dùng `WrapPanel` hoặc danh sách card cao cho toàn bộ 60 mục.
- Cột ưu tiên: chọn, index, tên, trạng thái, script, thao tác. Cột ít dùng đưa vào details hoặc menu.
- Selection được giữ theo instance index khi refresh/filter; action bar luôn hiển thị số mục đang chọn.
- Không dùng font dưới 12, row dưới 30 hoặc icon-only hàng loạt để tăng mật độ.

## 8. WPF implementation guardrails

- Tách dictionaries tối thiểu thành Colors, Typography, Controls, DataGrid và Tabs; merge từ `App.xaml` để secondary window nhận cùng resource.
- Named variant phải `BasedOn` base style. Template giữ `AutomationProperties`, keyboard focus, validation adorner và disabled behavior.
- Mọi raw color/font/spacing mới trong view phải được đưa về token, trừ visual đặc thù one-off đã có lý do trong comment hoặc design review.
- Visual acceptance bắt buộc ở light theme, 1280×720 trở lên, keyboard-only, Windows scaling 100/125/150%, và trạng thái empty/loading/error/disabled/executing.

Resource hiện hành nằm trong `Themes/Colors.Light.xaml`, `Typography.xaml`, `Controls.xaml`, `DataGrid.xaml`, `Tabs.xaml` và được merge từ `App.xaml`. Named styles chuẩn là `PrimaryButtonStyle`, `SecondaryButtonStyle`, `DangerButtonStyle`, `ToolbarButtonStyle`, `DataGridStyle`, `TabStyle`, `StatusBadgeStyle`, `GroupCardStyle`; view không tự tạo biến thể màu chữ trắng trên xanh trung bình.

Runtime visual acceptance của redesign phải được kiểm tra sau automated verification: Control Center ở normal/maximized/restore, dropdown script chung và script từng máy, hai tab active/recent, splitter ngang, checkbox enabled/disabled và bulk action trên danh sách 30–60 máy đại diện tại scaling 100/125/150%. Automated XAML test không thay thế kiểm tra contrast, clipping, keyboard focus và DPI thực tế. Overlay lấy tọa độ vẫn phải được kiểm tra riêng cho viewport, resize, letterbox và DPI mà không thay đổi geometry cửa sổ MEmu.
