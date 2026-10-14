using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using GuideOne.Api.Auth;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

/// <summary>Sysadmin: хянах самбар, холбоо барих хүсэлтүүд, суралцагчдын өргөдөл, хэрэглэгчид.</summary>
[Authorize(Policy = Policies.Sysadmin)]
[Route("api/admin")]
public class AdminController(SupabaseRest supabase, ProfileRoles roles) : ApiControllerBase
{
    private const string ApplicationSelect =
        "*,student:profiles(id,full_name,email,phone,parent_name,parent_phone),course:courses(id,name),partner:partners(id,name)";

    /// <summary>Хянах самбарын үзүүлэлтүүд: төлбөр, суралцагч, өргөдөл, хүсэлт, анги.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<StatsResponse>> Stats(CancellationToken ct)
    {
        var token = await AccessTokenAsync();
        var enrollmentsTask = supabase.GetAsync("rest/v1/enrollments?select=student_id,status,payment_status,amount_due,amount_paid", token, ct);
        var applicationsTask = supabase.GetAsync("rest/v1/applications?select=program,status", token, ct);
        var requestsTask = supabase.GetAsync("rest/v1/contact_requests?select=status", token, ct);
        var profilesTask = supabase.GetAsync("rest/v1/profiles?select=id,role", token, ct);
        var groupsTask = supabase.GetAsync("rest/v1/groups?select=is_active", token, ct);
        await Task.WhenAll(enrollmentsTask, applicationsTask, requestsTask, profilesTask, groupsTask);

        static List<JsonNode> Rows(JsonNode? n) => (n as JsonArray ?? []).OfType<JsonNode>().ToList();
        static decimal Money(JsonNode row, string name) => row[name]?.GetValue<decimal>() ?? 0;

        var enrollments = Rows(await enrollmentsTask);
        var applications = Rows(await applicationsTask);
        var requests = Rows(await requestsTask);
        var profiles = Rows(await profilesTask);
        var groups = Rows(await groupsTask);

        var active = enrollments.Where(e => e.Text("status") == "active").ToList();
        var due = active.Sum(e => Money(e, "amount_due"));
        var paid = enrollments.Sum(e => Money(e, "amount_paid"));
        var enrolledIds = enrollments.Select(e => e.Text("student_id")).ToHashSet();
        var studentIds = profiles.Where(p => p.Text("role") == "student").Select(p => p.Text("id")).ToList();
        var open = applications.Where(a => a.Text("status") is "new" or "in_review").ToList();

        return new StatsResponse(
            new PaymentStats(paid, due, Math.Max(0, due - active.Sum(e => Money(e, "amount_paid"))),
                active.Count(e => e.Text("payment_status") != "paid")),
            new StudentStats(studentIds.Count, active.Select(e => e.Text("student_id")).Distinct().Count(),
                studentIds.Count(id => !enrolledIds.Contains(id))),
            new ApplicationStats(open.Count, open.Count(a => a.Text("program") == "language_course"),
                open.Count(a => a.Text("program") == "school_placement"), applications.Count(a => a.Text("status") == "accepted")),
            new RequestStats(requests.Count(r => r.Text("status") == "new"), requests.Count(r => r.Text("status") == "contacted"), requests.Count),
            new GroupStats(groups.Count(g => g["is_active"]?.GetValue<bool>() == true), groups.Count,
                profiles.Count(p => p.Text("role") == "teacher")));
    }

    // ── Холбоо барих формоор ирсэн хүсэлтүүд ──

    [HttpGet("contact-requests")]
    public async Task<ActionResult<JsonNode>> ContactRequests(CancellationToken ct) =>
        Ok(await supabase.GetAsync("rest/v1/contact_requests?order=created_at.desc", await AccessTokenAsync(), ct));

    [HttpPatch("contact-requests/{id:long}")]
    public async Task<ActionResult<JsonNode>> UpdateContactRequest(long id, JsonObject body, CancellationToken ct)
    {
        var row = await supabase.UpdateAsync("contact_requests", $"id=eq.{id}", body.Pick(["status", "admin_note"]), await AccessTokenAsync(), ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpDelete("contact-requests/{id:long}")]
    public async Task<IActionResult> DeleteContactRequest(long id, CancellationToken ct) =>
        await supabase.DeleteAsync("contact_requests", $"id=eq.{id}", await AccessTokenAsync(), ct) ? NoContent() : NotFound();

    // ── Суралцагчдын өргөдөл ──

    [HttpGet("applications")]
    public async Task<ActionResult<JsonNode>> Applications(CancellationToken ct) =>
        Ok(await supabase.GetAsync($"rest/v1/applications?select={ApplicationSelect}&order=created_at.desc", await AccessTokenAsync(), ct));

    [HttpPatch("applications/{id:long}")]
    public async Task<ActionResult<JsonNode>> UpdateApplication(long id, JsonObject body, CancellationToken ct)
    {
        var row = await supabase.UpdateAsync("applications", $"id=eq.{id}", body.Pick(["status", "admin_note"]), await AccessTokenAsync(), ct);
        return row is null ? NotFound() : Ok(row);
    }

    // ── Хэрэглэгчид ──

    [HttpGet("users")]
    public async Task<ActionResult<JsonNode>> Users(CancellationToken ct) =>
        Ok(await supabase.GetAsync("rest/v1/profiles?order=created_at.desc", await AccessTokenAsync(), ct));

    /// <summary>
    /// Шинэ хэрэглэгч (багш, суралцагч, sysadmin) үүсгэнэ.
    /// Supabase:SecretKey тохируулсан бол и-мэйл нь шууд баталгаажсан. Үгүй бол суралцагчид баталгаажуулах и-мэйл
    /// илгээгдэнэ; sysadmin/багшийг DB trigger баталгаажуулна (06_staff_autoconfirm.sql).
    /// </summary>
    [HttpPost("users")]
    public async Task<ActionResult<CreateUserResponse>> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        var metadata = new JsonObject { ["full_name"] = request.FullName.Trim(), ["phone"] = request.Phone?.Trim() ?? "" };
        string userId;
        if (supabase.HasSecretKey)
        {
            userId = await supabase.CreateConfirmedUserAsync(new JsonObject
            {
                ["email"] = request.Email.Trim(),
                ["password"] = request.Password,
                ["email_confirm"] = true,
                ["user_metadata"] = metadata,
            }, ct);
        }
        else
        {
            var signup = await supabase.SendAsync(HttpMethod.Post, "auth/v1/signup",
                new JsonObject { ["email"] = request.Email.Trim(), ["password"] = request.Password, ["data"] = metadata }, ct: ct);
            // И-мэйл баталгаажуулалт асаалттай үед хариу нь user объект, унтраалттай үед { user, access_token, ... }
            userId = (signup?["user"]?["id"] ?? signup?["id"])?.GetValue<string>()
                ?? throw new SupabaseException(502, "Supabase did not return the new user.", null);
        }

        // Trigger profile-ийг "student" эрхтэй үүсгэсэн — сонгосон эрхийг онооно
        var patch = new JsonObject { ["role"] = request.Role };
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl)) patch["avatar_url"] = request.AvatarUrl;
        var profile = await supabase.UpdateAsync("profiles", $"id=eq.{userId}", patch, await AccessTokenAsync(), ct);
        // Бүртгэлтэй и-мэйлээр signup хийхэд Supabase хуурамч хэрэглэгч буцаадаг — profile олдохгүй
        if (profile is null) return Problem(statusCode: 409, detail: "A user with this email already exists.");
        // Sysadmin/багшийг DB trigger (profiles_confirm_staff) шууд баталгаажуулна — зөвхөн суралцагч и-мэйлээ баталгаажуулна
        return Ok(new CreateUserResponse(profile, !supabase.HasSecretKey && request.Role == Roles.Student));
    }

    [HttpPatch("users/{id:guid}")]
    public async Task<ActionResult<JsonNode>> UpdateUser(Guid id, JsonObject body, CancellationToken ct)
    {
        var patch = body.Pick(["role", "full_name", "phone", "birth_date", "parent_name", "parent_phone", "korean_level", "avatar_url"]);
        // Өөрийнхөө эрхийг бууруулж түгжигдэхээс сэргийлнэ
        if (id.ToString() == UserId && patch.ContainsKey("role"))
            return Problem(statusCode: 400, detail: "You cannot change your own role.");

        var row = await supabase.UpdateAsync("profiles", $"id=eq.{id}", patch, await AccessTokenAsync(), ct);
        roles.Forget(id.ToString());
        return row is null ? NotFound() : Ok(row);
    }
}

public record CreateUserRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MinLength(8), MaxLength(72)] string Password,
    [Required, MaxLength(120)] string FullName,
    [MaxLength(40)] string? Phone,
    [Required, RegularExpression("^(sysadmin|teacher|student)$")] string Role,
    [MaxLength(1000)] string? AvatarUrl);

/// <param name="RequiresConfirmation">true бол хэрэглэгч и-мэйлээ баталгаажуулсны дараа нэвтэрнэ.</param>
public record CreateUserResponse(JsonNode Profile, bool RequiresConfirmation);

public record StatsResponse(PaymentStats Payments, StudentStats Students, ApplicationStats Applications, RequestStats Requests, GroupStats Groups);

/// <param name="Paid">Нийт төлөгдсөн</param>
/// <param name="Due">Идэвхтэй элсэлтийн төлөх ёстой нийт дүн</param>
/// <param name="Outstanding">Идэвхтэй элсэлтийн дутуу төлбөр</param>
/// <param name="UnpaidCount">Бүрэн төлөөгүй идэвхтэй элсэлт</param>
public record PaymentStats(decimal Paid, decimal Due, decimal Outstanding, int UnpaidCount);

public record StudentStats(int Total, int Active, int NotEnrolled);

public record ApplicationStats(int Open, int LanguageCourse, int SchoolPlacement, int Accepted);

public record RequestStats(int New, int Contacted, int Total);

public record GroupStats(int Active, int Total, int Teachers);
