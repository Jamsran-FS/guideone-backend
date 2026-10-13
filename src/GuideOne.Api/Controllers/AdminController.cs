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

    [HttpGet("stats")]
    public async Task<ActionResult<StatsResponse>> Stats(CancellationToken ct)
    {
        var token = await AccessTokenAsync();
        var requests = supabase.GetAsync("rest/v1/contact_requests?status=eq.new&select=id", token, ct);
        var applications = supabase.GetAsync("rest/v1/applications?status=in.(new,in_review)&select=id", token, ct);
        var students = supabase.GetAsync("rest/v1/profiles?role=eq.student&select=id", token, ct);
        var active = supabase.GetAsync("rest/v1/enrollments?status=eq.active&select=student_id", token, ct);
        var unpaid = supabase.GetAsync("rest/v1/enrollments?status=eq.active&payment_status=neq.paid&select=id", token, ct);
        await Task.WhenAll(requests, applications, students, active, unpaid);

        var activeStudents = ((await active) as JsonArray ?? []).Select(r => r.Text("student_id")).Distinct().Count();
        return new StatsResponse((await requests).CountRows(), (await applications).CountRows(), (await students).CountRows(),
            activeStudents, (await unpaid).CountRows());
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

    [HttpPatch("users/{id:guid}")]
    public async Task<ActionResult<JsonNode>> UpdateUser(Guid id, JsonObject body, CancellationToken ct)
    {
        if (id.ToString() == UserId) return Problem(statusCode: 400, detail: "You cannot change your own role.");
        var row = await supabase.UpdateAsync("profiles", $"id=eq.{id}", body.Pick(["role"]), await AccessTokenAsync(), ct);
        roles.Forget(id.ToString());
        return row is null ? NotFound() : Ok(row);
    }
}

public record StatsResponse(int NewRequests, int OpenApplications, int Students, int ActiveStudents, int UnpaidEnrollments);
