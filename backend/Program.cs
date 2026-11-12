using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using QueDrink;

if (args.Contains("--self-test")) { Rules.SelfTest(); IntegrationChecks.Run(); return; }
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["INTEGRATIONS_FILE"] is string integrationsFile)
    builder.Configuration.AddJsonFile(integrationsFile, optional: false).AddEnvironmentVariables();
builder.Logging.ClearProviders().AddSimpleConsole(options => options.SingleLine = true);
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddDbContext<ShopDb>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Database for PostgreSQL.")));
builder.Services.AddSingleton<Identity>();
builder.Services.AddSingleton<Gmail>();
builder.Services.AddSingleton<VnPay>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(
    builder.Configuration["DATA_PROTECTION_PATH"] ?? Path.Combine(builder.Environment.ContentRootPath, ".keys")));
builder.Services.AddScoped<Checkout>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var value in (builder.Configuration["TRUSTED_PROXY_IPS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!System.Net.IPAddress.TryParse(value, out var address)) throw new InvalidOperationException("TRUSTED_PROXY_IPS contains an invalid IP address.");
        options.KnownProxies.Add(address);
    }
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration["FRONTEND_URL"] ?? "http://localhost:3000")
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
    .WithHeaders("Content-Type", "Authorization", "Idempotency-Key", "X-Request-ID", "X-Payment-Signature")
    .WithExposedHeaders("X-Request-ID")));
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(Api.Envelope(context.HttpContext, false, "Bạn thao tác quá nhanh. Vui lòng thử lại sau.", null), token);
    };
    foreach (var (name, count) in new[] { ("auth", 25), ("lookup", 40), ("email", 3) })
        options.AddPolicy(name, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = count, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<Identity>((options, identity) =>
{
    options.MapInboundClaims = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(identity.Secret)),
        ValidateIssuer = true, ValidIssuer = "quedrink", ValidateAudience = true, ValidAudience = "quedrink-web",
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15), ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<ShopDb>();
            var userId = Identity.UserId(context.Principal!);
            var sessionId = Guid.TryParse(context.Principal!.FindFirstValue("sid"), out var sid) ? sid : Guid.Empty;
            var version = int.TryParse(context.Principal!.FindFirstValue("sv"), out var sv) ? sv : -1;
            var valid = await (from user in db.Users join token in db.RefreshTokens on user.Id equals token.UserId
                               where user.Id == userId && user.Active && user.DeletedAt == null && user.SessionVersion == version
                                   && token.Id == sessionId && token.RevokedAt == null && token.ExpiresAt > DateTimeOffset.UtcNow
                               select user.Id).AnyAsync();
            if (!valid) context.Fail("Session revoked");
        },
        OnChallenge = async context => { context.HandleResponse(); context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(Api.Envelope(context.HttpContext, false, "Vui lòng đăng nhập lại.", null)); },
        OnForbidden = async context => { context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(Api.Envelope(context.HttpContext, false, "Bạn không có quyền thực hiện thao tác này.", null)); }
    };
});
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (http, next) =>
{
    http.Response.Headers["X-Request-ID"] = http.TraceIdentifier;
    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers["Referrer-Policy"] = "no-referrer";
    http.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { }
    catch (Exception error) when (!http.Response.HasStarted)
    {
        var (status, message) = error switch
        {
            ShopError shop => (shop.Status, shop.Message),
            ValidationException validation => (400, validation.Message),
            BadHttpRequestException => (400, "Dữ liệu gửi lên không hợp lệ."),
            OverflowException => (400, "Giá trị vượt giới hạn cho phép."),
            DbUpdateConcurrencyException => (409, "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại thao tác."),
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => (409, "Dữ liệu vừa thay đổi hoặc đã tồn tại. Vui lòng tải lại."),
            _ => (500, "Không thể xử lý yêu cầu. Vui lòng thử lại.")
        };
        if (status == 500) app.Logger.LogError(error, "Request failed {RequestId}", http.TraceIdentifier);
        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(Api.Envelope(http, false, message, null));
    }
});
app.UseCors(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/actuator/health", async (ShopDb db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "UP" }) : Results.Json(new { status = "DOWN" }, statusCode: 503));
app.MapAuth(); app.MapCatalog(); app.MapOrders(); app.MapPayments();
app.MapGet("/api/v1/admin/integrations", (Gmail mail, VnPay pay, HttpContext http) => Api.Ok(http,
    new { gmailConfigured = mail.Enabled, paymentProvider = pay.Provider, paymentMode = pay.Mode,
        ipnUrl = pay.Site + "/api/v1/payments/vnpay/ipn", returnUrl = pay.Site + "/api/v1/payments/vnpay/return" }))
    .RequireAuthorization(policy => policy.RequireRole("ADMIN"));
app.MapPost("/api/v1/admin/integrations/gmail/test", async (Gmail mail, HttpContext http) =>
{
    Rules.Require(await mail.SendTest(), "Gmail chưa gửi được thư. Kiểm tra App Password, kết nối và giới hạn gửi.", 502);
    return Api.Ok(http, null, "Đã gửi thư kiểm tra đến chính Gmail được cấu hình.");
}).RequireAuthorization(policy => policy.RequireRole("ADMIN")).RequireRateLimiting("email");
app.Services.GetRequiredService<VnPay>().Validate();
app.Services.GetRequiredService<Gmail>().Validate();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
    await db.Database.MigrateAsync();
    await Bootstrap.Admin(db, app.Configuration, app.Environment);
}
await app.RunAsync();

namespace QueDrink
{
    public static class Api
    {
        public static object Envelope(HttpContext http, bool success, string message, object? data) =>
            new { success, message, data, timestamp = DateTimeOffset.UtcNow, requestId = http.TraceIdentifier };
        public static IResult Ok(HttpContext http, object? data, string message = "Thành công.", int status = 200) =>
            Results.Json(Envelope(http, true, message, data), statusCode: status);
    }
    public static class Bootstrap
    {
        public static async Task Admin(ShopDb db, IConfiguration config, IWebHostEnvironment env)
        {
            var email = config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant(); var password = config["ADMIN_PASSWORD"];
            if (email is null || password is null) return;
            Rules.Require(new EmailAddressAttribute().IsValid(email) && password.Length >= 12 && Encoding.UTF8.GetByteCount(password) <= 72,
                "Admin seed requires valid email and a password of 12–72 UTF-8 bytes.", 500);
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new User { Email = email, DisplayName = "Quản trị viên", PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 11) };
                db.Users.Add(user);
            }
            var role = await db.Roles.SingleAsync(r => r.Name == "ADMIN");
            if (!await db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == role.Id))
                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            await db.SaveChangesAsync();
        }
    }
}
