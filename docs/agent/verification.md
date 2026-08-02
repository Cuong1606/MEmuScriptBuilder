# Verification and Definition of Done

Đọc tài liệu này trước khi kết luận một nhiệm vụ, giai đoạn hoặc MVP đã hoàn thành.

## 1. Vòng lặp bắt buộc

```text
Gather context
→ Plan
→ Implement
→ Build and test
→ Review
→ Fix
→ Retest
→ Complete
```

Không bỏ qua một bước áp dụng được mà không ghi rõ lý do và trạng thái.

## 2. Trạng thái verification

Mọi build, test hoặc kiểm tra phải dùng đúng một trong bốn trạng thái:

- `passed`: lệnh thực sự đã chạy, exit code cho biết thành công và kết quả đáp ứng kỳ vọng.
- `failed`: lệnh đã chạy nhưng exit code hoặc kết quả không đáp ứng kỳ vọng.
- `not run`: chưa chạy. Không được suy diễn kết quả.
- `blocked`: không thể chạy do một điều kiện cụ thể ngoài kết quả của code; phải nêu blocker và bằng chứng.

Không dùng từ “passed”, “thành công”, “hoàn thành” hoặc cách diễn đạt tương đương nếu bằng chứng không hỗ trợ kết luận đó.

## 3. Quy tắc bằng chứng

Mỗi kết luận verification phải ghi:

- Lệnh hoặc thao tác kiểm tra đã dùng.
- Exit code nếu là lệnh terminal.
- Kết quả quan sát được.
- Phạm vi mà bằng chứng thực sự chứng minh.

Không tuyên bố hoàn thành nếu chưa build thành công. Không tuyên bố test passed nếu test chưa thực sự chạy.

Nếu build/test không phù hợp với thay đổi chỉ-tài-liệu, ghi `not run` và lý do; chỉ được kết luận nhiệm vụ tài liệu hoàn thành sau khi kiểm tra nội dung, liên kết và phạm vi file.

## 4. Sửa và kiểm tra lại

- Không xóa, bỏ qua, vô hiệu hóa hoặc làm yếu test để nhận kết quả xanh.
- Tối đa 3 vòng sửa–kiểm tra cho cùng một vấn đề.
- Mỗi vòng phải tạo ra thay đổi hoặc bằng chứng mới có ý nghĩa.
- Nếu sau 3 vòng không tiến triển, dừng và báo blocker, các thử nghiệm đã thực hiện và bước cần người dùng hoặc môi trường hỗ trợ.
- Sau khi sửa lỗi do review/build/test phát hiện, phải chạy lại kiểm tra liên quan; kết quả cũ không còn đủ để kết luận.

## 5. Review trước khi hoàn tất

- Kiểm tra compiler error và warning liên quan.
- Xem lại diff và xác nhận không có thay đổi ngoài phạm vi.
- Kiểm tra hành vi lỗi, cancellation, timeout và dữ liệu nhạy cảm nếu có liên quan.
- Đối chiếu tiêu chí chấp nhận trong [`../product-spec.md`](../product-spec.md).
- Ghi lại file tạo/sửa, lệnh, exit code, kết quả và phần chưa kiểm tra.

## 6. Test bắt buộc theo phạm vi

Unit test tối thiểu của dự án phải bao phủ:

- Tạo tham số MEMUC.
- Escape đường dẫn và tham số.
- Thay thế biến.
- Phát hiện biến thiếu.
- Chuyển từng loại bước thành lệnh.
- Parse kết quả `memuc listvms`.
- Dừng kịch bản khi một bước lỗi.
- Tiếp tục khi bật “continue on error”.
- Hủy kịch bản.
- Lưu và đọc JSON.
- Nâng cấp dữ liệu JSON theo version nếu có migration.

Không cần chạy MEmu thật trong unit test. Phải dùng abstraction của process runner để mock kết quả.

## 7. Smoke test với MEmu thật

- Test tự động không thay thế smoke test với MEmu thật.
- Không được nói tích hợp MEmu đã hoạt động nếu chưa chạy trên MEmu thật.
- Nếu chưa có MEmu hoặc `memuc.exe`, ghi smoke test là `not run` hoặc `blocked`, không ghi `passed`.
- Bằng chứng smoke test phải nêu máy ảo mục tiêu, thao tác đã chạy, exit code và kết quả quan sát; không ghi dữ liệu bí mật.
- Smoke test không được xóa/reset máy ảo hoặc thực hiện lệnh ngoài phạm vi được người dùng cho phép.

## 8. Definition of Done cho một thay đổi

Một thay đổi source chỉ hoàn thành khi:

1. Phạm vi và tiêu chí chấp nhận đã được đối chiếu.
2. Code liên quan đã được implement và review.
3. Solution build thành công.
4. Các test liên quan và test suite áp dụng được đã thực sự chạy và passed.
5. Các lỗi phát hiện đã được sửa và retest.
6. Không có thay đổi ngoài phạm vi trong diff.
7. Tài liệu, project state và decision log đã cập nhật nếu cần.
8. Các kiểm tra chưa thể chạy được ghi rõ là `not run` hoặc `blocked`.

Một thay đổi chỉ-tài-liệu không yêu cầu build không liên quan, nhưng phải đọc lại toàn bộ file sửa/tạo, kiểm tra cấu trúc/liên kết/phạm vi và ghi build/test là `not run`.

## 9. Definition of Done cho giai đoạn và MVP

- Mỗi giai đoạn trong [`workflow.md`](workflow.md) phải build và test thành công trước khi chuyển sang giai đoạn tiếp theo.
- MVP phải đạt toàn bộ tiêu chí trong phần “Tiêu chí chấp nhận MVP” của [`../product-spec.md`](../product-spec.md).
- Các tiêu chí vận hành MEmu chỉ được xác nhận sau smoke test trên MEmu thật.
