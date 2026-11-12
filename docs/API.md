# API ASP.NET

Base `/api/v1`. Response JSON có `success, message, data, timestamp, requestId`. DELETE thành công trả 204. Lỗi thường gặp: 400 đầu vào, 401 cần đăng nhập, 403 thiếu quyền, 404 không tìm thấy, 409 dữ liệu/lượt dùng/trạng thái xung đột, 429 quá nhiều yêu cầu.

## Công khai

| Phương thức | Đường dẫn | Dữ liệu / kết quả |
|---|---|---|
| GET | /store | Địa chỉ nhận, phí giao, provider và chế độ DEMO/SANDBOX/LIVE/DISABLED |
| GET | /categories | Nhóm đang hiển thị |
| GET | /products?category=...&q=...&featured=true | Món đang bán, tối đa 200 |
| GET | /products/{slug} | Món + sizes/toppings/sugarOptions/iceOptions |
| GET | /products/{productId}/reviews | Tối đa 50 đánh giá gần nhất từ người đã mua |
| POST | /orders/quote | items, deliveryType, voucherCode |
| POST | /orders | name, phone, address, note, paymentMethod, deliveryType, voucherCode, items |
| POST | /orders/lookup | orderCode, phone |
| POST | /orders/cancel | orderCode, phone; chỉ hủy trước pha chế và trước thanh toán |
| GET | /orders/{code}/events?token=... | SSE: sự kiện status, token lấy từ response tra cứu |
| POST | /payments/intents | orderCode, phone; header Idempotency-Key |
| POST | /payments/callbacks/mock | reference, orderCode, amount, status; header X-Payment-Signature |
| GET | /payments/vnpay/ipn | Tham số có chữ ký VNPay; JSON RspCode/Message đúng giao thức, cập nhật tiền tại server |
| GET | /payments/vnpay/return | Kiểm tra kết quả và redirect về tra cứu; không ghi nhận PAID |

Tạo đơn cần **Idempotency-Key** 16–80 ký tự gồm chữ, số, `_`, `-`. Lưu cùng key khi thử lại yêu cầu do lỗi mạng. Đổi nội dung phải dùng key mới. Đơn trả về mã, tổng tiền, version, allowedStatuses, canCancel và trackingToken.

Một dòng hàng:

```json
{
  "productId": "UUID",
  "sizeId": "UUID",
  "sugarOptionId": "UUID",
  "iceOptionId": "UUID",
  "toppingIds": [],
  "quantity": 2,
  "note": "Không dùng ống hút"
}
```

Không gửi giá để quyết định tổng tiền. Hình thức nhận `DELIVERY` hoặc `PICKUP`; thanh toán `COD` hoặc `BANK_TRANSFER` (qua provider đang cấu hình). Địa chỉ bắt buộc khi giao. Có thể đăng nhập để gắn đơn vào tài khoản; khách vãng lai vẫn đặt được.

## Tài khoản

- `POST /auth/register`: displayName, email hoặc phone, password (ít nhất 10 ký tự, tối đa 72 byte UTF-8).
- `POST /auth/login`: identity, password. Trả accessToken, refreshToken, expiresIn, user.
- `POST /auth/refresh` và `/auth/logout`: refreshToken.
- `POST /auth/change-password`: currentPassword, newPassword; cần Bearer token.
- `POST /auth/forgot-password`: identity. Gmail gửi liên kết khi được cấu hình. Mã demo chỉ có ở Development, website và kết nối loopback, Gmail tắt và AUTH_EXPOSE_RESET_TOKEN bật; không dùng trên website công khai.
- `POST /auth/reset-password`: token, newPassword.

Các endpoint sau cần `Authorization: Bearer <accessToken>`:

- `GET /orders/mine` — 200 đơn gần nhất của chính tài khoản.
- `GET /me/favorites`, `POST/DELETE /me/favorites/{productId}`.
- `GET /me/loyalty` — số điểm và 20 giao dịch gần nhất.
- `GET /me/reviews`, `POST /me/reviews` — productId, orderId, rating (1–5), comment (tối đa 1000 ký tự).

## Quản trị ADMIN

| Phương thức | Đường dẫn | Công việc |
|---|---|---|
| GET | /admin/orders | 500 đơn gần nhất |
| PATCH | /admin/orders/{code}/status | status, expectedVersion |
| GET | /admin/reports/summary | Toàn bộ doanh thu, số đơn, phân bố trạng thái, 7 ngày gần nhất |
| GET | /admin/audit | 100 thay đổi gần nhất |
| GET/POST | /admin/categories | Danh sách / thêm nhóm |
| PATCH | /admin/categories/{id} | name, active, sortOrder |
| GET/POST | /admin/toppings | Danh sách / thêm topping |
| PATCH | /admin/toppings/{id} | name, price, available |
| GET/POST | /admin/products | Danh sách / thêm món |
| GET/PATCH/DELETE | /admin/products/{id} | Chi tiết / sửa / lưu trữ mềm |
| GET/POST | /admin/vouchers | Danh sách / tạo mã |
| PATCH | /admin/vouchers/{code} | Sửa điều kiện, không đổi code |
| POST | /admin/payments/{code}/mock | Chỉ provider MOCK trong Development; mô phỏng SUCCESS |
| GET | /admin/payments/review | Tối đa 100 giao dịch có tiền cần đối soát |
| GET | /admin/integrations | Trạng thái cấu hình Gmail/provider, URL IPN và return; không trả khóa |
| POST | /admin/integrations/gmail/test | Gửi thư thử đến chính Gmail đã cấu hình; tối đa 3 lần/phút/IP |

Product: name, categoryId, description, imageUrl (HTTPS hoặc `/images/`), basePrice, salePrice, clearSalePrice, available, featured, bestSeller, sizes `[{name,price}]`, toppingIds. `sizes.price` là giá cộng thêm, không phải toàn bộ giá ly. Tạo mới tự có 5 mức đường và 5 mức đá.

Voucher: code, discountType `FIXED/PERCENT`, discountValue, minOrder, maxDiscount, usageLimit, active, startsAt, endsAt (ISO8601). PATCH dùng `clearMaxDiscount/clearUsageLimit/clearStartsAt/clearEndsAt=true` để bỏ giới hạn tương ứng.

Callback demo ký HMAC-SHA256 UTF-8: `reference|orderCode|amount|status`, khóa PAYMENT_CALLBACK_SECRET, mã hex chữ thường. Chữ ký ràng buộc số tiền; kết quả SUCCESS không bị ghi đè thành FAILED. Đây là giao thức demo nội bộ, không phải giao thức VNPay/MoMo.
