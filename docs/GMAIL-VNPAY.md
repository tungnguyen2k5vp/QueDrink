# Bật Gmail và VNPay

Mã nguồn đã có Gmail SMTP và VNPay 2.1.0, hỗ trợ sandbox và live. Hiện chưa cấu hình tài khoản của bạn: chưa gửi email qua Gmail và chưa phát sinh giao dịch tiền thật. Không dùng khóa mẫu để nhận tiền.

## 1. Bật Gmail trước

1. Dùng Gmail riêng cho cửa hàng. Bật [Xác minh 2 bước](https://myaccount.google.com/security) cho tài khoản Google.
2. Mở [Mật khẩu ứng dụng](https://myaccount.google.com/apppasswords), tạo mật khẩu cho QueDrink. Google yêu cầu xác minh 2 bước; một số tài khoản trường học/tổ chức hoặc chế độ bảo vệ có thể không cung cấp chức năng này. Xem [hướng dẫn chính thức](https://support.google.com/accounts/answer/185833?hl=vi).
3. Trong PowerShell, chạy:

```powershell
cd D:\ReactJS\QueDrink
powershell -NoProfile -ExecutionPolicy Bypass -File .\configure-integrations.ps1
```

4. Nhập địa chỉ website; dùng `http://127.0.0.1:3006` khi chỉ thử trên chính máy này. Nhập Gmail và App Password 16 ký tự vào ô ẩn. Không dùng mật khẩu tài khoản Google. Chọn **0** cho thanh toán vì bạn chưa có merchant.
5. Script lưu `.local/integrations.json`, giới hạn quyền đọc/ghi theo tài khoản Windows của bạn. File không được đưa lên Git. Script từ chối ghi đè nếu đã có file; những lần sau chỉnh file này trực tiếp tại máy. Không gửi file hoặc chụp nội dung lên chat.
6. Tắt/mở lại demo:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\start-demo.ps1 -Stop
powershell -NoProfile -ExecutionPolicy Bypass -File .\start-demo.ps1
```

7. Đăng nhập quản trị, vào **Kết nối dịch vụ**, bấm **Gửi email thử đến Gmail của quán**. Endpoint chỉ gửi về chính địa chỉ gửi đã cấu hình, không cho nhập người nhận bất kỳ. Kiểm tra cả Spam.
8. Đăng ký tài khoản khách bằng email bạn kiểm soát → đăng xuất → Quên mật khẩu. Email có nút đặt mật khẩu mới, hiệu lực 15 phút. Khi đổi thành công, toàn bộ phiên và mã khôi phục cũ của tài khoản bị thu hồi.

Ứng dụng dùng `smtp.gmail.com:587` với STARTTLS, xác minh chứng chỉ mặc định của .NET và App Password. Xem [cấu hình SMTP của Google](https://developers.google.com/workspace/gmail/imap/imap-smtp). Có timeout 15 giây; lỗi gửi không ghi địa chỉ nhận, mật khẩu hay mã reset vào log. Thông báo khôi phục luôn chung chung để không tiết lộ tài khoản có tồn tại hay không. SMTP chấp nhận thư chưa bảo đảm thư đã đến Inbox.

Gmail hiện phục vụ **khôi phục mật khẩu và thư kiểm tra**, chưa gửi biên nhận đơn hàng hoặc email quảng cáo. Khi cần gửi thông báo đơn với cơ chế thử lại bền vững, bổ sung outbox. Nếu App Password bị Google thu hồi sau khi đổi mật khẩu tài khoản, tạo App Password mới và cập nhật cấu hình.

## 2. Tạo tài khoản VNPay sandbox

1. Mở [hướng dẫn kết nối chính thức VNPay](https://sandbox.vnpayment.vn/apis/docs/gioi-thieu/) và đăng ký thông tin kết nối sandbox theo liên kết của VNPay.
2. Lấy **TmnCode** và **HashSecret** của chính tài khoản sandbox. Đây là hai giá trị khác với tài khoản live.
3. Cần địa chỉ HTTPS công khai cho IPN để thử toàn bộ luồng. Chạy local vẫn tạo được URL sandbox, nhưng VNPay không gọi được IPN vào `127.0.0.1`. Chưa có địa chỉ công khai thì chỉ kiểm chứng nội bộ được phần ký và xử lý callback.
4. Khi có cấu hình, chỉnh `.local/integrations.json` tại máy:

```json
{
  "FRONTEND_URL": "https://ten-mien-cua-ban.vn",
  "GMAIL_ENABLED": true,
  "GMAIL_ADDRESS": "gmail-cua-ban@gmail.com",
  "GMAIL_APP_PASSWORD": "<app-password-rieng>",
  "PAYMENT_PROVIDER": "VNPAY",
  "VNPAY_SANDBOX": true,
  "VNPAY_TMN_CODE": "<ma-sandbox-cua-ban>",
  "VNPAY_HASH_SECRET": "<khoa-sandbox-cua-ban>"
}
```

Đây chỉ là cấu trúc minh họa, không phải cấu hình chạy được. Không ghi đè mật khẩu hiện có bằng nội dung mẫu.

5. Đăng ký IPN với VNPay: `https://ten-mien-cua-ban.vn/api/v1/payments/vnpay/ipn`. Return URL là `https://ten-mien-cua-ban.vn/api/v1/payments/vnpay/return`. Hai địa chỉ cũng hiển thị tại quản trị → Kết nối dịch vụ. Reverse proxy phải chuyển các đường `/api/` tới API.
6. Khởi động lại. Checkout hiển thị **VNPay sandbox — thử nghiệm**. Chỉ dùng dữ liệu thẻ thử nghiệm do VNPay cung cấp, không nhập thông tin thẻ thật. Kiểm tra đơn cập nhật từ IPN ngay cả khi người dùng đóng trang thanh toán.

## 3. Nhận tiền thật

Bạn cần merchant live được VNPay duyệt, thông tin kinh doanh/tài khoản nhận tiền theo yêu cầu của họ, website HTTPS công khai và IPN đăng ký thành công. Thực hiện thủ tục trực tiếp với VNPay; mã nguồn không thay thế được khâu duyệt merchant.

Sau khi được duyệt: dùng cơ sở dữ liệu triển khai riêng, không dùng các đơn/tài khoản giả của demo. Dùng TmnCode/HashSecret **live**, đặt `VNPAY_SANDBOX=false`, giữ `PAYMENT_PROVIDER=VNPAY`, môi trường Production, khởi động lại và thực hiện nghiệm thu theo hướng dẫn VNPay. Ứng dụng dùng cổng live `https://pay.vnpay.vn/vpcpay.html`. Chưa có merchant thì giữ thanh toán trực tuyến **DISABLED** và dùng COD.

Trước khi mở cho khách thật, cần kiểm chứng một giao dịch theo kế hoạch nghiệm thu do bạn/VNPay chấp thuận và kiểm tra tiền trong tài khoản merchant. Công việc hiện tại chưa thực hiện bước này, chưa triển khai website lên Internet và chưa mở tài khoản dịch vụ.

## 4. Luồng xử lý và đối soát

1. API đọc tổng tiền từ đơn, tạo mã giao dịch riêng, nhân VND với 100 theo giao thức và ký HMAC-SHA512. Liên kết có hiệu lực 15 phút, thời gian GMT+7.
2. Khách thanh toán trên VNPay; QueDrink không thu thập số thẻ hoặc OTP.
3. **Return chỉ đưa khách trở lại trang theo dõi**, không cập nhật đã trả tiền. Thông báo trên URL không phải bằng chứng thanh toán.
4. IPN kiểm tra chữ ký, merchant, mã tham chiếu, số tiền, mã phản hồi và trạng thái giao dịch. Khóa đơn/giao dịch trong transaction; lưu mã giao dịch VNPay và chặn dùng trùng mã này.
5. IPN hợp lệ thành công mới đánh dấu PAID và xác nhận đơn. Callback lặp không tăng phiên bản hoặc ghi nhận tiền thêm; lỗi DB trả mã để VNPay thử lại.
6. Tiền đến sau khi đơn hủy: giữ đơn đã hủy, ghi trạng thái cần đối soát. Hai giao dịch thành công cho cùng một đơn: giữ giao dịch thứ hai để đối soát. Quản trị có danh sách riêng, không tự pha lại hoặc hoàn tiền giả.
7. Hoàn tiền, truy vấn chủ động khi mất IPN và đóng hồ sơ đối soát chưa được tự động hóa. Dùng cổng merchant để kiểm tra/hoàn tiền, giữ mã giao dịch làm bằng chứng. Khi khách báo đã bị trừ tiền mà đơn chưa PAID, kiểm tra merchant trước, không hướng dẫn thanh toán lại ngay.

Tài liệu giao thức: [VNPay PAY 2.1.0](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html).

## 5. Cấu hình cho Docker hoặc chạy API riêng

Docker Compose dùng các biến trong `.env.example`: `GMAIL_ENABLED`, `GMAIL_ADDRESS`, `GMAIL_APP_PASSWORD`, `PAYMENT_PROVIDER`, `VNPAY_SANDBOX`, `VNPAY_TMN_CODE`, `VNPAY_HASH_SECRET`, `FRONTEND_URL`. Điền trong file cấu hình riêng tại máy/secret của nền tảng triển khai. Native API có thể đọc file JSON bằng biến `INTEGRATIONS_FILE`; biến môi trường có ưu tiên cao hơn file này. `start-demo.ps1` tự nhận `.local/integrations.json` nếu file tồn tại.

`MOCK` chỉ được phép trong Development và không thể gọi endpoint mô phỏng khi dùng VNPay. Mặc định Production tắt thanh toán trực tuyến nếu chưa chọn provider. Gmail được bật thì API không trả mã reset ra response, kể cả khi tùy chọn demo cũ còn bật.

API chỉ tin forwarded headers từ loopback hoặc IP proxy được khai báo trong `TRUSTED_PROXY_IPS` (phân cách dấu phẩy). Khi Next/reverse proxy nằm ở máy hoặc container khác, khai báo đúng IP proxy tin cậy để VNPay nhận IP khách; không tin mọi địa chỉ hoặc lấy `X-Forwarded-For` trực tiếp từ Internet.

## 6. Những kiểm tra chạy được mà không có tài khoản dịch vụ

- `dotnet run --project backend/QueDrink.Api.csproj -- --self-test`: đối chiếu HMAC với fixture Python, URL encoding, chặn sửa/nhân đôi tham số, nội dung email và escape HTML.
- `backend/check_api.py`: kiểm tra hồi quy ở chế độ MOCK trên DB thử riêng.
- `backend/check_vnpay.py`: dùng server local sandbox với khóa fixture để kiểm tra IPN, return, số tiền, merchant, callback đồng thời, thanh toán đến muộn và thu tiền hai lần. Không gửi yêu cầu đến cổng VNPay.

Các kiểm tra này không thay thế xác minh SMTP với Gmail thật hoặc nghiệm thu bằng tài khoản VNPay của bạn.
