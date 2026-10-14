using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace GuideOne.Api.Infrastructure;

/// <summary>
/// Supabase-ийн REST API-г (PostgREST, Auth, Storage) шууд дууддаг клиент.
/// Хэрэглэгчийн access token дамжуулбал Row Level Security тухайн хэрэглэгчийн эрхээр ажиллана;
/// token-гүй бол publishable key-ээр (anon) хандана.
/// </summary>
public sealed class SupabaseRest(HttpClient http, IOptions<SupabaseOptions> options)
{
    /// <summary>Secret key тохируулсан эсэх (админ хэрэглэгч үүсгэх боломж).</summary>
    public bool HasSecretKey => !string.IsNullOrWhiteSpace(options.Value.SecretKey);

    /// <summary>
    /// Auth admin API-аар и-мэйл нь баталгаажсан хэрэглэгч үүсгэнэ (secret key шаардлагатай).
    /// Буцаах: шинэ хэрэглэгчийн id.
    /// </summary>
    public async Task<string> CreateConfirmedUserAsync(JsonObject body, CancellationToken ct = default)
    {
        var secret = options.Value.SecretKey ?? throw new InvalidOperationException("Supabase:SecretKey is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/admin/users")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        var user = await SendAsync(request, secret, null, ct, apiKey: secret);
        return user!["id"]!.GetValue<string>();
    }

    public Task<JsonNode?> GetAsync(string path, string? token = null, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, path, null, token, null, ct);

    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body = null, string? token = null,
        string? prefer = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await SendAsync(request, token, prefer, ct);
    }

    /// <summary>Мөр нэмээд нэмэгдсэн мөрийг буцаана.</summary>
    public async Task<JsonNode?> InsertAsync(string table, JsonNode body, string? token, CancellationToken ct = default) =>
        First(await SendAsync(HttpMethod.Post, $"rest/v1/{table}", body, token, "return=representation", ct));

    /// <summary>Шүүлтүүрт таарсан мөрийг засна. Мөр олдоогүй (эсвэл эрхгүй) бол null.</summary>
    public async Task<JsonNode?> UpdateAsync(string table, string filter, JsonNode body, string token, CancellationToken ct = default) =>
        First(await SendAsync(HttpMethod.Patch, $"rest/v1/{table}?{filter}", body, token, "return=representation", ct));

    /// <summary>Шүүлтүүрт таарсан мөрийг устгана. Юу ч устаагүй бол false.</summary>
    public async Task<bool> DeleteAsync(string table, string filter, string token, CancellationToken ct = default) =>
        First(await SendAsync(HttpMethod.Delete, $"rest/v1/{table}?{filter}", null, token, "return=representation", ct)) is not null;

    public async Task<JsonNode?> UploadAsync(string path, Stream content, string contentType, string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StreamContent(content) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await SendAsync(request, token, null, ct);
    }

    /// <summary>Хаалттай bucket-ийн файлуудад түр (expiresIn секунд) татах холбоос үүсгэнэ: path → URL.</summary>
    public async Task<Dictionary<string, string>> SignAsync(string bucket, IEnumerable<string> paths, string token, int expiresIn,
        CancellationToken ct = default)
    {
        var list = paths.Distinct().ToArray();
        var urls = new Dictionary<string, string>();
        if (list.Length == 0) return urls;

        var body = new JsonObject { ["expiresIn"] = expiresIn, ["paths"] = new JsonArray([.. list.Select(p => (JsonNode?)p)]) };
        var result = await SendAsync(HttpMethod.Post, $"storage/v1/object/sign/{bucket}", body, token, null, ct);
        foreach (var item in result as JsonArray ?? [])
        {
            if (item?["path"]?.GetValue<string>() is { } path && item["signedURL"]?.GetValue<string>() is { } signed)
                urls[path] = $"{http.BaseAddress}storage/v1{signed}";
        }
        return urls;
    }

    public Task<JsonNode?> DeleteFileAsync(string bucket, string path, string token, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"storage/v1/object/{bucket}", new JsonObject { ["prefixes"] = new JsonArray(path) }, token, null, ct);

    private async Task<JsonNode?> SendAsync(HttpRequestMessage request, string? token, string? prefer, CancellationToken ct,
        string? apiKey = null)
    {
        request.Headers.Add("apikey", apiKey ?? options.Value.Key);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (prefer is not null) request.Headers.Add("Prefer", prefer);

        using var response = await http.SendAsync(request, ct);
        var json = Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode) throw SupabaseException.From(response.StatusCode, json);
        return json;
    }

    private static JsonNode? First(JsonNode? rows) => rows is JsonArray { Count: > 0 } array ? array[0]?.DeepClone() : null;

    private static JsonNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }
}

public static class JsonExtensions
{
    /// <summary>Зөвхөн зөвшөөрөгдсөн талбаруудыг авна — id, role, status зэргийг хэрэглэгч өөрөө бичихээс хамгаална.</summary>
    public static JsonObject Pick(this JsonObject source, IEnumerable<string> keys)
    {
        var result = new JsonObject();
        foreach (var key in keys)
            if (source.TryGetPropertyValue(key, out var value)) result[key] = value?.DeepClone();
        return result;
    }

    public static int CountRows(this JsonNode? rows) => rows is JsonArray array ? array.Count : 0;

    public static string? Text(this JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

public static class MaterialFiles
{
    public const string Bucket = "materials";
    public const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>Материалын мөр бүрт file_path-аас 1 цагийн хүчинтэй file_url нэмнэ.</summary>
    public static async Task AddFileUrlsAsync(this SupabaseRest supabase, JsonNode? rows, string token, CancellationToken ct)
    {
        if (rows is not JsonArray array) return;
        var paths = array.Select(r => r.Text("file_path")).OfType<string>();
        var urls = await supabase.SignAsync(Bucket, paths, token, 3600, ct);
        foreach (var row in array.OfType<JsonObject>())
            if (row.Text("file_path") is { } path && urls.TryGetValue(path, out var url)) row["file_url"] = url;
    }
}
