using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GuideOne.Api.Controllers;

/// <summary>
/// Суралцагч сургалтын төлбөрөө QPay-ээр төлнө. Нэхэмжлэх үүсгэх → QR/банкны апп →
/// QPay callback (эсвэл суралцагчийн шалгах хүсэлт) ирэхэд payment/check-ээр QPay-ээс баталгаажуулж
/// enrollments.amount_paid-ийг нэмнэ. Callback-д ирсэн мэдээлэлд шууд итгэхгүй.
/// </summary>
public class PaymentsController(SupabaseRest supabase, QPayClient qpay, IOptions<QPayOptions> options,
    ILogger<PaymentsController> logger) : ApiControllerBase
{
    /// <summary>Хүлээгдэж буй ижил дүнтэй нэхэмжлэхийг энэ хугацаанд дахин ашиглана (QR-ыг дахин үүсгэхгүй).</summary>
    private static readonly TimeSpan ReuseInvoiceFor = TimeSpan.FromHours(12);

    public sealed record PaymentStatusResponse(long Id, string Status);

    [Authorize]
    [HttpPost("api/me/enrollments/{id:long}/qpay")]
    public async Task<ActionResult<JsonNode>> CreateInvoice(long id, CancellationToken ct)
    {
        if (!qpay.IsConfigured || !supabase.HasSecretKey)
            return Problem("QPay төлбөр одоогоор идэвхжээгүй байна.", statusCode: StatusCodes.Status503ServiceUnavailable);

        // Хэрэглэгчийн token-оор уншина — RLS нь зөвхөн өөрийн элсэлтийг буцаана
        var rows = await supabase.GetAsync(
            $"rest/v1/enrollments?id=eq.{id}&student_id=eq.{UserId}&select=id,status,amount_due,amount_paid,group:groups(name)",
            await AccessTokenAsync(), ct);
        if (rows is not JsonArray { Count: > 0 } found || found[0] is not JsonObject enrollment) return NotFound();
        if (enrollment.Text("status") == "dropped")
            return Problem("Энэ ангиас гарсан тул төлбөр төлөх боломжгүй.", statusCode: StatusCodes.Status400BadRequest);

        var amount = Money(enrollment["amount_due"]) - Money(enrollment["amount_paid"]);
        if (amount <= 0) return Problem("Төлөх үлдэгдэл алга.", statusCode: StatusCodes.Status400BadRequest);
        var amountText = amount.ToString(CultureInfo.InvariantCulture);

        var since = Uri.EscapeDataString(DateTimeOffset.UtcNow.Subtract(ReuseInvoiceFor).ToString("O"));
        var existing = await supabase.ServiceAsync(HttpMethod.Get,
            $"rest/v1/payments?enrollment_id=eq.{id}&status=eq.pending&amount=eq.{amountText}&invoice_id=not.is.null" +
            $"&created_at=gte.{since}&select=id,amount,status,invoice&order=created_at.desc&limit=1", ct: ct);
        if (existing is JsonArray { Count: > 0 } reuse) return Ok(View(reuse[0]!));

        var callbackToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var payment = await supabase.ServiceAsync(HttpMethod.Post, "rest/v1/payments", new JsonObject
        {
            ["enrollment_id"] = id,
            ["student_id"] = UserId,
            ["amount"] = amount,
            ["sender_invoice_no"] = $"G1-{id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ["callback_token"] = callbackToken,
        }, "return=representation", ct);
        var row = payment is JsonArray { Count: > 0 } inserted ? inserted[0]! : throw new InvalidOperationException("Payment insert failed.");
        var paymentId = row["id"]!.GetValue<long>();

        var o = options.Value;
        JsonNode? invoice;
        try
        {
            invoice = await qpay.CreateInvoiceAsync(new JsonObject
            {
                ["invoice_code"] = o.InvoiceCode,
                ["sender_invoice_no"] = row.Text("sender_invoice_no"),
                ["invoice_receiver_code"] = UserId,
                ["invoice_description"] = Truncate($"GuideOne сургалтын төлбөр: {enrollment["group"].Text("name") ?? id.ToString()}", 255),
                ["amount"] = amount,
                ["callback_url"] = $"{o.PublicApiUrl.TrimEnd('/')}/api/payments/qpay/callback/{paymentId}?token={callbackToken}",
            }, ct);
        }
        catch (HttpRequestException error)
        {
            logger.LogError(error, "QPay invoice creation failed for payment {PaymentId}", paymentId);
            await SetStatusAsync(paymentId, "cancelled", ct);
            return Problem("QPay нэхэмжлэх үүсгэж чадсангүй. Дахин оролдоно уу.", statusCode: StatusCodes.Status502BadGateway);
        }

        var stored = new JsonObject
        {
            ["qr_text"] = invoice?["qr_text"]?.DeepClone(),
            ["qr_image"] = invoice?["qr_image"]?.DeepClone(),
            ["short_url"] = invoice?["qPay_shortUrl"]?.DeepClone(),
            ["urls"] = invoice?["urls"]?.DeepClone() ?? new JsonArray(),
        };
        var updated = await supabase.ServiceAsync(HttpMethod.Patch, $"rest/v1/payments?id=eq.{paymentId}", new JsonObject
        {
            ["invoice_id"] = invoice?["invoice_id"]?.DeepClone(),
            ["invoice"] = stored,
        }, "return=representation", ct);
        return Ok(View(updated is JsonArray { Count: > 0 } u ? u[0]! : row));
    }

    /// <summary>Суралцагч QR цонхноос үе үе шалгана. Pending бол QPay-ээс шалгаж баталгаажуулна.</summary>
    [Authorize]
    [HttpGet("api/me/payments/{id:long}")]
    public async Task<ActionResult<PaymentStatusResponse>> Status(long id, CancellationToken ct)
    {
        var rows = await supabase.GetAsync(
            $"rest/v1/payments?id=eq.{id}&student_id=eq.{UserId}&select=id,status,amount,invoice_id",
            await AccessTokenAsync(), ct);
        if (rows is not JsonArray { Count: > 0 } found) return NotFound();
        var payment = found[0]!;
        var status = payment.Text("status")!;
        if (status == "pending" && qpay.IsConfigured && supabase.HasSecretKey) status = await VerifyAsync(payment, ct);
        return new PaymentStatusResponse(id, status);
    }

    /// <summary>
    /// QPay төлөлт амжилттай болоход дууддаг callback. Зөвхөн "энэ нэхэмжлэхийг шалга" гэсэн дохио —
    /// төлөгдсөн эсэхийг payment/check-ээр QPay-ээс өөрөөс нь баталгаажуулна.
    /// </summary>
    [AllowAnonymous]
    [AcceptVerbs("GET", "POST", Route = "api/payments/qpay/callback/{id:long}")]
    public async Task<IActionResult> Callback(long id, [FromQuery] string? token, CancellationToken ct)
    {
        if (!qpay.IsConfigured || !supabase.HasSecretKey || string.IsNullOrEmpty(token)) return NotFound();

        var rows = await supabase.ServiceAsync(HttpMethod.Get,
            $"rest/v1/payments?id=eq.{id}&select=id,status,amount,invoice_id,callback_token", ct: ct);
        if (rows is not JsonArray { Count: > 0 } found) return NotFound();
        var payment = found[0]!;
        var expected = Encoding.UTF8.GetBytes(payment.Text("callback_token") ?? "");
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(token))) return NotFound();

        if (payment.Text("status") == "pending") await VerifyAsync(payment, ct);
        return Content("SUCCESS", "text/plain");
    }

    /// <summary>QPay-ээс нэхэмжлэхийн төлөлтийг шалгаад бүрэн төлөгдсөн бол бүртгэнэ. Буцаах: шинэ төлөв.</summary>
    private async Task<string> VerifyAsync(JsonNode payment, CancellationToken ct)
    {
        if (payment.Text("invoice_id") is not { } invoiceId) return "pending";

        var check = await qpay.CheckInvoiceAsync(invoiceId, ct);
        if (check.PaidAmount < Money(payment["amount"])) return "pending";

        await supabase.ServiceAsync(HttpMethod.Post, "rest/v1/rpc/apply_payment", new JsonObject
        {
            ["pid"] = payment["id"]!.GetValue<long>(),
            ["provider_pid"] = check.PaymentId,
        }, ct: ct);
        logger.LogInformation("QPay payment {PaymentId} confirmed ({Amount})", payment["id"], check.PaidAmount);
        return "paid";
    }

    private Task SetStatusAsync(long paymentId, string status, CancellationToken ct) =>
        supabase.ServiceAsync(HttpMethod.Patch, $"rest/v1/payments?id=eq.{paymentId}", new JsonObject { ["status"] = status }, ct: ct);

    /// <summary>Хөтөч рүү буцаах хэлбэр — callback_token зэрэг дотоод талбаргүй.</summary>
    private static JsonObject View(JsonNode row)
    {
        var invoice = row["invoice"] as JsonObject ?? [];
        return new JsonObject
        {
            ["id"] = row["id"]?.DeepClone(),
            ["amount"] = row["amount"]?.DeepClone(),
            ["status"] = row["status"]?.DeepClone(),
            ["qr_image"] = invoice["qr_image"]?.DeepClone(),
            ["qr_text"] = invoice["qr_text"]?.DeepClone(),
            ["short_url"] = invoice["short_url"]?.DeepClone(),
            ["urls"] = new JsonArray([.. (invoice["urls"] as JsonArray ?? []).OfType<JsonObject>().Select(u => (JsonNode?)new JsonObject
            {
                ["name"] = u["name"]?.DeepClone(),
                ["description"] = u["description"]?.DeepClone(),
                ["logo"] = u["logo"]?.DeepClone(),
                ["link"] = u["link"]?.DeepClone(),
            })]),
        };
    }

    private static decimal Money(JsonNode? node) =>
        node is JsonValue value && (value.TryGetValue<decimal>(out var d) ||
            decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) ? d : 0;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
