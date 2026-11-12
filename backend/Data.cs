using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace QueDrink;

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ResetToken> ResetTokens => Set<ResetToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSize> Sizes => Set<ProductSize>();
    public DbSet<SugarOption> Sugars => Set<SugarOption>();
    public DbSet<IceOption> Ices => Set<IceOption>();
    public DbSet<Topping> Toppings => Set<Topping>();
    public DbSet<ProductTopping> ProductToppings => Set<ProductTopping>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<LoyaltyEntry> Loyalty => Set<LoyaltyEntry>();
    public DbSet<AuditLog> Audit => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        model.Entity<UserRole>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<UserRole>().HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId);
        model.Entity<RefreshToken>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<ResetToken>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<ProductTopping>().HasKey(x => new { x.ProductId, x.ToppingId });
        model.Entity<Product>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        model.Entity<Product>().HasMany(x => x.Sizes).WithOne().HasForeignKey(x => x.ProductId);
        model.Entity<Product>().HasMany(x => x.Sugars).WithOne().HasForeignKey(x => x.ProductId);
        model.Entity<Product>().HasMany(x => x.Ices).WithOne().HasForeignKey(x => x.ProductId);
        model.Entity<Product>().HasMany(x => x.Toppings).WithMany().UsingEntity<ProductTopping>(
            right => right.HasOne<Topping>().WithMany().HasForeignKey(x => x.ToppingId),
            left => left.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId));
        model.Entity<Order>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId);
        model.Entity<Order>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<OrderItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<Payment>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<Payment>().HasIndex(x => new { x.Provider, x.GatewayTransactionId }).IsUnique();
        model.Entity<Favorite>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<Favorite>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        model.Entity<Review>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<Review>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        model.Entity<Review>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<LoyaltyEntry>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        model.Entity<LoyaltyEntry>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
        model.Entity<SugarOption>().ToTable("sugar_options");
        model.Entity<IceOption>().ToTable("ice_options");
    }

    public void Log(HttpContext http, string action, string entity, string? id) => Audit.Add(new AuditLog
    {
        ActorId = Identity.UserId(http.User), Action = action, EntityType = entity,
        EntityId = id, RequestId = http.TraceIdentifier
    });
}

[Table("users")]
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public int SessionVersion { get; set; }
}
[Table("roles")]
public sealed class Role { public Guid Id { get; set; } public string Name { get; set; } = ""; }
[Table("user_roles")]
public sealed class UserRole { public Guid UserId { get; set; } public Guid RoleId { get; set; } }
[Table("refresh_tokens")]
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
}
[Table("password_reset_tokens")]
public sealed class ResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
[Table("categories")]
public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
[Table("products")]
public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string SearchName { get; set; } = "";
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public long BasePrice { get; set; }
    public long? SalePrice { get; set; }
    public bool Available { get; set; } = true;
    public bool Featured { get; set; }
    public bool BestSeller { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public List<ProductSize> Sizes { get; set; } = [];
    public List<SugarOption> Sugars { get; set; } = [];
    public List<IceOption> Ices { get; set; } = [];
    public List<Topping> Toppings { get; set; } = [];
}
[Table("product_sizes")]
public sealed class ProductSize
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid ProductId { get; set; }
    public string Name { get; set; } = ""; public long PriceDelta { get; set; } public bool Available { get; set; } = true;
}
public sealed class SugarOption
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid ProductId { get; set; }
    public string Name { get; set; } = ""; public int SortOrder { get; set; } public bool Available { get; set; } = true;
}
public sealed class IceOption
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid ProductId { get; set; }
    public string Name { get; set; } = ""; public int SortOrder { get; set; } public bool Available { get; set; } = true;
}
[Table("toppings")]
public sealed class Topping
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = "";
    public long Price { get; set; } public string? ImageUrl { get; set; } public bool Available { get; set; } = true;
}
[Table("product_toppings")]
public sealed class ProductTopping { public Guid ProductId { get; set; } public Guid ToppingId { get; set; } }
[Table("orders")]
public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid? UserId { get; set; }
    public string OrderCode { get; set; } = ""; public string GuestName { get; set; } = "";
    public string GuestPhone { get; set; } = ""; public string DeliveryAddress { get; set; } = "";
    public string? Note { get; set; } public long Subtotal { get; set; } public long ShippingFee { get; set; }
    public long Discount { get; set; } public long Total { get; set; } public string? VoucherCode { get; set; }
    public string DeliveryType { get; set; } = "DELIVERY"; public string? PickupLocation { get; set; }
    public string? IdempotencyKey { get; set; } public string? RequestHash { get; set; }
    public string PaymentMethod { get; set; } = "COD"; public string PaymentStatus { get; set; } = "UNPAID";
    public string OrderStatus { get; set; } = "PENDING"; public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OrderItem> Items { get; set; } = [];
}
[Table("order_items")]
public sealed class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrderId { get; set; } public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = ""; public long UnitPrice { get; set; } public int Quantity { get; set; }
    public long LineTotal { get; set; } public string? SizeName { get; set; } public string? SugarName { get; set; }
    public string? IceName { get; set; } public string? ToppingsSummary { get; set; } public string? ItemNote { get; set; }
}
[Table("vouchers")]
public sealed class Voucher
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Code { get; set; } = "";
    public string DiscountType { get; set; } = "FIXED"; public long DiscountValue { get; set; }
    public long MinOrder { get; set; } public long? MaxDiscount { get; set; } public int? UsageLimit { get; set; }
    public int UsedCount { get; set; } public bool Active { get; set; } = true;
    public DateTimeOffset? StartsAt { get; set; } public DateTimeOffset? EndsAt { get; set; }
}
[Table("payment_transactions")]
public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrderId { get; set; }
    public string Provider { get; set; } = "MOCK"; public string ProviderReference { get; set; } = "";
    public long Amount { get; set; } public string Status { get; set; } = "PENDING";
    public string IdempotencyKey { get; set; } = "";
    public string? GatewayTransactionId { get; set; }
    public string? GatewayResponseCode { get; set; }
    public string? GatewayTransactionStatus { get; set; }
    public bool NeedsReview { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
[Table("favorites")]
public sealed class Favorite
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public Guid ProductId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
[Table("reviews")]
public sealed class Review
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public Guid ProductId { get; set; }
    public Guid OrderId { get; set; } public int Rating { get; set; } public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
[Table("loyalty_transactions")]
public sealed class LoyaltyEntry
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public Guid OrderId { get; set; }
    public int Points { get; set; } public string Reason { get; set; } = "Hoàn thành đơn hàng";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
[Table("audit_logs")]
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid? ActorId { get; set; }
    public string Action { get; set; } = ""; public string EntityType { get; set; } = "";
    public string? EntityId { get; set; } public string? RequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
