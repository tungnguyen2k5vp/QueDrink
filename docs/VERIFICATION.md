# Kiểm chứng ngày 06/11/2026

Các kiểm tra chạy trên DB và tài khoản demo riêng, không dùng dữ liệu khách hàng.

## Đã kiểm tra

- .NET 10 build: không lỗi/cảnh báo; self-test quy tắc tiền, voucher, số điện thoại, trạng thái và chặn thanh toán sai.
- EF xác nhận model không có thay đổi ngoài snapshot migration.
- `backend/check_api.py`: kiểm tra HTTP với PostgreSQL thật: đăng ký, role, quyền quản trị, refresh rotation, logout; tính giá máy chủ và từ chối size lạ; gửi trùng/đổi nội dung cùng mã; cạnh tranh lượt voucher; hủy trả lượt; bước nhận tại quán; điểm và điều kiện đánh giá; chữ ký/số tiền/callback lặp; sửa giá size giữ ID và lưu trữ sản phẩm.
- DB mới tự tạo bảng và thực đơn. Một DB khác dựng từ đúng SQL Java V1–V10, có tài khoản BCrypt, số +84, sản phẩm đã đổi giá và đơn snapshot. Sau EF migration vẫn giữ ID tài khoản, đăng nhập được bằng email/số 0..., đủ 15 sản phẩm, giá đã sửa, tổng đơn và tên snapshot.
- Frontend lint, TypeScript, 4 test giỏ hàng và production build thành công.
- Trình duyệt: chọn món → checkout → biên nhận → SSE; đăng nhập admin → xác nhận đơn → thêm sản phẩm; đăng ký khách → lưu yêu thích → đặt COD → quản trị hoàn thành → cộng điểm → đánh giá.
- Các route chính ở màn hình 390px không tràn ngang. Kiểm tra thêm giao diện desktop và modal.
- Bản API mới nhất chạy lại `check_api.py` thành công trên PostgreSQL kiểm thử riêng. Kiểm tra bổ sung tên chỉ có dấu cách, tên được trim, reset mật khẩu demo, token reset dùng một lần và thu hồi phiên cũ.
- Khi hai API quản trị cùng nhận token không hợp lệ, frontend chỉ gửi một yêu cầu refresh; hai yêu cầu được thử lại và tải dashboard thành công.
- `start-demo.ps1`: tạo DB demo riêng, mở web/API, dừng đúng tiến trình/container, mở lại và giữ nguyên đơn đã hoàn thành cùng tổng tiền 63.000đ. Health `UP`, thực đơn 15 món.
- Docker Compose kiểm tra cấu hình với file biến môi trường mẫu riêng; cả image ASP.NET và Next.js build thành công. Self-test trong image ASP.NET thành công sau khi bổ sung ICU cho xử lý tiếng Việt. Chưa chạy toàn bộ stack bằng Compose.

Các kết quả trên không thay thế load test, kiểm toán bảo mật hay chứng nhận truy cập. GitHub Actions đã được đổi sang .NET/Node; workflow sẽ chạy khi bạn đưa dự án lên repository.

Ảnh kiểm tra desktop/mobile lưu tại `D:\ReactJS\outputs\quedrink-migration-2026-11-06`.

## Giới hạn thực tế

- Đã có mã Gmail SMTP và VNPay sandbox/live; chưa có App Password/merchant của bạn nên chưa gửi thư bằng Gmail thật hoặc chạy thanh toán qua tài khoản VNPay của bạn. Không có giao dịch tiền thật trong kiểm chứng.
- Chưa tự động hoàn tiền, truy vấn mất IPN hoặc đóng hồ sơ đối soát; không đổi điểm hoặc đồng bộ tồn kho nguyên liệu/đơn vị giao hàng.
- SSE thăm DB mỗi 2 giây; danh sách public tối đa 200 món, lịch sử khách 200 đơn, bảng đơn admin 500, đánh giá public 50 gần nhất. Báo cáo tổng không bị giới hạn bởi số dòng admin tải.
- Đơn đã trả tiền không cho hủy tự động. Cần đối soát và hoàn tiền ngoài hệ thống trong phiên bản này.
- Ảnh là minh họa theo nhóm, không khẳng định là ảnh chính xác của từng món.
- Kiểm tra migration dùng dữ liệu giả trên cấu trúc cũ; chưa chuyển trực tiếp dữ liệu thật của bạn.

## Bổ sung Gmail/VNPay cùng ngày

- Backend build và self-test thành công; EF model khớp snapshot mới. Migration ExternalPayments chỉ thêm trường và index, giữ dữ liệu cũ; không cho rollback xóa bằng chứng thanh toán.
- Chạy lại kiểm tra HTTP MOCK thành công. `check_vnpay.py` thành công trên DB local với khóa fixture: đối chiếu chữ ký URL bằng Python, kiểm tra merchant/số tiền/tham số lặp, hai IPN đồng thời, replay, return không ghi PAID, chặn mock và giữ giao dịch đến muộn/thu hai lần để đối soát.
- Frontend lint, 4 test và production build thành công. Trình duyệt mở đúng URL sandbox từ checkout, hiển thị mục đối soát và tắt nút gửi Gmail khi chưa cấu hình; quản trị mới không tràn ngang ở 390px.
- Trình duyệt đi từ liên kết fragment → đặt mật khẩu mới thành công, xóa fragment khỏi URL; các mã reset và phiên cũ đều bị thu hồi. Request giả lập từ xa không nhận token demo.
- `configure-integrations.ps1` đã kiểm tra bằng địa chỉ/khóa fixture: nhận SecureString, tạo JSON có ACL riêng và từ chối ghi đè cấu hình. Không dùng thông tin đăng nhập thật.
- Gửi SMTP và nghiệm thu VNPay bên ngoài còn chờ tài khoản/khóa của bạn. Hướng dẫn tại [GMAIL-VNPAY.md](GMAIL-VNPAY.md).
