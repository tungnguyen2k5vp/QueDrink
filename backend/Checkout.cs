using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace QueDrink;

public sealed record ItemRequest(Guid ProductId, Guid? SizeId, Guid? SugarOptionId, Guid? IceOptionId,
    [property: MaxLength(5)] Guid[]? ToppingIds, [property: MaxLength(300)] string? Note,
    [property: Range(1, 20)] int Quantity);
public sealed record QuoteRequest([property: Required, MinLength(1), MaxLength(30)] ItemRequest[] Items,
    string? DeliveryType, [property: MaxLength(40)] string? VoucherCode);
public sealed record CreateOrderRequest([property: Required, StringLength(120, MinimumLength = 2)] string Name,
    [property: Required, MaxLength(20)] string Phone, [property: MaxLength(500)] string? Address,
    [property: MaxLength(500)] string? Note, [property: Required] string PaymentMethod,
    string? DeliveryType, [property: MaxLength(40)] string? VoucherCode,
    [property: Required, MinLength(1), MaxLength(30)] ItemRequest[] Items);

public sealed class Checkout(ShopDb db, IConfiguration config)
{
    public string PickupAddress => config["STORE_ADDRESS"] ?? "Nhận tại quầy Quê Drink (liên hệ quán để biết địa chỉ)";
    public static string Fulfillment(string? value)
    { var type = string.IsNullOrWhiteSpace(value) ? "DELIVERY" : value; Rules.Require(type is "DELIVERY" or "PICKUP", "Hình thức nhận hàng không hợp lệ."); return type; }
    public async Task<(OrderItem[] Items, long Subtotal, long Shipping, long Discount, string? Voucher)> Price(QuoteRequest input, bool consume)
    {
        Rules.Validate(input); var type = Fulfillment(input.DeliveryType);
        var lines = new List<OrderItem>();
        foreach (var item in input.Items)
        {
            Rules.Require(item is not null, "Dòng hàng không hợp lệ."); Rules.Validate(item!);
            IQueryable<Product> query = consume
                ? db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id={item!.ProductId} FOR SHARE") : db.Products;
            var product = await query.Include(p => p.Category).Include(p => p.Sizes).Include(p => p.Sugars)
                .Include(p => p.Ices).Include(p => p.Toppings).AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == item!.ProductId && p.Available && p.DeletedAt == null && p.Category.Active);
            Rules.Require(product is not null, "Món không tồn tại hoặc đã ngừng bán.", 409);
            var size = product!.Sizes.Where(s => s.Available).OrderBy(s => s.PriceDelta)
                .FirstOrDefault(s => item!.SizeId is null || s.Id == item.SizeId);
            var sugar = product.Sugars.Where(s => s.Available).OrderBy(s => s.SortOrder)
                .FirstOrDefault(s => item!.SugarOptionId is null || s.Id == item.SugarOptionId);
            var ice = product.Ices.Where(s => s.Available).OrderBy(s => s.SortOrder)
                .FirstOrDefault(s => item!.IceOptionId is null || s.Id == item.IceOptionId);
            Rules.Require(size is not null && sugar is not null && ice is not null, "Tùy chọn không thuộc món hoặc đã ngừng phục vụ.", 409);
            var toppingIds = item!.ToppingIds ?? [];
            Rules.Require(toppingIds.Distinct().Count() == toppingIds.Length, "Topping bị trùng lặp.");
            var toppings = product.Toppings.Where(t => t.Available && toppingIds.Contains(t.Id)).OrderBy(t => t.Name).ToArray();
            Rules.Require(toppings.Length == toppingIds.Length, "Topping không thuộc món hoặc đã ngừng phục vụ.", 409);
            var unitPrice = checked((product.SalePrice ?? product.BasePrice) + size!.PriceDelta + toppings.Sum(t => t.Price));
            Rules.Require(unitPrice >= 0, "Giá món chưa được cấu hình hợp lệ.", 409);
            lines.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = unitPrice,
                Quantity = item.Quantity, LineTotal = checked(unitPrice * item.Quantity), SizeName = size.Name,
                SugarName = sugar!.Name, IceName = ice!.Name, ToppingsSummary = string.Join(", ", toppings.Select(t => t.Name)), ItemNote = item.Note?.Trim() });
        }
        var subtotal = lines.Sum(l => l.LineTotal);
        long discount = 0; string? code = null;
        if (!string.IsNullOrWhiteSpace(input.VoucherCode))
        {
            code = input.VoucherCode.Trim().ToUpperInvariant();
            var vouchers = consume ? db.Vouchers.FromSqlInterpolated($"SELECT * FROM vouchers WHERE code={code} FOR UPDATE") : db.Vouchers;
            var voucher = await vouchers.SingleOrDefaultAsync(v => v.Code == code) ?? throw new ShopError(400, "Mã ưu đãi không hợp lệ.");
            discount = Rules.Discount(voucher, subtotal, DateTimeOffset.UtcNow);
            if (consume) voucher.UsedCount++;
        }
        var freeFrom = config.GetValue<long?>("SHIPPING_FREE_FROM") ?? 150000;
        var shipping = type == "PICKUP" || subtotal >= freeFrom ? 0 : config.GetValue<long?>("SHIPPING_FEE") ?? 10000;
        return (lines.ToArray(), subtotal, shipping, discount, code);
    }
    public async Task ReleaseVoucher(Order order)
    {
        if (order.VoucherCode is null) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE vouchers SET used_count=GREATEST(0,used_count-1) WHERE code={order.VoucherCode}");
    }
    public static async Task LockKey(ShopDb db, string key) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 42))");
}
