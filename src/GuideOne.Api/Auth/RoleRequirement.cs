using System.Security.Claims;
using System.Text.Json.Nodes;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace GuideOne.Api.Auth;

public static class Roles
{
    public const string Sysadmin = "sysadmin";
    public const string Teacher = "teacher";
    public const string Student = "student";
}

public static class Policies
{
    /// <summary>Сайтын контент, анги, суралцагч, төлбөр — бүгдийг удирдана.</summary>
    public const string Sysadmin = "Sysadmin";

    /// <summary>Өөрийн ангийн материал, зар, шалгалтын дүн.</summary>
    public const string Teacher = "Teacher";
}

/// <summary>Хэрэглэгчийн profiles.role нь заасан эрхүүдийн нэг байхыг шаардана.</summary>
public sealed class RoleRequirement(params string[] roles) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> Roles { get; } = roles;
}

public sealed class RoleHandler(ProfileRoles roles) : AuthorizationHandler<RoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
    {
        if (context.Resource is not HttpContext http) return;
        var userId = context.User.FindFirstValue("sub");
        var token = await http.GetTokenAsync("access_token");
        if (userId is null || token is null) return;

        var role = await roles.GetRoleAsync(userId, token, http.RequestAborted);
        if (role is not null && requirement.Roles.Contains(role)) context.Succeed(requirement);
    }
}

/// <summary>Role-ийг profiles хүснэгтээс уншиж 1 минут cache-лэнэ.</summary>
public sealed class ProfileRoles(SupabaseRest supabase, IMemoryCache cache)
{
    public async Task<string?> GetRoleAsync(string userId, string token, CancellationToken ct) =>
        await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            var rows = await supabase.GetAsync($"rest/v1/profiles?id=eq.{Uri.EscapeDataString(userId)}&select=role", token, ct);
            return rows is JsonArray { Count: > 0 } array ? array[0]?["role"]?.GetValue<string>() : null;
        });

    public void Forget(string userId) => cache.Remove(Key(userId));

    private static string Key(string userId) => $"role:{userId}";
}
