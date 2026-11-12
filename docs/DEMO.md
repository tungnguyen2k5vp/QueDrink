# Demo QueDrink trong 6–8 phút

## Chuẩn bị

Chạy `start-demo.ps1` theo README. Mở web khách và quản trị ở hai tab cùng trình duyệt. Đăng ký một tài khoản khách thử để có lịch sử và điểm. Không dùng thông tin cá nhân thật để demo.

## Trình tự trình bày

1. **Chọn món:** tìm `tra sua`, lọc theo giá, mở món. Đổi size L, 50% đường, ít đá, thêm trân châu. Giá mỗi ly đổi theo lựa chọn. Đây là giá tạm tính của giao diện.
2. **Giỏ hàng:** tăng số lượng lên 2 để đủ 50.000đ. Chọn nhận tại quán, nhập tên và số điện thoại thử; nhập `WELCOME10`. Tổng xác nhận đến từ API, không lấy giá do trình duyệt gửi lên.
3. **Đặt COD:** bấm xác nhận. Lưu mã đơn, mở theo dõi. Bấm tải lại trang giỏ vẫn có thể xem biên nhận gần nhất trong phiên. Yêu cầu đặt lại do mạng chập chờn dùng cùng Idempotency-Key để nhận cùng đơn.
4. **Quản trị:** tìm mã đơn vừa tạo, chuyển `Xác nhận → Pha chế → Sẵn sàng → Hoàn thành`. Đơn nhận tại quán không đi qua bước giao hàng. Trở lại tab theo dõi để thấy cập nhật trực tiếp.
5. **Sau mua:** mở tài khoản, xem đơn và điểm; đánh giá món từ đơn hoàn thành. Mỗi người chỉ đánh giá một món một lần trong một đơn. Xem đánh giá ở cửa sổ chọn món.
6. **Thanh toán:** chưa có merchant thì dùng MOCK và quản trị bấm “Mô phỏng đã trả tiền”. Khi đã có khóa sandbox của bạn, checkout mở VNPay sandbox; chỉ IPN hợp lệ cập nhật đơn. Xem [cách cấu hình Gmail/VNPay](GMAIL-VNPAY.md). Không nhận mình đã chạy thanh toán live nếu chưa nghiệm thu bằng tài khoản thật.
7. **Quản lý thực đơn:** sửa giá, tạm ẩn món, thêm topping và gán vào món. Tạo voucher giới hạn 1 lượt. Hai đơn đồng thời chỉ một đơn được dùng mã; hủy đơn chưa pha sẽ trả lại lượt.

## Những câu cần tự giải thích được

| Câu hỏi | Ý chính và chỗ đọc |
|---|---|
| Một lần đặt món chạy qua những đâu? | Next.js gửi JSON qua `/api/v1`, rewrite chuyển sang ASP.NET, EF đọc PostgreSQL và trả biên nhận. `frontend/lib/api.ts`, `Orders.cs`. |
| Vì sao sửa giá trong trình duyệt không làm đơn rẻ đi? | API chỉ nhận mã món, mã lựa chọn, số lượng. `Checkout.Price` đọc giá thật từ DB rồi tính lại. |
| CRUD, LINQ và quan hệ bảng ở đâu? | `Catalog.cs` quản lý món; `Data.cs` ánh xạ category → products, product → sizes và liên kết topping. Dùng `Where`, `Include`, `Select`, `Sum`. |
| Vì sao không đặt trùng? | Mã yêu cầu + hash nội dung, khóa giao dịch PostgreSQL và unique index. Cùng mã nhưng nội dung khác trả 409. |
| Hai người cùng dùng lượt voucher cuối thì sao? | Khóa dòng voucher bằng `FOR UPDATE`, kiểm tra lượt rồi tăng trong cùng transaction tạo đơn. Nếu thất bại thì rollback. |
| Vì sao không thể hoàn thành ngay đơn mới? | `Rules.Next` xác định các bước cho phép; API kiểm tra lại. Frontend chỉ hiển thị những bước API trả về. |
| Đăng nhập, phân quyền khác nhau thế nào? | JWT xác nhận phiên; role ADMIN cho phép endpoint quản trị. Đổi mật khẩu tăng session_version và thu hồi refresh token. |
| Xử lý thanh toán thế nào? | Tạo giao dịch riêng; callback HMAC ràng buộc mã, đơn, số tiền, trạng thái. Thành công không bị callback FAILED ghi đè. Chưa có provider thật. |
| Kiểm thử được gì? | Self-test quy tắc, kiểm tra HTTP có DB thật, test giỏ hàng và luồng thao tác trên trình duyệt. Đọc `VERIFICATION.md`. |

## Cách học để trình bày trung thực

Đầu tiên tự chạy và giải thích một đường đi: chọn món → báo giá → tạo đơn → xác nhận → hoàn thành. Sau đó tự đổi một giá size, thêm điều kiện voucher và chạy lại check. Chỉ ghi trong CV các phần bạn đã tự đọc, sửa và kiểm tra được; có thể nói dự án phát triển có AI hỗ trợ. Không nhận mình tự thiết kế toàn bộ bảo mật hoặc tích hợp cổng thanh toán thật.
