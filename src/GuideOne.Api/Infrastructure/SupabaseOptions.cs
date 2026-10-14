namespace GuideOne.Api.Infrastructure;

public sealed class SupabaseOptions
{
    public string Url { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Заавал биш. Supabase-ийн secret key (sb_secret_...) — админ шууд баталгаажсан хэрэглэгч үүсгэхэд.
    /// Git-д ХЭЗЭЭ Ч бичихгүй: локалд `dotnet user-secrets`, Render дээр `Supabase__SecretKey` env var.
    /// </summary>
    public string? SecretKey { get; set; }
}
