using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GuideOne.Api.Auth;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GuideOne.Api.Controllers;

/// <summary>Сайтын контентыг засах админ endpoint-ууд. Эрхийг энд болон Supabase RLS (is_admin()) давхар шалгана.</summary>
[Authorize(Policy = Policies.Sysadmin)]
[Route("api/admin")]
public partial class AdminContentController(SupabaseRest supabase, IOptions<SupabaseOptions> options) : ApiControllerBase
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> ImageTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    // ── Жагсаалт контент: courses, services, partners, teachers, testimonials, faqs ──

    [HttpGet("content/{resource}")]
    public async Task<ActionResult<JsonNode>> List(string resource, CancellationToken ct)
    {
        if (!ContentResources.All.TryGetValue(resource, out var r)) return NotFound();
        return Ok(await supabase.GetAsync($"rest/v1/{r.Table}?order={ContentResource.Order}", await AccessTokenAsync(), ct));
    }

    [HttpPost("content/{resource}")]
    public async Task<ActionResult<JsonNode>> Create(string resource, JsonObject body, CancellationToken ct)
    {
        if (!ContentResources.All.TryGetValue(resource, out var r)) return NotFound();
        return Ok(await supabase.InsertAsync(r.Table, body.Pick(r.Columns), await AccessTokenAsync(), ct));
    }

    [HttpPut("content/{resource}/{id:long}")]
    public async Task<ActionResult<JsonNode>> Update(string resource, long id, JsonObject body, CancellationToken ct)
    {
        if (!ContentResources.All.TryGetValue(resource, out var r)) return NotFound();
        var row = await supabase.UpdateAsync(r.Table, $"id=eq.{id}", body.Pick(r.Columns), await AccessTokenAsync(), ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpDelete("content/{resource}/{id:long}")]
    public async Task<IActionResult> Delete(string resource, long id, CancellationToken ct)
    {
        if (!ContentResources.All.TryGetValue(resource, out var r)) return NotFound();
        return await supabase.DeleteAsync(r.Table, $"id=eq.{id}", await AccessTokenAsync(), ct) ? NoContent() : NotFound();
    }

    // ── Хуудасны текстүүд (site_content: key → JSON) ──

    [HttpGet("sections")]
    public async Task<ActionResult<JsonNode>> Sections(CancellationToken ct) =>
        Ok(await supabase.GetAsync("rest/v1/site_content?select=key,value,updated_at&order=key.asc", await AccessTokenAsync(), ct));

    [HttpPut("sections/{key}")]
    public async Task<ActionResult<JsonNode>> SaveSection(string key, SectionRequest request, CancellationToken ct)
    {
        if (!SectionKey().IsMatch(key)) return Problem(statusCode: 400, detail: "Invalid section key.");
        var body = new JsonObject { ["key"] = key, ["value"] = request.Value.DeepClone() };
        var rows = await supabase.SendAsync(HttpMethod.Post, "rest/v1/site_content", body, await AccessTokenAsync(),
            "resolution=merge-duplicates,return=representation", ct);
        return Ok(rows?[0]?.DeepClone());
    }

    // ── Зураг upload (Supabase Storage "media" bucket) ──

    [HttpPost("uploads")]
    [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
    public async Task<ActionResult<UploadResponse>> Upload(IFormFile file, CancellationToken ct)
    {
        if (!ImageTypes.TryGetValue(file.ContentType, out var extension))
            return Problem(statusCode: 400, detail: "Only JPG, PNG, WEBP or GIF images are allowed.");
        if (file.Length is 0 or > MaxUploadBytes)
            return Problem(statusCode: 400, detail: "The image must be 5 MB or smaller.");

        var path = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}";
        await using var stream = file.OpenReadStream();
        await supabase.UploadAsync($"storage/v1/object/media/{path}", stream, file.ContentType, await AccessTokenAsync(), ct);
        return Ok(new UploadResponse($"{options.Value.Url.TrimEnd('/')}/storage/v1/object/public/media/{path}"));
    }

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_-]{0,63}$")]
    private static partial Regex SectionKey();
}

public record SectionRequest(JsonNode Value);

public record UploadResponse(string Url);
