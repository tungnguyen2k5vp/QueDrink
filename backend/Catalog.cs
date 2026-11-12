using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace QueDrink;

public sealed record SizeInput([property: Required, MaxLength(20)] string Name, [property: Range(0, 1000000)] long Price);
public sealed record ProductChange(string? Name, Guid? CategoryId, string? Description, string? ImageUrl,
    long? BasePrice, long? SalePrice, bool ClearSalePrice = false, bool? Available = null, bool? Featured = null,
    bool? BestSeller = null, SizeInput[]? Sizes = null, Guid[]? ToppingIds = null);
public sealed record CategoryInput([property: Required, StringLength(100, MinimumLength = 2)] string Name, bool Active = true, int SortOrder = 0);
public sealed record VoucherInput(string? Code, string? DiscountType, long? DiscountValue, long? MinOrder,
    long? MaxDiscount, int? UsageLimit, bool? Active, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    bool ClearMaxDiscount = false, bool ClearUsageLimit = false, bool ClearStartsAt = false, bool ClearEndsAt = false);
public sealed record ToppingInput([property: Required, StringLength(100, MinimumLength = 2)] string Name,
    [property: Range(0, 1000000)] long Price, bool Available = true);
public sealed record ReviewInput(Guid ProductId, Guid OrderId, [property: Range(1, 5)] int Rating,
    [property: MaxLength(1000)] string? Comment);

public static class CatalogEndpoints
{
    public static object View(Product product) => new { product.Id, product.Name, product.Slug, product.Description,
        product.ImageUrl, categoryName = product.Category.Name, categorySlug = product.Category.Slug,
        product.CategoryId, category = product.Category.Name, product.BasePrice, product.SalePrice,
        product.Available, product.BestSeller, product.Featured };
    public static IQueryable<Product> Details(ShopDb db) => db.Products.Include(p => p.Category).Include(p => p.Sizes)
        .Include(p => p.Sugars).Include(p => p.Ices).Include(p => p.Toppings).AsSplitQuery();
    public static object Detail(Product product, bool admin = false) => new { product = View(product),
        sizes = product.Sizes.Where(x => x.Available).OrderBy(x => x.PriceDelta).Select(x => new { x.Id, x.Name, price = x.PriceDelta }),
        toppings = product.Toppings.Where(x => admin || x.Available).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Price }),
        sugarOptions = product.Sugars.Where(x => x.Available).OrderBy(x => x.SortOrder).Select(x => new { x.Id, x.Name }),
        iceOptions = product.Ices.Where(x => x.Available).OrderBy(x => x.SortOrder).Select(x => new { x.Id, x.Name }) };
    public static void MapCatalog(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/store", (IConfiguration config, VnPay pay, Checkout checkout, HttpContext http) =>
            Api.Ok(http, new { name = config["STORE_NAME"] ?? "Quê Drinks", address = checkout.PickupAddress,
                phone = config["STORE_PHONE"], shippingFee = config.GetValue<long?>("SHIPPING_FEE") ?? 10000,
                freeShippingFrom = config.GetValue<long?>("SHIPPING_FREE_FROM") ?? 150000,
                paymentMode = pay.Mode, paymentProvider = pay.Provider, onlinePaymentEnabled = pay.OnlineEnabled,
                mockPaymentEnabled = pay.Provider == "MOCK" }));
        api.MapGet("/categories", async (ShopDb db, HttpContext http) => Api.Ok(http,
            await db.Categories.AsNoTracking().Where(c => c.Active && c.DeletedAt == null).OrderBy(c => c.SortOrder)
                .Select(c => new { c.Id, c.Name, c.Slug }).ToListAsync()));
        api.MapGet("/products", async (ShopDb db, HttpContext http, string? category, string? q, bool? featured) =>
        {
            Rules.Require(q is null || q.Length <= 100, "Từ khóa tối đa 100 ký tự.");
            var query = db.Products.AsNoTracking().Include(p => p.Category).Where(p => p.Available && p.DeletedAt == null && p.Category.Active && p.Category.DeletedAt == null);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category.Slug == category);
            if (!string.IsNullOrWhiteSpace(q)) { var term = Rules.Normalize(q); query = query.Where(p => p.SearchName.Contains(term)); }
            if (featured is true) query = query.Where(p => p.Featured);
            var products = await query.OrderByDescending(p => p.BestSeller).ThenBy(p => p.Name).Take(200).ToListAsync();
            return Api.Ok(http, products.Select(View));
        });
        api.MapGet("/products/{slug}", async (string slug, ShopDb db, HttpContext http) =>
        {
            var product = await Details(db).AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.Available && p.DeletedAt == null && p.Category.Active)
                ?? throw new ShopError(404, "Món không tồn tại hoặc đã ngừng bán.");
            return Api.Ok(http, Detail(product));
        });
        api.MapGet("/products/{productId:guid}/reviews", async (Guid productId, ShopDb db, HttpContext http) => Api.Ok(http,
            await db.Reviews.AsNoTracking().Where(r => r.ProductId == productId).OrderByDescending(r => r.CreatedAt).Take(50)
                .Select(r => new { r.Rating, r.Comment, r.CreatedAt, verifiedPurchase = true }).ToListAsync()));
        var me = api.MapGroup("/me").RequireAuthorization();
        me.MapGet("/favorites", async (ShopDb db, HttpContext http) =>
        { var user = Identity.RequiredUser(http.User); return Api.Ok(http, await db.Favorites.Where(f => f.UserId == user).Select(f => f.ProductId).ToArrayAsync()); });
        me.MapPost("/favorites/{productId:guid}", async (Guid productId, ShopDb db, HttpContext http) =>
        {
            var user = Identity.RequiredUser(http.User);
            Rules.Require(await db.Products.AnyAsync(p => p.Id == productId && p.Available && p.DeletedAt == null), "Không tìm thấy món.", 404);
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO favorites(id,user_id,product_id) VALUES({id},{user},{productId}) ON CONFLICT(user_id,product_id) DO NOTHING");
            return Api.Ok(http, await db.Favorites.Where(f => f.UserId == user).Select(f => f.ProductId).ToArrayAsync(), "Đã lưu món yêu thích.", 201);
        });
        me.MapDelete("/favorites/{productId:guid}", async (Guid productId, ShopDb db, HttpContext http) =>
        { var user = Identity.RequiredUser(http.User); await db.Favorites.Where(f => f.UserId == user && f.ProductId == productId).ExecuteDeleteAsync(); return Results.NoContent(); });
        me.MapGet("/loyalty", async (ShopDb db, HttpContext http) =>
        {
            var user = Identity.RequiredUser(http.User);
            return Api.Ok(http, new { points = await db.Loyalty.Where(l => l.UserId == user).SumAsync(l => (int?)l.Points) ?? 0,
                transactions = await db.Loyalty.AsNoTracking().Where(l => l.UserId == user).OrderByDescending(l => l.CreatedAt).Take(20)
                    .Select(l => new { l.Points, l.Reason, l.CreatedAt }).ToListAsync() });
        });
        me.MapPost("/reviews", async (ReviewInput input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input); var user = Identity.RequiredUser(http.User);
            Rules.Require(!await db.Reviews.AnyAsync(r => r.UserId == user && r.OrderId == input.OrderId && r.ProductId == input.ProductId), "Bạn đã đánh giá món này trong đơn hàng này.", 409);
            Rules.Require(await db.Orders.AnyAsync(o => o.Id == input.OrderId && o.UserId == user && o.OrderStatus == "COMPLETED"
                && o.PaymentStatus == "PAID" && o.Items.Any(i => i.ProductId == input.ProductId)), "Chỉ đánh giá món đã mua từ đơn hoàn thành của bạn.", 403);
            var review = new Review { UserId = user, OrderId = input.OrderId, ProductId = input.ProductId, Rating = input.Rating, Comment = input.Comment?.Trim() };
            db.Reviews.Add(review); await db.SaveChangesAsync();
            return Api.Ok(http, new { review.Rating, review.Comment, review.CreatedAt }, "Cảm ơn đánh giá của bạn.", 201);
        });
        me.MapGet("/reviews", async (ShopDb db, HttpContext http) =>
        {
            var user = Identity.RequiredUser(http.User);
            return Api.Ok(http, await db.Reviews.AsNoTracking().Where(r => r.UserId == user)
                .Select(r => new { r.OrderId, r.ProductId, r.Rating }).ToListAsync());
        });

        var admin = api.MapGroup("/admin").RequireAuthorization(policy => policy.RequireRole("ADMIN"));
        admin.MapGet("/categories", async (ShopDb db, HttpContext http) => Api.Ok(http, await db.Categories.AsNoTracking()
            .Where(c => c.DeletedAt == null).OrderBy(c => c.SortOrder).ToListAsync()));
        admin.MapPost("/categories", async (CategoryInput input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input); var slug = Slug(input.Name);
            Rules.Require(slug.Length > 0, "Tên danh mục cần có chữ cái hoặc chữ số.");
            var category = new Category { Name = input.Name.Trim(), Slug = slug, Active = input.Active, SortOrder = input.SortOrder };
            db.Categories.Add(category); db.Log(http, "CATEGORY_CREATE", "CATEGORY", category.Id.ToString()); await db.SaveChangesAsync();
            return Api.Ok(http, category, "Đã thêm nhóm món.", 201);
        });
        admin.MapPatch("/categories/{id:guid}", async (Guid id, CategoryInput input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input); var category = await db.Categories.FindAsync(id) ?? throw new ShopError(404, "Không tìm thấy nhóm món.");
            Rules.Require(input.Name.Trim().Length >= 2, "Tên danh mục cần ít nhất 2 ký tự.");
            category.Name = input.Name.Trim(); category.Active = input.Active; category.SortOrder = input.SortOrder;
            db.Log(http, "CATEGORY_UPDATE", "CATEGORY", id.ToString()); await db.SaveChangesAsync(); return Api.Ok(http, category);
        });
        admin.MapGet("/toppings", async (ShopDb db, HttpContext http) => Api.Ok(http,
            await db.Toppings.AsNoTracking().OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.Price, t.Available }).ToListAsync()));
        admin.MapPost("/toppings", async (ToppingInput input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input); var topping = new Topping { Name = input.Name.Trim(), Price = input.Price, Available = input.Available };
            Rules.Require(topping.Name.Length >= 2, "Tên topping cần ít nhất 2 ký tự.");
            db.Toppings.Add(topping); db.Log(http, "TOPPING_CREATE", "TOPPING", topping.Id.ToString());
            await db.SaveChangesAsync(); return Api.Ok(http, new { topping.Id, topping.Name, topping.Price, topping.Available }, "Đã thêm topping.", 201);
        });
        admin.MapPatch("/toppings/{id:guid}", async (Guid id, ToppingInput input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input); var topping = await db.Toppings.FindAsync(id) ?? throw new ShopError(404, "Không tìm thấy topping.");
            Rules.Require(input.Name.Trim().Length >= 2, "Tên topping cần ít nhất 2 ký tự.");
            topping.Name = input.Name.Trim(); topping.Price = input.Price; topping.Available = input.Available;
            db.Log(http, "TOPPING_UPDATE", "TOPPING", id.ToString()); await db.SaveChangesAsync();
            return Api.Ok(http, new { topping.Id, topping.Name, topping.Price, topping.Available });
        });
        admin.MapGet("/products", async (ShopDb db, HttpContext http) =>
            Api.Ok(http, (await db.Products.AsNoTracking().Include(p => p.Category).Where(p => p.DeletedAt == null).OrderBy(p => p.Name).ToListAsync()).Select(View)));
        admin.MapGet("/products/{id:guid}", async (Guid id, ShopDb db, HttpContext http) =>
            Api.Ok(http, Detail(await Details(db).SingleOrDefaultAsync(p => p.Id == id && p.DeletedAt == null) ?? throw new ShopError(404, "Không tìm thấy món."), true)));
        admin.MapPost("/products", async (ProductChange input, ShopDb db, HttpContext http) =>
        {
            Rules.Require(input.Name is not null && input.CategoryId.HasValue && input.BasePrice.HasValue, "Cần tên, nhóm món và giá gốc.");
            var product = new Product(); await ApplyProduct(product, input, db);
            product.Slug = Slug(product.Name) + "-" + product.Id.ToString("N")[..6];
            product.Sizes = input.Sizes is null ? [new() { Name = "M", PriceDelta = 0 }, new() { Name = "L", PriceDelta = 5000 }] : product.Sizes;
            product.Sugars = new[] { "100% đường", "70% đường", "50% đường", "30% đường", "Không đường" }
                .Select((name, i) => new SugarOption { Name = name, SortOrder = i }).ToList();
            product.Ices = new[] { "100% đá", "70% đá", "50% đá", "Ít đá", "Không đá" }
                .Select((name, i) => new IceOption { Name = name, SortOrder = i }).ToList();
            db.Products.Add(product); db.Log(http, "PRODUCT_CREATE", "PRODUCT", product.Id.ToString()); await db.SaveChangesAsync();
            product.Category = await db.Categories.SingleAsync(c => c.Id == product.CategoryId);
            return Api.Ok(http, View(product), "Đã thêm món.", 201);
        });
        admin.MapPatch("/products/{id:guid}", async (Guid id, ProductChange input, ShopDb db, HttpContext http) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var product = await db.Products.FromSqlInterpolated($"SELECT * FROM products WHERE id={id} FOR UPDATE")
                .Include(p => p.Category).Include(p => p.Sizes).Include(p => p.Toppings).AsSplitQuery().SingleOrDefaultAsync()
                ?? throw new ShopError(404, "Không tìm thấy món.");
            await ApplyProduct(product, input, db); product.UpdatedAt = DateTimeOffset.UtcNow;
            db.Log(http, "PRODUCT_UPDATE", "PRODUCT", id.ToString()); await db.SaveChangesAsync(); await tx.CommitAsync();
            product.Category = await db.Categories.SingleAsync(c => c.Id == product.CategoryId);
            return Api.Ok(http, View(product), "Đã cập nhật món.");
        });
        admin.MapDelete("/products/{id:guid}", async (Guid id, ShopDb db, HttpContext http) =>
        {
            var product = await db.Products.FindAsync(id) ?? throw new ShopError(404, "Không tìm thấy món.");
            product.DeletedAt = DateTimeOffset.UtcNow; product.Available = false;
            db.Log(http, "PRODUCT_ARCHIVE", "PRODUCT", id.ToString()); await db.SaveChangesAsync(); return Results.NoContent();
        });
        admin.MapGet("/vouchers", async (ShopDb db, HttpContext http) => Api.Ok(http, await db.Vouchers.AsNoTracking().OrderBy(v => v.Code).ToListAsync()));
        admin.MapPost("/vouchers", async (VoucherInput input, ShopDb db, HttpContext http) =>
        {
            Rules.Require(input.Code is not null && input.DiscountType is not null && input.DiscountValue.HasValue, "Cần mã và hình thức/giá trị giảm.");
            var voucher = new Voucher { Code = input.Code!.Trim().ToUpperInvariant() }; ApplyVoucher(voucher, input);
            db.Vouchers.Add(voucher); db.Log(http, "VOUCHER_CREATE", "VOUCHER", voucher.Code); await db.SaveChangesAsync();
            return Api.Ok(http, voucher, "Đã tạo mã ưu đãi.", 201);
        });
        admin.MapPatch("/vouchers/{code}", async (string code, VoucherInput input, ShopDb db, HttpContext http) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var key = code.ToUpperInvariant();
            var voucher = await db.Vouchers.FromSqlInterpolated($"SELECT * FROM vouchers WHERE code={key} FOR UPDATE").SingleOrDefaultAsync()
                ?? throw new ShopError(404, "Không tìm thấy mã ưu đãi.");
            ApplyVoucher(voucher, input); db.Log(http, "VOUCHER_UPDATE", "VOUCHER", code);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Api.Ok(http, voucher, "Đã cập nhật ưu đãi.");
        });
    }
    private static string Slug(string name) => string.Join('-', Rules.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => new string(s.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray())));
    private static async Task ApplyProduct(Product product, ProductChange input, ShopDb db)
    {
        if (input.Name is not null) { Rules.Require(input.Name.Trim().Length is >= 2 and <= 160, "Tên món dài 2–160 ký tự."); product.Name = input.Name.Trim(); product.SearchName = Rules.Normalize(product.Name); }
        if (input.CategoryId is Guid category) { Rules.Require(await db.Categories.AnyAsync(c => c.Id == category && c.Active && c.DeletedAt == null), "Nhóm món không hợp lệ."); product.CategoryId = category; }
        if (input.BasePrice.HasValue) { Rules.Require(input.BasePrice is >= 0 and <= 1000000000, "Giá gốc không hợp lệ."); product.BasePrice = input.BasePrice.Value; }
        if (input.ClearSalePrice) product.SalePrice = null; else if (input.SalePrice.HasValue) product.SalePrice = input.SalePrice;
        Rules.Require(product.SalePrice is null || product.SalePrice >= 0 && product.SalePrice <= product.BasePrice, "Giá ưu đãi phải từ 0 đến giá gốc.");
        if (input.Description is not null) { Rules.Require(input.Description.Length <= 500, "Mô tả tối đa 500 ký tự."); product.Description = input.Description.Trim(); }
        if (input.ImageUrl is not null) { Rules.Require(input.ImageUrl.Length <= 500 && (input.ImageUrl == "" || input.ImageUrl.StartsWith("/images/") || Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var url) && url.Scheme == "https"), "Ảnh cần là đường dẫn /images/ hoặc HTTPS."); product.ImageUrl = input.ImageUrl; }
        if (input.Available.HasValue) product.Available = input.Available.Value;
        if (input.Featured.HasValue) product.Featured = input.Featured.Value;
        if (input.BestSeller.HasValue) product.BestSeller = input.BestSeller.Value;
        if (input.Sizes is not null)
        {
            Rules.Require(input.Sizes.All(s => s is not null && !string.IsNullOrWhiteSpace(s.Name)), "Size cần có tên.");
            Rules.Require(input.Sizes.Length is >= 1 and <= 8 && input.Sizes.Select(s => s.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == input.Sizes.Length, "Cần 1–8 size không trùng tên.");
            foreach (var size in input.Sizes) Rules.Validate(size);
            foreach (var removed in product.Sizes.Where(s => !input.Sizes.Any(i => i.Name.Trim() == s.Name)).ToArray())
            { db.Sizes.Remove(removed); product.Sizes.Remove(removed); }
            foreach (var inputSize in input.Sizes)
            {
                var size = product.Sizes.FirstOrDefault(s => s.Name == inputSize.Name.Trim());
                if (size is null)
                {
                    size = new ProductSize { ProductId = product.Id, Name = inputSize.Name.Trim(), PriceDelta = inputSize.Price };
                    db.Sizes.Add(size); product.Sizes.Add(size);
                }
                else { size.PriceDelta = inputSize.Price; size.Available = true; }
            }
        }
        if (input.ToppingIds is not null)
        {
            Rules.Require(input.ToppingIds.Length <= 20 && input.ToppingIds.Distinct().Count() == input.ToppingIds.Length, "Danh sách topping không hợp lệ.");
            var toppings = await db.Toppings.Where(t => input.ToppingIds.Contains(t.Id)).ToListAsync();
            Rules.Require(toppings.Count == input.ToppingIds.Length, "Topping không tồn tại."); product.Toppings = toppings;
        }
    }
    private static void ApplyVoucher(Voucher voucher, VoucherInput input)
    {
        Rules.Require(System.Text.RegularExpressions.Regex.IsMatch(voucher.Code, "^[A-Z0-9_-]{3,40}$"), "Mã ưu đãi dài 3–40 ký tự A–Z, 0–9, _ hoặc -.");
        if (input.DiscountType is not null) voucher.DiscountType = input.DiscountType;
        if (input.DiscountValue.HasValue) voucher.DiscountValue = input.DiscountValue.Value;
        if (input.MinOrder.HasValue) voucher.MinOrder = input.MinOrder.Value;
        if (input.ClearMaxDiscount) voucher.MaxDiscount = null; else if (input.MaxDiscount.HasValue) voucher.MaxDiscount = input.MaxDiscount;
        if (input.ClearUsageLimit) voucher.UsageLimit = null; else if (input.UsageLimit.HasValue) voucher.UsageLimit = input.UsageLimit;
        if (input.Active.HasValue) voucher.Active = input.Active.Value;
        if (input.ClearStartsAt) voucher.StartsAt = null; else if (input.StartsAt.HasValue) voucher.StartsAt = input.StartsAt.Value.ToUniversalTime();
        if (input.ClearEndsAt) voucher.EndsAt = null; else if (input.EndsAt.HasValue) voucher.EndsAt = input.EndsAt.Value.ToUniversalTime();
        Rules.Require(voucher.DiscountType is "FIXED" or "PERCENT" && voucher.DiscountValue > 0 && voucher.DiscountValue <= 1000000000 &&
            (voucher.DiscountType != "PERCENT" || voucher.DiscountValue <= 100) && voucher.MinOrder >= 0 && voucher.MinOrder <= 1000000000 &&
            (voucher.MaxDiscount is null || voucher.MaxDiscount > 0) && (voucher.UsageLimit is null || voucher.UsageLimit >= voucher.UsedCount && voucher.UsageLimit > 0) &&
            (voucher.StartsAt is null || voucher.EndsAt is null || voucher.StartsAt < voucher.EndsAt), "Điều kiện mã ưu đãi không hợp lệ.");
    }
}
