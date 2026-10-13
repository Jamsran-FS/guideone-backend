namespace GuideOne.Api.Infrastructure;

/// <summary>Админаас засах боломжтой сайтын контентын хүснэгт.</summary>
/// <param name="Table">Supabase дахь хүснэгтийн нэр.</param>
/// <param name="Columns">Админ бичиж болох баганууд (id, created_at зэргийг DB өөрөө бөглөнө).</param>
public sealed record ContentResource(string Table, string[] Columns)
{
    public const string Order = "sort_order.asc,id.asc";
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
            ["name", "city", "type", "logo_url", "url", "sort_order", "is_published"]),
        ["teachers"] = new("teachers",
            ["name", "image_url", "position", "specialty", "experience", "description", "sort_order", "is_published"]),
        ["testimonials"] = new("testimonials",
            ["name", "context", "quote", "image_url", "sort_order", "is_published"]),
        ["faqs"] = new("faqs",
            ["question", "answer", "sort_order", "is_published"]),
    };
}
