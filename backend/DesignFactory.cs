using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QueDrink;

public sealed class DesignFactory : IDesignTimeDbContextFactory<ShopDb>
{
    public ShopDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ShopDb>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=quedrink;Username=quedrink").Options);
}
