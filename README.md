# Quê Drinks — ASP.NET Core + Next.js

Website đặt đồ uống, phục vụ demo và học full stack C#/.NET. Backend Java đã được chuyển sang **ASP.NET Core 10, Entity Framework Core và PostgreSQL 16**. Frontend dùng **Next.js, React, TypeScript và Tailwind CSS**.

## Chạy nhanh trên Windows

Cần .NET SDK 10, Node.js 24 và Docker Desktop đang chạy.

```powershell
cd D:\ReactJS\QueDrink
powershell -NoProfile -ExecutionPolicy Bypass -File .\start-demo.ps1
```

- Web: http://127.0.0.1:3006
- Quản trị: http://127.0.0.1:3006/admin/login
- Tài khoản **demo cục bộ**: `admin@quedrink.local` / `QueDrink!2026Demo`
- API: http://127.0.0.1:5086/api/v1
- Health: http://127.0.0.1:5086/actuator/health

Script tự tạo PostgreSQL riêng ở cổng **55439**, lưu trong volume `quedrink_dotnet_demo_data`; không dùng cơ sở dữ liệu Java cũ. Mật khẩu DB và khóa ký được tạo riêng, lưu trong `.local/demo-settings.json`. Giữ file này cùng volume. Lần đầu cần mạng để tải package/image nếu máy chưa có. Các tiến trình chạy nền, log ở `.local/`.

```powershell
# Tắt web/API và container demo, giữ dữ liệu
powershell -NoProfile -ExecutionPolicy Bypass -File .\start-demo.ps1 -Stop
# Chọn cổng khác nếu cần
powershell -NoProfile -ExecutionPolicy Bypass -File .\start-demo.ps1 -WebPort 3007 -ApiPort 5088
```

## Các nghiệp vụ đã nối xuyên suốt

| Khách hàng | Quản trị |
|---|---|
| Tìm không dấu, lọc danh mục/giá, sắp xếp | Thêm/sửa/lưu trữ món, đổi trạng thái bán |
| Chọn size, đá, đường, tối đa 5 topping và ghi chú | Quản lý giá size, liên kết topping, danh mục |
| Giỏ hàng lưu cục bộ, giới hạn số lượng | Thêm/sửa topping và trạng thái phục vụ |
| Báo giá từ máy chủ, voucher, phí giao nhận | Voucher theo số tiền/%, thời hạn và lượt dùng |
| Giao hàng hoặc nhận tại quán, chống gửi trùng đơn | Xử lý đơn theo bước hợp lệ và phiên bản hiện tại |
| Tra cứu, cập nhật trực tiếp, hủy đơn chưa pha và chưa trả tiền | Báo cáo doanh thu từ đơn hoàn thành + đã trả tiền |
| Đăng ký/đăng nhập/đổi mật khẩu, tự gia hạn phiên | Phân quyền ADMIN được kiểm tra tại API |
| Lịch sử đơn, món yêu thích, điểm và đánh giá sau mua | Nhật ký các thao tác thay đổi quan trọng trong API |

**Gmail SMTP và VNPay đã có mã tích hợp**, cần tài khoản/khóa của bạn để bật. VNPay hỗ trợ sandbox/live, xác minh IPN, kiểm tra số tiền, chống lặp và giữ giao dịch cần đối soát. Gmail gửi liên kết khôi phục mật khẩu; COD được ghi nhận đã trả tiền khi hoàn thành đơn. Chưa cấu hình dịch vụ thì demo cục bộ vẫn dùng MOCK; Production tắt thanh toán trực tuyến mặc định. Chưa có đổi điểm, tồn kho nguyên liệu, đối tác vận chuyển hoặc hoàn tiền tự động.

Bạn chưa có merchant/App Password: làm theo [hướng dẫn bật Gmail và VNPay](docs/GMAIL-VNPAY.md). Script `configure-integrations.ps1` nhận khóa bằng ô nhập ẩn và lưu cấu hình riêng tại máy; không gửi khóa qua chat.

## Kiểm tra

```powershell
dotnet build .\backend\QueDrink.Api.csproj
dotnet run --project .\backend\QueDrink.Api.csproj -- --self-test
cd frontend
npm run lint
npm test
npm run build
```

Kiểm tra HTTP chỉ chạy trên DB thử riêng vì sẽ tạo dữ liệu:

```powershell
$env:QUE_API = 'http://127.0.0.1:5086/api/v1'
$env:QUE_ADMIN_PASSWORD = 'QueDrink!2026Demo'
$settings = Get-Content ..\.local\demo-settings.json -Raw | ConvertFrom-Json
$env:QUE_PAYMENT_SECRET = $settings.payment
python ..\backend\check_api.py
```

## Mã nguồn và tài liệu

- [Hướng dẫn demo và phần cần hiểu để phỏng vấn](docs/DEMO.md)
- [Cấu trúc code và quy tắc nghiệp vụ](docs/ARCHITECTURE.md)
- [Danh sách API](docs/API.md)
- [Chuyển dữ liệu Java sang .NET, Docker và cấu hình](docs/MIGRATION.md)
- [Kết quả kiểm tra và giới hạn](docs/VERIFICATION.md)

Backend dùng Minimal API + EF Core trực tiếp, tách theo nghiệp vụ; không có lớp repository/service trung gian cho từng bảng. `backend/Migrations/Legacy` giữ SQL cần để tạo mới hoặc tiếp nhận cấu trúc PostgreSQL cũ. Không đưa `.env`, `.local`, khóa, dữ liệu khách hay thư mục build lên Git.
