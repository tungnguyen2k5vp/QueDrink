# Cấu trúc và quy tắc

```text
Next.js / React
  └─ /api/v1 qua Next rewrites
      └─ ASP.NET Core Minimal API
          └─ Entity Framework Core / Npgsql
              └─ PostgreSQL 16
```

## Các file backend

| File | Trách nhiệm |
|---|---|
| Program.cs | Cấu hình DI, JWT, phân quyền, CORS, rate limit, lỗi và khởi tạo EF migration |
| Data.cs | Entities, khóa ngoại, ánh xạ tên bảng/cột của schema Java cũ |
| Identity.cs | Đăng nhập, BCrypt, JWT, refresh rotation, logout, mật khẩu và token theo dõi |
| Catalog.cs | Danh mục, món/lựa chọn, topping, voucher, yêu thích, đánh giá và điểm |
| Checkout.cs | Đọc giá, xác thực lựa chọn thuộc món, voucher, phí nhận/giao và khóa transaction |
| Orders.cs | Báo giá, tạo/tra cứu/hủy đơn, SSE, xử lý đơn quản trị và báo cáo |
| Payments.cs / VnPay.cs | MOCK hoặc VNPay sandbox/live, ký URL, xác minh IPN, chống ghi nhận lặp và lưu giao dịch cần đối soát |
| Gmail.cs | Gmail SMTP STARTTLS, thư reset và gửi thử về chính tài khoản gửi |
| Rules.cs | Kiểm tra đầu vào, số điện thoại, tiền VND, điều kiện voucher và trạng thái |

Frontend: `lib/api.ts` giữ kiểu dữ liệu, gọi API và gia hạn phiên; `lib/cart.ts` kiểm tra dữ liệu giỏ từ localStorage. Các route `menu/cart/orders/account` cho khách, `admin` xử lý đơn và `admin/manage` quản lý dữ liệu. Modal dùng `<dialog>` với focus/ESC của trình duyệt; form có nhãn, focus bàn phím và giao diện nhỏ.

## Quy tắc tiền và đơn hàng

- Dùng số nguyên VND (`long`), không tính tiền bằng số thực. Giá ly = giá ưu đãi hoặc giá gốc + giá tăng của size + topping. Nhân số lượng và cộng các dòng.
- Mỗi dòng 1–20 ly, tối đa 30 dòng và 5 topping khác nhau. Lựa chọn phải đang phục vụ và thuộc đúng món.
- Một voucher giảm trên tiền món; giảm không vượt tiền món và mức trần. Phí giao mặc định 10.000đ, miễn khi tiền món đạt 150.000đ. Nhận tại quán không tính phí giao. Các giá trị phí do cấu hình server quyết định.
- Tạo đơn, giữ lượt voucher và lưu dòng hàng cùng transaction. Khóa idempotency ngăn hai yêu cầu cùng mã tạo hai đơn. Dòng hàng lưu snapshot tên, giá, size và topping; sửa món sau này không sửa đơn cũ.
- Size cùng tên giữ ID khi sửa giá. Size bị xóa/đổi tên khiến lựa chọn cũ hết hiệu lực và được yêu cầu chọn lại.
- Luồng giao: `PENDING → CONFIRMED → PREPARING → READY → DELIVERING → COMPLETED`. Luồng nhận tại quán bỏ `DELIVERING`.
- Chỉ hủy khi còn PENDING/CONFIRMED và chưa trả tiền. Chỉ từ chối ở bước tiếp nhận. Sau pha chế không cho hủy tự động. Đơn đã trả tiền cần quy trình đối soát/hoàn tiền riêng, chưa triển khai.
- COD ghi PAID khi COMPLETED. Đơn ngân hàng chưa trả tiền không được xác nhận bằng thao tác đổi trạng thái. Đơn tổng 0 được xác nhận đã trả ngay.
- Điểm chỉ được cộng một lần khi đơn của tài khoản hoàn thành. Doanh thu chỉ cộng đơn COMPLETED + PAID. Đánh giá yêu cầu đúng tài khoản, đúng món thuộc đơn đã hoàn thành và đã trả tiền.

## Phiên và cập nhật đồng thời

JWT sống 15 phút; refresh token 30 ngày, chỉ lưu hash trong DB và đổi token mỗi lần gia hạn. Phiên bị thu hồi sau logout/đổi mật khẩu không tiếp tục gọi API được. Frontend lưu phiên trong sessionStorage, tự gia hạn khi API trả 401 và chỉ dùng một yêu cầu refresh khi nhiều request đồng thời.

Admin cập nhật với expectedVersion; transaction khóa dòng đơn rồi kiểm tra phiên bản. Tạo/hủy đơn khóa voucher để không vượt lượt. SSE dùng token ký, không đặt số điện thoại trong URL; thăm DB mỗi 2 giây và nối lại sau 2 phút. Đây là giới hạn thiết kế cho demo/một cửa hàng nhỏ; cần notification/broker nếu mở rộng lớn.
