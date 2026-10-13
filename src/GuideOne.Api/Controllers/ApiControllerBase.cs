using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Нэвтэрсэн хэрэглэгчийн Supabase user id (JWT "sub").</summary>
    protected string UserId => User.FindFirstValue("sub") ?? throw new InvalidOperationException("User is not authenticated.");

    /// <summary>Хүсэлтийн Bearer token — Supabase руу дамжуулж RLS-ийг хэрэглэгчийн эрхээр ажиллуулна.</summary>
    protected async Task<string> AccessTokenAsync() =>
        await HttpContext.GetTokenAsync("access_token") ?? throw new InvalidOperationException("Access token is missing.");
}
