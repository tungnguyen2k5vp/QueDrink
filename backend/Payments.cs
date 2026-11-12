using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace QueDrink;

public sealed record PaymentInput([property: Required, MaxLength(32)] string OrderCode, [property: Required] string Phone);
public sealed record CallbackInput([property: Required, MaxLength(80)] string Reference,
    [property: Required, MaxLength(32)] string OrderCode, long Amount, [property: Required] string Status);

public static class PaymentEndpoints
{
    private static object View(Payment payment, Order order, VnPay pay, HttpContext http) => new
    {
        reference = payment.ProviderReference, order.OrderCode, payment.Amount, payment.Status,
        checkoutUrl = payment.Provider == "MOCK" ? "/orders?order=" + order.OrderCode :
            payment.Status == "PENDING" && order.PaymentStatus == "UNPAID" && order.OrderStatus == "PENDING"
                ? pay.CheckoutUrl(payment, order, http.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1") : null,
        provider = payment.Provider, isDemo = payment.Provider != "VNPAY", payment.NeedsReview,
        expiresAt = payment.CreatedAt.AddMinutes(15)
    };
    private static IResult Reply(string code, string message) => Results.Json(new Dictionary<string, string>
        { ["RspCode"] = code, ["Message"] = message });

    public static void MapPayments(this WebApplication app)
    {
        app.MapPost("/api/v1/payments/intents", async (PaymentInput input, ShopDb db, VnPay pay, HttpContext http) =>
        {
            Rules.Validate(input); Rules.Require(pay.OnlineEnabled, "Thanh toán trực tuyến chưa được cấu hình.", 409);
            var key = Rules.Key(http.Request.Headers["Idempotency-Key"]); var phone = Rules.Phone(input.Phone);
            await using var tx = await db.Database.BeginTransactionAsync(); await Checkout.LockKey(db, "payment:" + key);
            var order = await OrderEndpoints.Lock(db, input.OrderCode);
            Rules.Require(Rules.Phone(order.GuestPhone) == phone, "Mã đơn hoặc số điện thoại chưa chính xác.", 404);
            var existing = await db.Payments.SingleOrDefaultAsync(p => p.IdempotencyKey == key);
            if (existing is not null)
            {
                Rules.Require(existing.OrderId == order.Id && existing.Amount == order.Total && existing.Provider == pay.DatabaseProvider,
                    "Mã yêu cầu thanh toán đã dùng cho đơn hoặc cấu hình khác.", 409);
                Rules.Require(existing.Provider == "MOCK" || existing.Status != "PENDING" || existing.CreatedAt.AddMinutes(15) > DateTimeOffset.UtcNow,
                    "Liên kết đã hết hạn. Hãy tạo yêu cầu thanh toán mới tại trang tra cứu.", 409);
                return Api.Ok(http, View(existing, order, pay, http));
            }
            Rules.Require(order.PaymentMethod == "BANK_TRANSFER" && order.PaymentStatus == "UNPAID" && order.OrderStatus == "PENDING" && order.Total > 0,
                "Đơn không còn chờ thanh toán hoặc không dùng hình thức này.", 409);
            Rules.Require(pay.Provider != "VNPAY" || order.Total <= 9999999999, "Số tiền vượt giới hạn giao dịch VNPay.");
            var pending = await db.Payments.Where(p => p.OrderId == order.Id && p.Provider == pay.DatabaseProvider && p.Status == "PENDING")
                .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();
            if (pending is not null && (pending.Provider == "MOCK" || pending.CreatedAt.AddMinutes(15) > DateTimeOffset.UtcNow))
                return Api.Ok(http, View(pending, order, pay, http));
            if (pending is not null) { pending.Status = "EXPIRED"; pending.UpdatedAt = DateTimeOffset.UtcNow; }
            var payment = new Payment { OrderId = order.Id, Provider = pay.DatabaseProvider,
                ProviderReference = "PAY" + Guid.NewGuid().ToString("N").ToUpperInvariant(), Amount = order.Total, IdempotencyKey = key };
            db.Payments.Add(payment); await db.SaveChangesAsync(); await tx.CommitAsync();
            return Api.Ok(http, View(payment, order, pay, http), pay.Mode == "LIVE" ? "Tiếp tục thanh toán tại VNPay." :
                pay.Provider == "VNPAY" ? "VNPay sandbox: chỉ dùng thông tin thẻ thử nghiệm." : "Thanh toán thử nghiệm: không chuyển tiền thật.", 201);
        }).RequireRateLimiting("lookup");

        app.MapGet("/api/v1/payments/vnpay/ipn", async (ShopDb db, VnPay pay, HttpContext http) =>
        {
            var fields = pay.Verify(http.Request.Query);
            if (fields is null) return Reply("97", "Invalid signature");
            try { return await ApplyVnPay(fields, db, pay, http); }
            catch (Exception error)
            {
                app.Logger.LogError("VNPAY IPN could not be committed ({ErrorType}); gateway should retry. Request {RequestId}", error.GetType().Name, http.TraceIdentifier);
                return Reply("99", "Temporary processing error");
            }
        });
        app.MapGet("/api/v1/payments/vnpay/return", async (ShopDb db, VnPay pay, HttpContext http) =>
        {
            var fields = pay.Verify(http.Request.Query);
            var fallback = pay.Site + "/orders?payment=unverified";
            if (fields is null) return Results.Redirect(fallback);
            var reference = fields.GetValueOrDefault("vnp_TxnRef");
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.ProviderReference == reference && p.Provider == pay.DatabaseProvider);
            if (payment is null || !AmountMatches(fields, payment.Amount)) return Results.Redirect(fallback);
            var code = await db.Orders.Where(o => o.Id == payment.OrderId).Select(o => o.OrderCode).SingleAsync();
            // Browser return never credits an order; only the server IPN does.
            return Results.Redirect(pay.Site + "/orders?order=" + Uri.EscapeDataString(code) + "&payment=" +
                (fields.GetValueOrDefault("vnp_ResponseCode") == "24" ? "cancelled" : "waiting"));
        });

        app.MapPost("/api/v1/payments/callbacks/mock", async (CallbackInput input, ShopDb db, VnPay pay, Identity identity, HttpContext http) =>
        {
            Rules.Require(pay.Provider == "MOCK", "Không có endpoint mô phỏng trong chế độ thanh toán này.", 404);
            Rules.Validate(input); var payload = $"{input.Reference}|{input.OrderCode}|{input.Amount}|{input.Status}";
            Rules.Require(Identity.Equal(Identity.Sign(payload, identity.PaymentSecret), http.Request.Headers["X-Payment-Signature"]), "Chữ ký thanh toán không hợp lệ.", 403);
            return Api.Ok(http, await ApplyMock(input, db, http));
        });
        app.MapPost("/api/v1/admin/payments/{code}/mock", async (string code, ShopDb db, VnPay pay, HttpContext http) =>
        {
            Rules.Require(pay.Provider == "MOCK", "Thanh toán mô phỏng chỉ khả dụng trong chế độ MOCK.", 404);
            var order = await OrderEndpoints.Find(db, code);
            var payment = await db.Payments.AsNoTracking().Where(p => p.OrderId == order.Id && p.Provider == "MOCK")
                .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync() ?? throw new ShopError(409, "Chưa có yêu cầu thanh toán cho đơn.");
            return Api.Ok(http, await ApplyMock(new CallbackInput(payment.ProviderReference, order.OrderCode, payment.Amount, "SUCCESS"), db, http), "Đã mô phỏng thanh toán thành công.");
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));
        app.MapGet("/api/v1/admin/payments/review", async (ShopDb db, HttpContext http) => Api.Ok(http,
            await (from payment in db.Payments.AsNoTracking() join order in db.Orders on payment.OrderId equals order.Id
                   where payment.NeedsReview orderby payment.UpdatedAt descending
                   select new { reference = payment.ProviderReference, payment.Provider, payment.Amount, payment.GatewayTransactionId,
                       payment.GatewayResponseCode, payment.GatewayTransactionStatus, order.OrderCode, order.OrderStatus, order.PaymentStatus, payment.UpdatedAt })
                .Take(100).ToListAsync())).RequireAuthorization(policy => policy.RequireRole("ADMIN"));
    }

    private static bool AmountMatches(Dictionary<string, string> fields, long amount) =>
        long.TryParse(fields.GetValueOrDefault("vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out var received) && received == checked(amount * 100);

    private static async Task<IResult> ApplyVnPay(Dictionary<string, string> fields, ShopDb db, VnPay pay, HttpContext http)
    {
        var reference = fields.GetValueOrDefault("vnp_TxnRef");
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.ProviderReference == reference && p.Provider == pay.DatabaseProvider);
        if (payment is null) return Reply("01", "Transaction not found");
        await using var tx = await db.Database.BeginTransactionAsync();
        var code = await db.Orders.Where(o => o.Id == payment.OrderId).Select(o => o.OrderCode).SingleAsync();
        var order = await OrderEndpoints.Lock(db, code);
        payment = await db.Payments.FromSqlInterpolated($"SELECT * FROM payment_transactions WHERE id={payment.Id} FOR UPDATE").SingleAsync();
        if (!AmountMatches(fields, payment.Amount) || payment.Amount != order.Total) return Reply("04", "Invalid amount");
        var response = fields.GetValueOrDefault("vnp_ResponseCode") ?? "";
        var status = fields.GetValueOrDefault("vnp_TransactionStatus") ?? "";
        if (response.Length != 2 || status.Length != 2 || !response.All(char.IsAsciiDigit) || !status.All(char.IsAsciiDigit))
            return Reply("99", "Invalid result fields");
        var success = response == "00" && status == "00";
        if (payment.Status == "SUCCESS" || payment.Status == "FAILED" && !success) return Reply("02", "Already processed");
        if (success)
        {
            var transaction = fields.GetValueOrDefault("vnp_TransactionNo") ?? "";
            if (transaction.Length is < 1 or > 64 || transaction == "0" || !transaction.All(char.IsAsciiDigit)) return Reply("99", "Invalid gateway transaction");
            if (await db.Payments.AnyAsync(p => p.Provider == payment.Provider && p.GatewayTransactionId == transaction && p.Id != payment.Id))
                return Reply("99", "Gateway transaction already assigned");
            payment.GatewayTransactionId = transaction;
            payment.NeedsReview = order.OrderStatus is "CANCELLED" or "REJECTED" || order.PaymentStatus != "UNPAID";
            if (!payment.NeedsReview) { order.PaymentStatus = "PAID"; order.OrderStatus = "CONFIRMED"; }
            else if (order.PaymentStatus == "UNPAID") order.PaymentStatus = "REVIEW";
            order.Version++; order.UpdatedAt = DateTimeOffset.UtcNow;
        }
        payment.Status = success ? "SUCCESS" : "FAILED";
        payment.GatewayResponseCode = response; payment.GatewayTransactionStatus = status; payment.UpdatedAt = DateTimeOffset.UtcNow;
        db.Log(http, payment.NeedsReview ? "PAYMENT_REQUIRES_REVIEW" : "VNPAY_" + payment.Status, "ORDER", order.OrderCode);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Reply("00", "Confirm success");
    }

    private static async Task<object> ApplyMock(CallbackInput input, ShopDb db, HttpContext http)
    {
        Rules.Require(input.Status is "SUCCESS" or "FAILED", "Trạng thái thanh toán không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = await OrderEndpoints.Lock(db, input.OrderCode);
        var payment = await db.Payments.FromSqlInterpolated($"SELECT * FROM payment_transactions WHERE provider_reference={input.Reference} FOR UPDATE").SingleOrDefaultAsync()
            ?? throw new ShopError(404, "Không tìm thấy giao dịch.");
        Rules.Require(payment.Provider == "MOCK" && payment.OrderId == order.Id && payment.Amount == input.Amount, "Thông tin thanh toán không khớp.");
        if (payment.Status != "SUCCESS")
        {
            Rules.Require(order.OrderStatus is not "CANCELLED" and not "REJECTED" && order.PaymentStatus == "UNPAID", "Đơn không còn nhận thanh toán tự động; cần đối soát thủ công.", 409);
            payment.Status = input.Status; payment.UpdatedAt = DateTimeOffset.UtcNow;
            if (input.Status == "SUCCESS") { order.PaymentStatus = "PAID"; order.OrderStatus = "CONFIRMED"; order.Version++; order.UpdatedAt = DateTimeOffset.UtcNow; }
            db.Log(http, "PAYMENT_" + input.Status, "ORDER", order.OrderCode);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return new { reference = payment.ProviderReference, payment.Status, order.OrderCode, order.PaymentStatus };
    }
}
