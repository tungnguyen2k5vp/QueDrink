using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace QueDrink;

public sealed record LookupRequest([property: Required, MaxLength(32)] string OrderCode, [property: Required, MaxLength(20)] string Phone);
public sealed record StatusRequest([property: Required] string Status, int? ExpectedVersion);

public static class OrderEndpoints
{
    public static object View(Order order, Identity identity) => new
    {
        order.Id, order.OrderCode, order.GuestName, order.GuestPhone, order.DeliveryAddress, order.Note,
        order.Subtotal, order.ShippingFee, order.Discount, order.Total, order.VoucherCode, order.DeliveryType,
        order.PickupLocation, order.PaymentMethod, order.PaymentStatus, order.OrderStatus, order.CreatedAt,
        order.Version, allowedStatuses = Rules.Next(order), trackingToken = identity.Tracking(order),
        canCancel = order.PaymentStatus == "UNPAID" && order.OrderStatus is "PENDING" or "CONFIRMED",
        items = order.Items.Select(i => new { i.ProductId, name = i.ProductName, price = i.UnitPrice, i.Quantity,
            total = i.LineTotal, size = i.SizeName, sugar = i.SugarName, ice = i.IceName, toppings = i.ToppingsSummary, note = i.ItemNote })
    };
    public static async Task<Order> Find(ShopDb db, string code) => await db.Orders.Include(o => o.Items)
        .SingleOrDefaultAsync(o => o.OrderCode == code.Trim().ToUpperInvariant()) ?? throw new ShopError(404, "Không tìm thấy đơn hàng.");
    public static async Task<Order> Lock(ShopDb db, string code)
    {
        var value = code.Trim().ToUpperInvariant();
        return await db.Orders.FromSqlInterpolated($"SELECT * FROM orders WHERE order_code={value} FOR UPDATE")
            .Include(o => o.Items).AsSplitQuery().SingleOrDefaultAsync() ?? throw new ShopError(404, "Không tìm thấy đơn hàng.");
    }
    public static void MapOrders(this WebApplication app)
    {
        var orders = app.MapGroup("/api/v1/orders");
        orders.MapPost("/quote", async (QuoteRequest input, Checkout checkout, HttpContext http) =>
        {
            var price = await checkout.Price(input, false);
            return Api.Ok(http, new { price.Subtotal, shippingFee = price.Shipping, price.Discount,
                total = checked(price.Subtotal + price.Shipping - price.Discount), voucherCode = price.Voucher,
                deliveryType = Checkout.Fulfillment(input.DeliveryType) }, "Báo giá từ quán.");
        });
        orders.MapPost("", async (CreateOrderRequest input, Checkout checkout, ShopDb db, Identity identity, VnPay pay, HttpContext http) =>
        {
            Rules.Validate(input); var phone = Rules.Phone(input.Phone); var type = Checkout.Fulfillment(input.DeliveryType);
            Rules.Require(input.Name.Trim().Length >= 2, "Tên người nhận cần ít nhất 2 ký tự.");
            Rules.Require(input.PaymentMethod is "COD" or "BANK_TRANSFER", "Phương thức thanh toán không hợp lệ.");
            Rules.Require(type != "DELIVERY" || !string.IsNullOrWhiteSpace(input.Address), "Vui lòng nhập địa chỉ giao hàng.");
            var key = Rules.Key(http.Request.Headers["Idempotency-Key"]);
            var userId = Identity.UserId(http.User);
            var hash = Rules.Hash(JsonSerializer.Serialize(new { input, userId }));
            await using var tx = await db.Database.BeginTransactionAsync();
            await Checkout.LockKey(db, "order:" + key);
            var existing = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.IdempotencyKey == key);
            if (existing is not null)
            {
                Rules.Require(existing.RequestHash == hash && existing.UserId == userId,
                    "Yêu cầu đã thay đổi. Vui lòng xác nhận lại giỏ và gửi như một đơn mới.", 409);
                return Api.Ok(http, View(existing, identity), "Đơn hàng đã được tiếp nhận trước đó.");
            }
            Rules.Require(input.PaymentMethod != "BANK_TRANSFER" || pay.OnlineEnabled, "Thanh toán trực tuyến chưa được cấu hình. Vui lòng chọn trả khi nhận hàng.", 409);
            var price = await checkout.Price(new QuoteRequest(input.Items, type, input.VoucherCode), true);
            var order = new Order { UserId = userId, OrderCode = "QD-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)),
                GuestName = input.Name.Trim(), GuestPhone = phone, DeliveryAddress = type == "PICKUP" ? checkout.PickupAddress : input.Address!.Trim(),
                Note = input.Note?.Trim(), Subtotal = price.Subtotal, ShippingFee = price.Shipping, Discount = price.Discount,
                Total = checked(price.Subtotal + price.Shipping - price.Discount), VoucherCode = price.Voucher,
                DeliveryType = type, PickupLocation = type == "PICKUP" ? checkout.PickupAddress : null,
                IdempotencyKey = key, RequestHash = hash, PaymentMethod = input.PaymentMethod, Items = price.Items.ToList() };
            if (order.Total == 0) { order.PaymentStatus = "PAID"; order.OrderStatus = "CONFIRMED"; }
            db.Orders.Add(order); db.Log(http, "ORDER_CREATED", "ORDER", order.OrderCode);
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Api.Ok(http, View(order, identity), "Đặt hàng thành công.", 201);
        });
        orders.MapPost("/lookup", async (LookupRequest input, ShopDb db, Identity identity, HttpContext http) =>
        {
            Rules.Validate(input); var phone = Rules.Phone(input.Phone); var code = input.OrderCode.Trim().ToUpperInvariant();
            var order = await Find(db, code);
            Rules.Require(Rules.Phone(order.GuestPhone) == phone, "Mã đơn hoặc số điện thoại chưa chính xác.", 404);
            return Api.Ok(http, View(order, identity));
        }).RequireRateLimiting("lookup");
        orders.MapGet("/mine", async (ShopDb db, Identity identity, HttpContext http) =>
        {
            var id = Identity.RequiredUser(http.User);
            var list = await db.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.UserId == id).OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync();
            return Api.Ok(http, list.Select(o => View(o, identity)));
        }).RequireAuthorization();
        orders.MapPost("/cancel", async (LookupRequest input, Checkout checkout, ShopDb db, Identity identity, HttpContext http) =>
        {
            Rules.Validate(input); var phone = Rules.Phone(input.Phone);
            await using var tx = await db.Database.BeginTransactionAsync();
            var order = await Lock(db, input.OrderCode);
            Rules.Require(Rules.Phone(order.GuestPhone) == phone, "Mã đơn hoặc số điện thoại chưa chính xác.", 404);
            Rules.Transition(order, "CANCELLED"); await checkout.ReleaseVoucher(order);
            db.Log(http, "ORDER_CANCELLED", "ORDER", order.OrderCode);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Api.Ok(http, View(order, identity), "Đã hủy đơn chưa pha chế.");
        }).RequireRateLimiting("lookup");
        orders.MapGet("/{code}/events", async (string code, string token, ShopDb db, Identity identity, HttpContext http) =>
        {
            var order = await Find(db, code); Rules.Require(identity.CanTrack(order, token), "Liên kết theo dõi hết hạn. Vui lòng tra cứu lại.", 403);
            http.Response.ContentType = "text/event-stream"; http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            var previous = ""; var until = DateTimeOffset.UtcNow.AddMinutes(2);
            // ponytail: poll the database for one small shop; use notifications when concurrent tracking grows.
            while (!http.RequestAborted.IsCancellationRequested && DateTimeOffset.UtcNow < until)
            {
                var current = await db.Orders.AsNoTracking().Where(o => o.Id == order.Id)
                    .Select(o => new { o.OrderStatus, o.PaymentStatus, o.Total, o.Version }).SingleAsync(http.RequestAborted);
                var data = JsonSerializer.Serialize(current, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (data != previous) { await http.Response.WriteAsync("event: status\ndata: " + data + "\n\n", http.RequestAborted); previous = data; }
                else await http.Response.WriteAsync(": keepalive\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
                if (current.OrderStatus is "COMPLETED" or "CANCELLED" or "REJECTED") break;
                await Task.Delay(2000, http.RequestAborted);
            }
        }).RequireRateLimiting("lookup");

        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization(policy => policy.RequireRole("ADMIN"));
        admin.MapGet("/orders", async (ShopDb db, Identity identity, HttpContext http) =>
        {
            var list = await db.Orders.AsNoTracking().Include(o => o.Items).OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync();
            return Api.Ok(http, list.Select(o => View(o, identity)));
        });
        admin.MapPatch("/orders/{code}/status", async (string code, StatusRequest input, ShopDb db, Checkout checkout, Identity identity, HttpContext http) =>
        {
            Rules.Validate(input); await using var tx = await db.Database.BeginTransactionAsync();
            var order = await Lock(db, code);
            Rules.Require(input.ExpectedVersion is null || input.ExpectedVersion == order.Version, "Đơn đã thay đổi. Vui lòng tải lại.", 409);
            Rules.Transition(order, input.Status);
            if (input.Status is "CANCELLED" or "REJECTED") await checkout.ReleaseVoucher(order);
            if (input.Status == "COMPLETED" && order.UserId is Guid user && !await db.Loyalty.AnyAsync(l => l.OrderId == order.Id))
                db.Loyalty.Add(new LoyaltyEntry { UserId = user, OrderId = order.Id, Points = (int)(order.Total / 10000) });
            db.Log(http, "ORDER_" + input.Status, "ORDER", code);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Api.Ok(http, View(order, identity), "Đã cập nhật đơn.");
        });
        admin.MapGet("/reports/summary", async (ShopDb db, HttpContext http) =>
        {
            var groups = await db.Orders.GroupBy(o => o.OrderStatus).Select(g => new { status = g.Key, count = g.LongCount() }).ToListAsync();
            var paid = db.Orders.Where(o => o.OrderStatus == "COMPLETED" && o.PaymentStatus == "PAID");
            var revenue = await paid.SumAsync(o => (long?)o.Total) ?? 0; var completed = await paid.LongCountAsync();
            var since = DateTimeOffset.UtcNow.AddDays(-7);
            var recent = await paid.Where(o => o.CreatedAt >= since).Select(o => new { o.CreatedAt, o.Total }).ToListAsync();
            return Api.Ok(http, new { totalOrders = groups.Sum(g => g.count), completedOrders = completed, revenue,
                averageOrderValue = completed == 0 ? 0 : revenue / completed, byStatus = groups.ToDictionary(g => g.status, g => g.count),
                daily = recent.GroupBy(o => o.CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd"))
                    .OrderBy(g => g.Key).Select(g => new { date = g.Key, revenue = g.Sum(o => o.Total), orders = g.Count() }) });
        });
        admin.MapGet("/audit", async (ShopDb db, HttpContext http) => Api.Ok(http,
            await db.Audit.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync()));
    }
}
