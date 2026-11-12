# Dữ liệu, cấu hình và chuyển đổi

## Dùng PostgreSQL Java cũ

ASP.NET tiếp nhận schema từ các migration Flyway V1–V10 hiện có. SQL gốc nằm trong `backend/Migrations/Legacy`. EF migration đầu tiên kiểm tra các mốc bảng: tạo phần chưa có, giữ phần đã tồn tại; không xóa bảng hoặc seed lại sản phẩm đang có.

Bổ sung `users.session_version`, `orders.request_hash`, `orders.version` và index; mở rộng password_hash. Bảng Flyway history cũ được giữ lại. Mật khẩu BCrypt cũ tiếp tục đăng nhập được; JWT cũ không được dùng tiếp, người dùng đăng nhập lại.

Trước khi trỏ app vào DB thật, xuất `pg_dump` và kiểm tra bản sao phục hồi. Dùng bản sao để chạy migration trước. Không dùng `docker compose down -v` với volume cần giữ. Không dùng script demo làm cách chuyển dữ liệu: script đó chủ động dùng DB riêng.

Migration Down từ chối xóa dữ liệu. Muốn quay lại Java cần ngừng ghi, phục hồi bản sao đã kiểm tra và chạy source Java tương ứng; không trộn hai backend ghi cùng lúc vào một DB.

## Chạy API thủ công

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5086'
$env:ConnectionStrings__Database = 'Host=localhost;Port=5432;Database=quedrink;Username=...;Password=...'
$env:JWT_SECRET = '<chuỗi ngẫu nhiên riêng ít nhất 48 ký tự>'
$env:PAYMENT_CALLBACK_SECRET = '<chuỗi ngẫu nhiên riêng ít nhất 48 ký tự>'
dotnet run --project backend/QueDrink.Api.csproj
```

Admin seed dùng ADMIN_EMAIL và ADMIN_PASSWORD (ít nhất 12 ký tự, tối đa 72 byte). Seed chỉ tạo nếu chưa có, không thay mật khẩu tài khoản cũ. Bỏ hai biến nếu không cần seed admin.

## Cấu hình

| Biến | Ý nghĩa |
|---|---|
| ConnectionStrings__Database | Kết nối PostgreSQL cho native API; thay cấu hình JDBC cũ |
| JWT_SECRET | Khóa JWT và token theo dõi; giữ ổn định để không mất phiên sau restart |
| PAYMENT_CALLBACK_SECRET | Khóa ký callback demo; tách khỏi JWT |
| FRONTEND_URL | Origin được CORS cho phép; native mặc định localhost:3000 |
| API_INTERNAL_URL | URL API mà Next rewrite gọi; đặt trước khi khởi chạy/build frontend |
| NEXT_PUBLIC_API_URL | Mặc định /api/v1; trình duyệt gọi cùng origin |
| STORE_NAME / STORE_ADDRESS / STORE_PHONE | Thông tin quán; cần điền thông tin thật nếu phục vụ khách thật |
| SHIPPING_FEE / SHIPPING_FREE_FROM | Phí mặc định 10000 / miễn giao từ tiền món 150000 |
| AUTH_EXPOSE_RESET_TOKEN | Chỉ Development, false mặc định; bật để thực hành khôi phục cục bộ |
| DATA_PROTECTION_PATH | Thư mục key nội bộ ASP.NET; native mặc định backend/.keys, Docker /keys |

Secret không nằm trong source, response hay log bình thường. Development có thể sinh khóa tạm nếu không đặt; script demo sinh và giữ khóa riêng trong `.local`. Production cần đặt cả hai khóa ít nhất 48 ký tự.

## Docker Compose

Sao chép `.env.example` thành `.env` khi chưa có cấu hình riêng; điền DB password, hai khóa ngẫu nhiên và tài khoản admin. Nếu đã có `.env` Java, giữ bản sao và cập nhật biến cần thiết theo bảng trên. Không ghi đè file đang dùng bằng dữ liệu mẫu.

```powershell
docker compose config --quiet
docker compose up --build -d
```

Compose giữ volume `postgres_data`, thêm volume `backend_keys`, dùng API .NET ở 8080 và web 3000. Frontend build nhận `API_INTERNAL_URL=http://backend:8080`; ảnh public được đưa vào image. Các cổng mặc định chỉ bind loopback. Dockerfile dùng user không phải root khi chạy ứng dụng.

Đây là cấu hình local/demo. Mã tích hợp Gmail/VNPay và cách bật bằng tài khoản của bạn nằm trong [GMAIL-VNPAY.md](GMAIL-VNPAY.md). Migration `ExternalPayments` bổ sung thông tin VNPay và cờ đối soát, không xóa dữ liệu; không hỗ trợ Down làm mất bằng chứng giao dịch. Khi đưa lên Internet vẫn cần HTTPS/reverse proxy, secret triển khai riêng, sao lưu/giám sát, merchant được duyệt và quy trình đối soát.

## Ảnh minh họa

Ảnh tải từ Unsplash, lưu cục bộ; UI ghi “Ảnh minh họa” nếu món chưa có ảnh riêng. Nguồn theo file:

- boba.jpg: https://images.unsplash.com/photo-1558857563-b371033873b8.
- fruit.jpg: image ID photo-1546173159-315724a31696.
- coffee.jpg: image ID photo-1461023058943-07fcbe16d735.
- matcha.jpg: image ID photo-1515823064-d6e0c04616a7.
- milk-tea.jpg: image ID photo-1572490122747-3968b75cc699; minh họa đồ uống xay.

URL ảnh gốc: `https://images.unsplash.com/<image ID>`. Thay bằng ảnh sản phẩm thực tế trước khi bán hàng.
