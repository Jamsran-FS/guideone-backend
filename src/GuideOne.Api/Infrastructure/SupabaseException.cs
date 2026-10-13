using System.Net;
using System.Text.Json.Nodes;

namespace GuideOne.Api.Infrastructure;

/// <summary>Supabase (PostgREST / Auth / Storage)-ээс ирсэн алдаа.</summary>
public sealed class SupabaseException(int status, string message, string? code) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;

    public static SupabaseException From(HttpStatusCode status, JsonNode? body)
    {
        // Auth: { error_code, msg } · PostgREST: { code, message } · Storage: { error, message }
        var message = Text(body, "msg") ?? Text(body, "message") ?? Text(body, "error_description") ?? Text(body, "error")
            ?? $"Supabase returned {(int)status}.";
        var code = Text(body, "error_code") ?? Text(body, "code");
        return new SupabaseException((int)status, message, code);
    }

    private static string? Text(JsonNode? body, string name) =>
        body is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
