using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QueDrink;

public sealed class ShopError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public static partial class Rules
{
    public static void Require(bool condition, string message, int status = 400)
    { if (!condition) throw new ShopError(status, message); }
    public static T Validate<T>(T request) where T : notnull
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        return request;
    }
    public static string Normalize(string text) => string.Concat(text.Trim().ToLowerInvariant()
        .Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        .Replace('đ', 'd').Normalize(NormalizationForm.FormC);
    public static string Phone(string? value)
    {
        var phone = (value ?? "").Trim().Replace(" ", "");
        if (phone.StartsWith("+84")) phone = "0" + phone[3..];
        Require(PhonePattern().IsMatch(phone), "Số điện thoại Việt Nam không hợp lệ.");
        return phone;
    }
    public static string Key(string? key)
    { Require(key is not null && KeyPattern().IsMatch(key), "Thiếu Idempotency-Key hợp lệ (16–80 ký tự)."); return key!; }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static long Discount(Voucher voucher, long subtotal, DateTimeOffset now)
    {
        Require(voucher.Active && (!voucher.StartsAt.HasValue || now >= voucher.StartsAt) &&
            (!voucher.EndsAt.HasValue || now < voucher.EndsAt), "Mã ưu đãi chưa có hiệu lực hoặc đã hết hạn.");
        Require(subtotal >= voucher.MinOrder, "Đơn hàng chưa đạt giá trị tối thiểu của mã ưu đãi.");
        Require(!voucher.UsageLimit.HasValue || voucher.UsedCount < voucher.UsageLimit, "Mã ưu đãi đã hết lượt sử dụng.", 409);
        var discount = voucher.DiscountType == "PERCENT" ? checked(subtotal * voucher.DiscountValue / 100) : voucher.DiscountValue;
        return Math.Min(subtotal, voucher.MaxDiscount.HasValue ? Math.Min(discount, voucher.MaxDiscount.Value) : discount);
    }
    public static string[] Next(Order order)
    {
        if (order.OrderStatus == "PENDING") return order.PaymentStatus == "PAID" ? ["CONFIRMED"] :
            order.PaymentMethod == "COD" ? ["CONFIRMED", "REJECTED", "CANCELLED"] : ["REJECTED", "CANCELLED"];
        if (order.OrderStatus == "CONFIRMED") return order.PaymentStatus == "PAID" ? ["PREPARING"] : ["PREPARING", "CANCELLED"];
        if (order.OrderStatus == "PREPARING") return ["READY"];
        if (order.OrderStatus == "READY") return order.DeliveryType == "PICKUP" ? ["COMPLETED"] : ["DELIVERING"];
        if (order.OrderStatus == "DELIVERING") return ["COMPLETED"];
        return [];
    }
    public static void Transition(Order order, string target)
    {
        Require(Next(order).Contains(target), "Không thể chuyển trạng thái này. Hãy tải lại đơn hoặc kiểm tra thanh toán.", 409);
        order.OrderStatus = target; order.Version++; order.UpdatedAt = DateTimeOffset.UtcNow;
        if (target == "COMPLETED" && order.PaymentMethod == "COD") order.PaymentStatus = "PAID";
    }
    [GeneratedRegex("^0[0-9]{9,10}$")] private static partial Regex PhonePattern();
    [GeneratedRegex("^[A-Za-z0-9_-]{16,80}$")] private static partial Regex KeyPattern();

    public static void SelfTest()
    {
        static void Check(bool ok) { if (!ok) throw new Exception("Domain self-check failed"); }
        Check(Normalize("Trà Đào") == "tra dao"); Check(Phone("+84912345678") == "0912345678");
        var voucher = new Voucher { DiscountType = "PERCENT", DiscountValue = 10, MinOrder = 50000, MaxDiscount = 30000 };
        Check(Discount(voucher, 60000, DateTimeOffset.UtcNow) == 6000);
        Check(Discount(voucher, 900000, DateTimeOffset.UtcNow) == 30000);
        var order = new Order { DeliveryType = "PICKUP" };
        foreach (var status in new[] { "CONFIRMED", "PREPARING", "READY", "COMPLETED" }) Transition(order, status);
        Check(order.PaymentStatus == "PAID" && Next(order).Length == 0);
        var bank = new Order { PaymentMethod = "BANK_TRANSFER" };
        Check(!Next(bank).Contains("CONFIRMED"));
        try { Transition(bank, "COMPLETED"); throw new Exception("Invalid transition accepted"); } catch (ShopError) { }
        voucher.UsageLimit = 1; voucher.UsedCount = 1;
        try { Discount(voucher, 60000, DateTimeOffset.UtcNow); throw new Exception("Exhausted voucher accepted"); } catch (ShopError) { }
        Console.WriteLine("Domain checks PASS: pricing caps, voucher limits, phone, transitions and payment guards.");
    }
}
