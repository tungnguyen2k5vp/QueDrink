using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace QueDrink;

public sealed class Identity(IConfiguration config, IWebHostEnvironment environment)
{
    public string Secret { get; } = ReadSecret(config, environment, "JWT_SECRET");
    public string PaymentSecret { get; } = ReadSecret(config, environment, "PAYMENT_CALLBACK_SECRET");
    public string DummyHash { get; } = BCrypt.Net.BCrypt.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), 11);
    public static Guid? UserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public static Guid RequiredUser(ClaimsPrincipal user) => UserId(user) ?? throw new ShopError(401, "Vui lòng đăng nhập.");
    private static string ReadSecret(IConfiguration config, IWebHostEnvironment environment, string name)
    {
        var value = config[name];
        if (string.IsNullOrWhiteSpace(value) && environment.IsDevelopment()) return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        Rules.Require(value is { Length: >= 48 }, $"Cần cấu hình {name} ít nhất 48 ký tự.", 500);
        return value!;
    }
    public async Task<object> Issue(ShopDb db, User user)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(48)).ToLowerInvariant();
        var refresh = new RefreshToken { UserId = user.Id, TokenHash = Rules.Hash(raw), ExpiresAt = DateTimeOffset.UtcNow.AddDays(30) };
        db.RefreshTokens.Add(refresh);
        var roles = await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id
                           where ur.UserId == user.Id select role.Name).ToArrayAsync();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()), new("sid", refresh.Id.ToString()),
            new("sv", user.SessionVersion.ToString()), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var jwt = new JwtSecurityToken("quedrink", "quedrink-web", claims, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        await db.SaveChangesAsync();
        return new { accessToken = new JwtSecurityTokenHandler().WriteToken(jwt), refreshToken = raw, expiresIn = 900,
            user = new { user.Id, user.DisplayName, user.Email, user.Phone, roles } };
    }
    public string Tracking(Order order)
    {
        var expires = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        return expires + "." + Sign($"{order.Id}|{order.GuestPhone}|{expires}", Secret);
    }
    public bool CanTrack(Order order, string? token)
    {
        var parts = (token ?? "").Split('.');
        return parts.Length == 2 && long.TryParse(parts[0], out var expires) && expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            && Equal(Sign($"{order.Id}|{order.GuestPhone}|{expires}", Secret), parts[1]);
    }
    public static string Sign(string payload, string secret) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    public static bool Equal(string expected, string? actual) => actual is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}

public sealed record RegisterRequest([property: Required, StringLength(120, MinimumLength = 2)] string DisplayName,
    [property: EmailAddress, MaxLength(255)] string? Email, string? Phone,
    [property: Required, StringLength(72, MinimumLength = 10)] string Password);
public sealed record LoginRequest([property: Required, MaxLength(255)] string Identity,
    [property: Required, MaxLength(72)] string Password);
public sealed record RefreshRequest([property: Required, MaxLength(512)] string RefreshToken);
public sealed record ChangePasswordRequest([property: Required, MaxLength(72)] string CurrentPassword,
    [property: Required, StringLength(72, MinimumLength = 10)] string NewPassword);
public sealed record ForgotRequest([property: Required, MaxLength(255)] string Identity);
public sealed record ResetRequest([property: Required, MaxLength(512)] string Token,
    [property: Required, StringLength(72, MinimumLength = 10)] string NewPassword);

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest input, ShopDb db, Identity identity, HttpContext http) =>
        {
            Rules.Validate(input);
            Rules.Require(input.DisplayName.Trim().Length >= 2, "Tên hiển thị cần ít nhất 2 ký tự.");
            Rules.Require(Encoding.UTF8.GetByteCount(input.Password) <= 72, "Mật khẩu tối đa 72 byte UTF-8.");
            var email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim().ToLowerInvariant();
            var phone = string.IsNullOrWhiteSpace(input.Phone) ? null : Rules.Phone(input.Phone);
            Rules.Require(email is not null || phone is not null, "Cần email hoặc số điện thoại.");
            Rules.Require(!await db.Users.AnyAsync(u => (email != null && u.Email == email) || (phone != null && u.Phone == phone)),
                "Email hoặc số điện thoại đã được sử dụng.", 409);
            var user = new User { DisplayName = input.DisplayName.Trim(), Email = email, Phone = phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(input.Password, 11) };
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Users.Add(user);
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = Guid.Parse("00000000-0000-0000-0000-000000000001") });
            await db.SaveChangesAsync();
            var result = await identity.Issue(db, user);
            await tx.CommitAsync();
            return Api.Ok(http, result, "Đã tạo tài khoản.", 201);
        });
        auth.MapPost("/login", async (LoginRequest input, ShopDb db, Identity identity, HttpContext http) =>
        {
            Rules.Validate(input);
            var value = input.Identity.Trim().ToLowerInvariant();
            if (value.StartsWith("+84")) value = "0" + value[3..];
            var legacyPhone = value.StartsWith('0') ? "+84" + value[1..] : value;
            var user = await db.Users.FirstOrDefaultAsync(u => u.DeletedAt == null && (u.Email == value || u.Phone == value || u.Phone == legacyPhone));
            var passwordOk = BCrypt.Net.BCrypt.Verify(input.Password, user?.PasswordHash ?? identity.DummyHash);
            Rules.Require(user is { Active: true } && passwordOk, "Thông tin đăng nhập không chính xác.", 401);
            return Api.Ok(http, await identity.Issue(db, user!), "Đăng nhập thành công.");
        });
        auth.MapPost("/refresh", async (RefreshRequest input, ShopDb db, Identity identity, HttpContext http) =>
        {
            Rules.Validate(input);
            await using var tx = await db.Database.BeginTransactionAsync();
            var hash = Rules.Hash(input.RefreshToken);
            var token = await db.RefreshTokens.FromSqlInterpolated($"SELECT * FROM refresh_tokens WHERE token_hash={hash} FOR UPDATE").FirstOrDefaultAsync();
            Rules.Require(token is not null && token.RevokedAt is null && token.ExpiresAt > DateTimeOffset.UtcNow, "Phiên đăng nhập đã hết hạn.", 401);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token!.UserId && u.Active && u.DeletedAt == null)
                ?? throw new ShopError(401, "Phiên đăng nhập không hợp lệ.");
            token!.RevokedAt = DateTimeOffset.UtcNow;
            var result = await identity.Issue(db, user);
            await tx.CommitAsync();
            return Api.Ok(http, result, "Đã gia hạn phiên.");
        });
        auth.MapPost("/logout", async (RefreshRequest input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input);
            var hash = Rules.Hash(input.RefreshToken);
            await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow));
            return Api.Ok(http, null, "Đã đăng xuất.");
        });
        auth.MapPost("/change-password", async (ChangePasswordRequest input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input);
            Rules.Require(Encoding.UTF8.GetByteCount(input.NewPassword) <= 72, "Mật khẩu tối đa 72 byte UTF-8.");
            await using var tx = await db.Database.BeginTransactionAsync();
            var id = Identity.RequiredUser(http.User);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id={id} FOR UPDATE").SingleAsync();
            Rules.Require(BCrypt.Net.BCrypt.Verify(input.CurrentPassword, user.PasswordHash), "Mật khẩu hiện tại không đúng.");
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(input.NewPassword, 11); user.SessionVersion++; user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Api.Ok(http, null, "Đã đổi mật khẩu. Vui lòng đăng nhập lại trên các thiết bị.");
        }).RequireAuthorization();
        auth.MapPost("/forgot-password", async (ForgotRequest input, ShopDb db, Gmail mail, IConfiguration config, IWebHostEnvironment env, HttpContext http) =>
        {
            Rules.Validate(input);
            var value = input.Identity.Trim().ToLowerInvariant();
            if (value.StartsWith("+84")) value = "0" + value[3..];
            var legacyPhone = value.StartsWith('0') ? "+84" + value[1..] : value;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Active && u.DeletedAt == null && (u.Email == value || u.Phone == value || u.Phone == legacyPhone));
            string? raw = null;
            var remote = http.Connection.RemoteIpAddress;
            var local = remote is not null && (System.Net.IPAddress.IsLoopback(remote) ||
                remote.IsIPv4MappedToIPv6 && System.Net.IPAddress.IsLoopback(remote.MapToIPv4()));
            var expose = env.IsDevelopment() && local && new Uri(Gmail.Site(config)).IsLoopback &&
                !mail.Enabled && config.GetValue<bool>("AUTH_EXPOSE_RESET_TOKEN");
            if (user is not null && (expose || mail.Enabled && !string.IsNullOrWhiteSpace(user.Email)))
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await Checkout.LockKey(db, "reset:" + user.Id);
                if (!mail.Enabled || !await db.ResetTokens.AnyAsync(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(14)))
                {
                    raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                    db.ResetTokens.Add(new ResetToken { UserId = user.Id, TokenHash = Rules.Hash(raw), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) });
                    await db.SaveChangesAsync();
                }
                await tx.CommitAsync();
                if (mail.Enabled && raw is not null) await mail.SendReset(user.Email!, raw);
            }
            return Api.Ok(http, new { accepted = true, resetToken = expose ? raw : null, deliveryEnabled = mail.Enabled },
                mail.Enabled ? "Nếu tài khoản có email, liên kết phục hồi sẽ được gửi đến email đã đăng ký. Hãy kiểm tra cả thư mục Spam." :
                expose ? "Chế độ thử nghiệm: mã phục hồi hiển thị khi tài khoản tồn tại." : "Chức năng gửi email phục hồi chưa được cấu hình; vui lòng liên hệ quản trị.");
        });
        auth.MapPost("/reset-password", async (ResetRequest input, ShopDb db, HttpContext http) =>
        {
            Rules.Validate(input); Rules.Require(Encoding.UTF8.GetByteCount(input.NewPassword) <= 72, "Mật khẩu quá dài.");
            await using var tx = await db.Database.BeginTransactionAsync();
            var hash = Rules.Hash(input.Token);
            var token = await db.ResetTokens.FromSqlInterpolated($"SELECT * FROM password_reset_tokens WHERE token_hash={hash} FOR UPDATE").FirstOrDefaultAsync();
            Rules.Require(token is not null && token.UsedAt is null && token.ExpiresAt > DateTimeOffset.UtcNow, "Mã phục hồi không hợp lệ hoặc đã hết hạn.");
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == token!.UserId && u.Active && u.DeletedAt == null)
                ?? throw new ShopError(400, "Tài khoản không còn khả dụng.");
            token!.UsedAt = DateTimeOffset.UtcNow; user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(input.NewPassword, 11);
            user.SessionVersion++; user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.ResetTokens.Where(t => t.UserId == user.Id && t.UsedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.UsedAt, DateTimeOffset.UtcNow));
            await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Api.Ok(http, null, "Đã đổi mật khẩu. Vui lòng đăng nhập lại.");
        });
    }
}
