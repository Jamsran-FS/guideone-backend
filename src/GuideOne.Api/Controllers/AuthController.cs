using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

/// <summary>Supabase Auth дээр суурилсан бүртгэл, нэвтрэлт.</summary>
[Route("api/auth")]
public class AuthController(SupabaseRest supabase) : ApiControllerBase
{
    /// <summary>Хэрэглэгч өөрөө засаж болох profile талбарууд (role-ийг зөвхөн админ солино).</summary>
    private static readonly string[] EditableProfileFields =
        ["full_name", "phone", "birth_date", "parent_name", "parent_phone", "korean_level"];

    /// <summary>Шинэ суралцагч бүртгэнэ. Profile нь DB trigger-ээр "student" эрхтэй үүснэ.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["email"] = request.Email.Trim(),
            ["password"] = request.Password,
            ["data"] = new JsonObject { ["full_name"] = request.FullName.Trim(), ["phone"] = request.Phone.Trim() },
        };
        var result = await supabase.SendAsync(HttpMethod.Post, $"auth/v1/signup{RedirectQuery(request.RedirectTo)}", body, ct: ct);

        // Supabase-д и-мэйл баталгаажуулалт асаалттай бол session ирэхгүй — хэрэглэгч и-мэйлээ шалгана.
        if (result?["access_token"] is null) return Ok(new RegisterResponse(true, null));
        return Ok(new RegisterResponse(false, await ToSessionAsync(result, ct)));
    }

    /// <summary>Баталгаажуулах и-мэйлийг дахин илгээнэ (холбоосын хугацаа дууссан, и-мэйл ирээгүй үед).</summary>
    [HttpPost("resend")]
    public async Task<IActionResult> Resend(ResendRequest request, CancellationToken ct)
    {
        var body = new JsonObject { ["type"] = "signup", ["email"] = request.Email.Trim() };
        await supabase.SendAsync(HttpMethod.Post, $"auth/v1/resend{RedirectQuery(request.RedirectTo)}", body, ct: ct);
        return NoContent();
    }

    [HttpPost("login")]
    public async Task<ActionResult<SessionResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var body = new JsonObject { ["email"] = request.Email.Trim(), ["password"] = request.Password };
        var result = await supabase.SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=password", body, ct: ct);
        return Ok(await ToSessionAsync(result!, ct));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<SessionResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var body = new JsonObject { ["refresh_token"] = request.RefreshToken };
        var result = await supabase.SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token", body, ct: ct);
        return Ok(await ToSessionAsync(result!, ct));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await supabase.SendAsync(HttpMethod.Post, "auth/v1/logout", token: await AccessTokenAsync(), ct: ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<JsonNode>> Me(CancellationToken ct)
    {
        var profile = await GetProfileAsync(UserId, await AccessTokenAsync(), ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<ActionResult<JsonNode>> UpdateMe(JsonObject body, CancellationToken ct)
    {
        var profile = await supabase.UpdateAsync("profiles", $"id=eq.{UserId}", body.Pick(EditableProfileFields), await AccessTokenAsync(), ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>
    /// И-мэйлийн холбоос дээр дарсны дараа очих хуудас. Supabase → Authentication → URL Configuration →
    /// Redirect URLs жагсаалтад байх ёстой, үгүй бол Site URL руу очно.
    /// </summary>
    private static string RedirectQuery(string? redirectTo) =>
        string.IsNullOrWhiteSpace(redirectTo) ? "" : $"?redirect_to={Uri.EscapeDataString(redirectTo)}";

    private async Task<SessionResponse> ToSessionAsync(JsonNode auth, CancellationToken ct)
    {
        var accessToken = auth["access_token"]!.GetValue<string>();
        var userId = auth["user"]!["id"]!.GetValue<string>();
        return new SessionResponse(
            accessToken,
            auth["refresh_token"]!.GetValue<string>(),
            auth["expires_in"]!.GetValue<int>(),
            await GetProfileAsync(userId, accessToken, ct));
    }

    private async Task<JsonNode?> GetProfileAsync(string userId, string token, CancellationToken ct)
    {
        var rows = await supabase.GetAsync($"rest/v1/profiles?id=eq.{userId}&select=*", token, ct);
        return rows is JsonArray { Count: > 0 } array ? array[0]!.DeepClone() : null;
    }
}

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MinLength(8), MaxLength(72)] string Password,
    [Required, MaxLength(120)] string FullName,
    [Required, MaxLength(40)] string Phone,
    [MaxLength(500)] string? RedirectTo);

public record ResendRequest([Required, EmailAddress] string Email, [MaxLength(500)] string? RedirectTo);

public record LoginRequest([Required] string Email, [Required] string Password);

public record RefreshRequest([Required] string RefreshToken);

public record SessionResponse(string AccessToken, string RefreshToken, int ExpiresIn, JsonNode? Profile);

public record RegisterResponse(bool RequiresConfirmation, SessionResponse? Session);
