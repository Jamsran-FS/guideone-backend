using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

/// <summary>Нийтэд нээлттэй endpoint-ууд: сайтын контент, холбоо барих форм.</summary>
[Route("api")]
public class ContentController(SupabaseRest supabase) : ApiControllerBase
{
    /// <summary>
    /// Сайтад харагдах бүх контентыг нэг дор буцаана:
    /// { sections: { hero: {...}, ... }, courses: [...], services: [...], ... } — зөвхөн нийтлэгдсэн мөрүүд.
    /// </summary>
    [HttpGet("content")]
    public async Task<ActionResult<JsonObject>> Get(CancellationToken ct)
    {
        var sections = supabase.GetAsync("rest/v1/site_content?select=key,value", ct: ct);
        var lists = ContentResources.All.ToDictionary(
            r => r.Key,
            r => ListAsync(r.Value, ct));
        await Task.WhenAll([sections, .. lists.Values]);

        var sectionMap = new JsonObject();
        foreach (var row in (await sections)?.AsArray() ?? [])
            sectionMap[row!["key"]!.GetValue<string>()] = row["value"]?.DeepClone();

        var result = new JsonObject { ["sections"] = sectionMap };
        foreach (var (name, rows) in lists)
            if (await rows is { } list) result[name] = list;
        return result;
    }

    /// <summary>
    /// Нийтлэгдсэн мөрүүд. Migration хараахан ажиллаагүй (хүснэгт байхгүй) бол null — бусад контент хэвийн ирнэ.
    /// </summary>
    private async Task<JsonNode?> ListAsync(ContentResource r, CancellationToken ct)
    {
        var query = $"rest/v1/{r.Table}?is_published=eq.true&order={r.Order}" + (r.PublicFilter is { } filter ? $"&{filter()}" : "");
        try { return await supabase.GetAsync(query, ct: ct); }
        catch (SupabaseException e) when (e.Code is "PGRST205" or "42P01") { return null; }
    }

    /// <summary>Холбоо барих формын хүсэлтийг хадгална — админ "Хүсэлтүүд" хэсэгт харна.</summary>
    [HttpPost("contact")]
    public async Task<IActionResult> Contact(ContactRequest request, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["name"] = request.Name.Trim(),
            ["phone"] = request.Phone.Trim(),
            ["interest"] = request.Interest ?? "",
            ["message"] = request.Message?.Trim() ?? "",
            ["locale"] = request.Locale ?? "mn",
        };
        // anon хэрэглэгч хүсэлтээ уншиж чадахгүй тул return=minimal
        await supabase.SendAsync(HttpMethod.Post, "rest/v1/contact_requests", body, prefer: "return=minimal", ct: ct);
        return NoContent();
    }
}

public record ContactRequest(
    [Required, MaxLength(120)] string Name,
    [Required, MinLength(6), MaxLength(40)] string Phone,
    [MaxLength(40)] string? Interest,
    [MaxLength(4000)] string? Message,
    [MaxLength(5)] string? Locale);
