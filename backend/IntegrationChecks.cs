using Microsoft.Extensions.Primitives;

namespace QueDrink;

public static class IntegrationChecks
{
    public static void Run()
    {
        static void Check(bool valid, string name) { if (!valid) throw new Exception("Integration self-check failed: " + name); }
        Check(VnPay.Sign("VNPAY signing fixture", "fixture-only-key") ==
            "eee0efb6ffd61ddc806f6fcdd4f5281ec28fb375b62028f8fc23af107bf0d53b0ca29859610eb0be9ee8d7e82e2da4039fdf44414c60f72e23917d218bc8dc45", "HMAC-SHA512 Python reference");
        Check(VnPay.Canonical(new Dictionary<string, string> { ["vnp_OrderInfo"] = "Thanh toan QD-123", ["vnp_Amount"] = "123400",
            ["vnp_ReturnUrl"] = "https://example.test/return?a=1&b=2", ["vnp_SecureHash"] = "excluded" }) ==
            "vnp_Amount=123400&vnp_OrderInfo=Thanh+toan+QD-123&vnp_ReturnUrl=https%3A%2F%2Fexample.test%2Freturn%3Fa%3D1%26b%3D2", "sort, form encoding and hash exclusion");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PAYMENT_PROVIDER"] = "VNPAY", ["VNPAY_TMN_CODE"] = "FIXTURE1", ["VNPAY_HASH_SECRET"] = "fixture-only-secret-2026" }).Build();
        var pay = new VnPay(config, builder.Environment); pay.Validate();
        var fields = new Dictionary<string, StringValues> { ["vnp_TmnCode"] = "FIXTURE1", ["vnp_Amount"] = "123400", ["vnp_TxnRef"] = "TEST123" };
        fields["vnp_SecureHash"] = VnPay.Sign(VnPay.Canonical(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value.ToString()))), "fixture-only-secret-2026");
        Check(pay.Verify(new QueryCollection(fields)) is not null, "valid signature");
        fields["vnp_Amount"] = new StringValues(["123400", "1"]);
        Check(pay.Verify(new QueryCollection(fields)) is null, "duplicate fields rejected");
        fields["vnp_Amount"] = "1";
        Check(pay.Verify(new QueryCollection(fields)) is null, "modified amount rejected");
        using var message = Gmail.ResetMessage("sender@example.test", "customer@example.test", "<Que & Drinks>", "https://example.test", "fixture-reset-token");
        Check(message.To.Count == 1 && message.Body.Contains("/account#reset=fixture-reset-token"), "reset link uses fragment");
        using var reader = new StreamReader(message.AlternateViews[1].ContentStream);
        var html = reader.ReadToEnd();
        Check(html.Contains("&lt;Que &amp; Drinks&gt;") && !html.Contains("<Que & Drinks>"), "email HTML escaping");
        Console.WriteLine("Integration checks PASS: VNPAY signing/encoding/tamper/duplicates and Gmail reset message.");
    }
}
