using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace QueDrink;

public sealed class VnPay(IConfiguration config, IWebHostEnvironment env)
{
    public string Provider { get; } = (config["PAYMENT_PROVIDER"] ?? (env.IsDevelopment() ? "MOCK" : "DISABLED")).ToUpperInvariant();
    public bool Sandbox { get; } = config.GetValue<bool?>("VNPAY_SANDBOX") ?? true;
    public string Merchant => config["VNPAY_TMN_CODE"]?.Trim() ?? "";
    private string Secret => config["VNPAY_HASH_SECRET"] ?? "";
    public string Mode => Provider == "VNPAY" ? Sandbox ? "SANDBOX" : "LIVE" : Provider == "MOCK" ? "DEMO" : "DISABLED";
    public string DatabaseProvider => Provider == "VNPAY" && Sandbox ? "VNPAY_SANDBOX" : Provider;
    public bool OnlineEnabled => Provider != "DISABLED";
    public bool IsDemo => Mode != "LIVE";
    public string Site => Gmail.Site(config);
    public string Gateway => Sandbox ? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html" : "https://pay.vnpay.vn/vpcpay.html";

    public void Validate()
    {
        Rules.Require(Provider is "MOCK" or "VNPAY" or "DISABLED", "PAYMENT_PROVIDER must be MOCK, VNPAY or DISABLED.", 500);
        Rules.Require(Provider != "MOCK" || env.IsDevelopment(), "MOCK payments are available only in Development.", 500);
        if (Provider != "VNPAY") return;
        Rules.Require(Merchant.Length is >= 4 and <= 32 && Merchant.All(char.IsAsciiLetterOrDigit) && Secret.Length >= 16,
            "Configure VNPAY_TMN_CODE and VNPAY_HASH_SECRET from your merchant account.", 500);
        var site = new Uri(Site);
        Rules.Require(Sandbox || site.Scheme == "https" && !site.IsLoopback,
            "Live VNPAY requires a public HTTPS FRONTEND_URL with a registered IPN URL.", 500);
    }

    // VNPAY 2.1.0 signs sorted, form-URL-encoded fields; spaces use '+'.
    public static string Canonical(IEnumerable<KeyValuePair<string, string>> fields) => string.Join('&', fields
        .Where(p => p.Key != "vnp_SecureHash" && p.Key != "vnp_SecureHashType" && p.Value.Length > 0)
        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => WebUtility.UrlEncode(p.Key) + "=" + WebUtility.UrlEncode(p.Value)));
    public static string Sign(string payload, string secret) => Convert.ToHexString(HMACSHA512.HashData(
        Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    public Dictionary<string, string>? Verify(IQueryCollection query)
    {
        if (Provider != "VNPAY" || query.Count > 40) return null;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in query)
        {
            if (!entry.Key.StartsWith("vnp_", StringComparison.Ordinal)) continue;
            if (entry.Value.Count != 1 || entry.Key.Length > 64 || entry.Value[0] is not string value || value.Length > 2048) return null;
            fields.Add(entry.Key, value);
        }
        if (!fields.TryGetValue("vnp_SecureHash", out var hash) || hash.Length != 128 || !hash.All(char.IsAsciiHexDigit) ||
            !Identity.Equal(Sign(Canonical(fields), Secret), hash.ToLowerInvariant()) ||
            fields.GetValueOrDefault("vnp_TmnCode") != Merchant) return null;
        return fields;
    }

    public string CheckoutUrl(Payment payment, Order order, string ip)
    {
        Rules.Require(payment.Provider == DatabaseProvider, "Giao dịch thuộc cấu hình thanh toán khác. Hãy tạo yêu cầu mới.", 409);
        var created = payment.CreatedAt.ToOffset(TimeSpan.FromHours(7));
        var fields = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0", ["vnp_Command"] = "pay", ["vnp_TmnCode"] = Merchant,
            ["vnp_Amount"] = checked(payment.Amount * 100).ToString(CultureInfo.InvariantCulture), ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = payment.ProviderReference, ["vnp_OrderInfo"] = "Thanh toan don hang " + order.OrderCode,
            ["vnp_OrderType"] = "other", ["vnp_Locale"] = "vn", ["vnp_IpAddr"] = ip,
            ["vnp_ReturnUrl"] = Site + "/api/v1/payments/vnpay/return",
            ["vnp_CreateDate"] = created.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["vnp_ExpireDate"] = created.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        };
        var data = Canonical(fields);
        return Gateway + "?" + data + "&vnp_SecureHash=" + Sign(data, Secret);
    }
}
