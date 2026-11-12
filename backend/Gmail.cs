using System.Net;
using System.Net.Mail;
using System.Text;

namespace QueDrink;

public sealed class Gmail(IConfiguration config, IWebHostEnvironment env, ILogger<Gmail> logger)
{
    public bool Enabled { get; } = config.GetValue<bool>("GMAIL_ENABLED");
    private string Address => config["GMAIL_ADDRESS"]?.Trim() ?? "";
    private string Password => string.Concat((config["GMAIL_APP_PASSWORD"] ?? "").Where(c => !char.IsWhiteSpace(c)));

    public void Validate()
    {
        if (!Enabled) return;
        Rules.Require(MailAddress.TryCreate(Address, out var address) && address.Address == Address && Password.Length == 16,
            "Configure GMAIL_ADDRESS and a 16-character GMAIL_APP_PASSWORD; do not use your Google account password.", 500);
        Rules.Require(env.IsDevelopment() || new Uri(Site(config)).Scheme == "https", "Gmail recovery links require HTTPS outside Development.", 500);
    }

    public static string Site(IConfiguration config)
    {
        var site = config["FRONTEND_URL"] ?? config["DEMO_FRONTEND_URL"] ?? "http://localhost:3000";
        Rules.Require(Uri.TryCreate(site, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/",
            "FRONTEND_URL must be the public website origin, without a path, query or credentials.", 500);
        return site.TrimEnd('/');
    }

    public static MailMessage ResetMessage(string from, string to, string store, string site, string token)
    {
        var link = site + "/account#reset=" + Uri.EscapeDataString(token);
        var message = new MailMessage(new MailAddress(from, store), new MailAddress(to))
        {
            Subject = "Đặt lại mật khẩu — " + store,
            SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8,
            Body = $"Bạn vừa yêu cầu đặt lại mật khẩu tại {store}.\n\nMở liên kết sau trong vòng 15 phút:\n{link}\n\nNếu không phải bạn yêu cầu, hãy bỏ qua email này. Không chia sẻ liên kết với người khác."
        };
        var html = $"<html lang=\"vi\"><body style=\"font-family:Arial,sans-serif;background:#f5f6f2;padding:32px;color:#173b31\"><div style=\"max-width:560px;margin:auto;background:#fff;padding:32px;border-radius:16px\"><h1>{WebUtility.HtmlEncode(store)}</h1><h2>Đặt lại mật khẩu</h2><p>Bạn vừa yêu cầu khôi phục tài khoản. Liên kết có hiệu lực trong 15 phút và chỉ dùng được một lần.</p><p style=\"padding:20px 0\"><a style=\"background:#17654e;color:white;padding:14px 20px;border-radius:8px;text-decoration:none\" href=\"{WebUtility.HtmlEncode(link)}\">Đặt mật khẩu mới</a></p><p>Nếu không phải bạn yêu cầu, hãy bỏ qua email này. Không chia sẻ liên kết với người khác.</p></div></body></html>";
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Body, Encoding.UTF8, "text/plain"));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
        return message;
    }

    public async Task<bool> SendReset(string recipient, string token)
    {
        if (!Enabled) return false;
        using var message = ResetMessage(Address, recipient, config["STORE_NAME"] ?? "Quê Drinks", Site(config), token);
        return await Send(message);
    }

    public async Task<bool> SendTest()
    {
        Rules.Require(Enabled, "Gmail chưa được bật.", 409);
        using var message = new MailMessage(Address, Address, "QueDrink — kiểm tra kết nối Gmail",
            "QueDrink đã gửi email này qua Gmail SMTP với STARTTLS. Không có mật khẩu hay khóa thanh toán trong email.")
            { SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8 };
        return await Send(message);
    }

    private async Task<bool> Send(MailMessage message)
    {
        // ponytail: synchronous SMTP, bounded to 15 seconds; add a durable outbox when sending order notifications or handling larger traffic.
        using var smtp = new SmtpClient("smtp.gmail.com", 587)
        { EnableSsl = true, UseDefaultCredentials = false, Credentials = new NetworkCredential(Address, Password) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await smtp.SendMailAsync(message, timeout.Token); return true; }
        catch (Exception error) when (error is SmtpException or OperationCanceledException)
        {
            logger.LogWarning("Gmail send failed ({ErrorType}). Check credentials, Gmail limits and network; recipient and reset token are not logged.", error.GetType().Name);
            return false;
        }
    }
}
