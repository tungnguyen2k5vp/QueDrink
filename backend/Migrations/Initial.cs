using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace QueDrink.Migrations;

[DbContext(typeof(ShopDb))]
[Migration("202611060001_AdoptPostgresSchema")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder migration)
    {
        // Adopt the existing Flyway schema without dropping tables or re-seeding existing products.
        var markers = new Dictionary<int, string>
        {
            [1] = "to_regclass('public.users') IS NULL", [2] = "to_regclass('public.products') IS NULL",
            [3] = "TRUE", [4] = "to_regclass('public.orders') IS NULL",
            [5] = "to_regclass('public.password_reset_tokens') IS NULL", [6] = "to_regclass('public.sugar_options') IS NULL",
            [7] = "to_regclass('public.vouchers') IS NULL", [8] = "to_regclass('public.payment_transactions') IS NULL",
            [9] = "to_regclass('public.favorites') IS NULL", [10] = "to_regclass('public.reviews') IS NULL"
        };
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql"))
                     .OrderBy(n => int.Parse(n.Split(".V")[1].Split("__")[0])))
        {
            var version = int.Parse(resource.Split(".V")[1].Split("__")[0]);
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            migration.Sql($"DO $adopt$ BEGIN IF {markers[version]} THEN\n{reader.ReadToEnd()}\nEND IF; END $adopt$;");
        }
        migration.Sql("""
            ALTER TABLE users ADD COLUMN IF NOT EXISTS session_version INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE users ALTER COLUMN password_hash TYPE VARCHAR(255);
            ALTER TABLE orders ADD COLUMN IF NOT EXISTS request_hash VARCHAR(64);
            ALTER TABLE orders ADD COLUMN IF NOT EXISTS version INTEGER NOT NULL DEFAULT 1;
            CREATE INDEX IF NOT EXISTS idx_orders_created_status ON orders(created_at DESC, order_status);
            """);
    }
    protected override void Down(MigrationBuilder migration) =>
        throw new NotSupportedException("This adoption migration cannot drop business data. Restore a verified backup for rollback.");
}
