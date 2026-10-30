namespace GuideOne.Api.Infrastructure;

/// <summary>Админаас засах боломжтой сайтын контентын хүснэгт.</summary>
/// <param name="Table">Supabase дахь хүснэгтийн нэр.</param>
/// <param name="Columns">Админ бичиж болох баганууд (id, created_at зэргийг DB өөрөө бөглөнө).</param>
/// <param name="Order">Эрэмбэ (PostgREST order).</param>
/// <param name="PublicFilter">Сайтад (/api/content) харуулах нэмэлт шүүлтүүр — админд бүх мөр харагдана.</param>
public sealed record ContentResource(string Table, string[] Columns, string Order = ContentResource.DefaultOrder,
    Func<string>? PublicFilter = null)
{
    public const string DefaultOrder = "sort_order.asc,id.asc";
}

public static class ContentResources
{
    public static readonly IReadOnlyDictionary<string, ContentResource> All = new Dictionary<string, ContentResource>
    {
        ["courses"] = new("courses",
            ["slug", "glyph", "name", "level", "description", "duration", "price", "is_placeholder", "sort_order", "is_published"]),
        ["services"] = new("services",
            ["slug", "icon", "status", "title", "summary", "description", "topics", "details", "is_new", "sort_order", "is_published"]),
        ["partners"] = new("partners",
            ["name", "city", "type", "logo_url", "image_url", "url", "description", "requirements",
             "tuition", "scholarships", "intakes", "costs", "majors", "sort_order", "is_published"]),
        ["language-schools"] = new("language_schools",
            ["name", "city", "visa_type", "tuition", "headline", "highlights", "scholarships", "description",
             "logo_url", "image_url", "url", "sort_order", "is_published"]),
        ["teachers"] = new("teachers",
            ["name", "image_url", "position", "specialty", "experience", "description", "education", "sort_order", "is_published"]),
        ["testimonials"] = new("testimonials",
            ["name", "context", "quote", "image_url", "sort_order", "is_published"]),
        ["faqs"] = new("faqs",
            ["question", "answer", "sort_order", "is_published"]),
        // Гүйдэг зар: хугацаа нь дуусаагүйг л сайт руу илгээнэ (эхлэх хугацааг frontend шалгана)
        ["notices"] = new("notices",
            ["text", "url", "starts_at", "ends_at", "sort_order", "is_published"],
            Order: "starts_at.desc,id.desc",
            PublicFilter: () => $"ends_at=gt.{Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}"),
    };
}
