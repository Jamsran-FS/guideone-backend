using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace GuideOne.Api.Infrastructure;

public sealed class QPayOptions
{
    public string BaseUrl { get; set; } = "https://merchant.qpay.mn/v2/";
    public string InvoiceCode { get; set; } = string.Empty;

    /// <summary>
    /// QPay-ийн клиент нэр, нууц үг. Git-д ХЭЗЭЭ Ч бичихгүй: локалд `dotnet user-secrets`,
    /// Render дээр `QPay__ClientId`, `QPay__ClientSecret` env var.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Энэ API-ийн интернэтээс хандах үндсэн хаяг — QPay төлөлтийн callback-ийг энд илгээнэ.</summary>
    public string PublicApiUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(InvoiceCode) && !string.IsNullOrWhiteSpace(PublicApiUrl);
}

/// <summary>Нэхэмжлэхийн төлөгдсөн байдал: QPay-д бүртгэгдсэн PAID гүйлгээнүүдийн нийлбэр.</summary>
public sealed record QPayCheck(decimal PaidAmount, string? PaymentId);

/// <summary>
/// QPay Merchant V2 клиент. Access token-ийг expires_in (unix timestamp) хүртэл нэг л удаа авч
/// дахин ашиглана — хүсэлт бүрт шинэ token үүсгэхгүй.
/// </summary>
public sealed class QPayClient(HttpClient http, IOptions<QPayOptions> options)
{
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _token;
    private static DateTimeOffset _tokenExpires;

    public bool IsConfigured => options.Value.IsConfigured;

    public Task<JsonNode?> CreateInvoiceAsync(JsonObject body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "invoice", body, ct);

    public async Task<QPayCheck> CheckInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["object_type"] = "INVOICE",
            ["object_id"] = invoiceId,
            ["offset"] = new JsonObject { ["page_number"] = 1, ["page_limit"] = 100 },
        };
        var result = await SendAsync(HttpMethod.Post, "payment/check", body, ct);
        var paid = (result?["rows"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(r => string.Equals(r.Text("payment_status"), "PAID", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return new QPayCheck(paid.Sum(r => Decimal(r["payment_amount"])), paid.Select(r => Text(r["payment_id"])).FirstOrDefault());
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct, bool retry = true)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && retry)
        {
            // Token хугацаанаасаа өмнө хүчингүй болсон — нэг удаа шинээр аваад дахин оролдоно
            _token = null;
            return await SendAsync(method, path, body, ct, retry: false);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"QPay {path}: {(int)response.StatusCode} {text}", null, response.StatusCode);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires) return _token;

        await TokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires) return _token;

            var o = options.Value;
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ClientId}:{o.ClientSecret}")));
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"QPay auth/token: {(int)response.StatusCode} {text}", null, response.StatusCode);

            var json = JsonNode.Parse(text)!;
            _token = json["access_token"]!.GetValue<string>();
            // expires_in нь дуусах хугацааны unix timestamp (секунд). 1 минутын нөөцтэйгөөр шинэчилнэ.
            var expires = (long)Decimal(json["expires_in"]);
            var at = expires > 1_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(expires) : DateTimeOffset.UtcNow.AddSeconds(expires);
            _tokenExpires = at.AddMinutes(-1);
            return _token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static string? Text(JsonNode? node) => node?.GetValueKind() switch
    {
        System.Text.Json.JsonValueKind.String => node.GetValue<string>(),
        System.Text.Json.JsonValueKind.Number => node.ToJsonString(),
        _ => null,
    };

    private static decimal Decimal(JsonNode? node) =>
        decimal.TryParse(Text(node), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
