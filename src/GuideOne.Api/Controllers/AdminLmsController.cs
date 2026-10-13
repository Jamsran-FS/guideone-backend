using System.Text.Json.Nodes;
using GuideOne.Api.Auth;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

/// <summary>Sysadmin: анги, элсэлт ба төлбөр, суралцагчдын нэгдсэн мэдээлэл (анги, төлбөр, шалгалтын дүн, өргөдөл).</summary>
[Authorize(Policy = Policies.Sysadmin)]
[Route("api/admin")]
public class AdminLmsController(SupabaseRest supabase) : ApiControllerBase
{
    private static readonly string[] GroupColumns = ["name", "description", "teacher_id", "course_id", "start_date", "end_date", "is_active"];
    private static readonly string[] EnrollmentColumns = ["status", "payment_status", "amount_due", "amount_paid", "note"];

    // groups → profiles холбоо 2 янз (teacher_id, enrollments-ээр) тул !teacher_id гэж заана
    private const string GroupSelect = "*,teacher:profiles!teacher_id(id,full_name,email),course:courses(id,name)";

    // ── Ангиуд ──

    [HttpGet("groups")]
    public async Task<ActionResult<JsonNode>> Groups(CancellationToken ct) =>
        Ok(await supabase.GetAsync($"rest/v1/groups?select={GroupSelect},enrollments(count)&order=is_active.desc,created_at.desc",
            await AccessTokenAsync(), ct));

    [HttpGet("groups/{id:long}")]
    public async Task<ActionResult<JsonNode>> Group(long id, CancellationToken ct)
    {
        var rows = await supabase.GetAsync(
            $"rest/v1/groups?id=eq.{id}&select={GroupSelect}," +
            "enrollments(*,student:profiles(id,full_name,email,phone,parent_name,parent_phone))",
            await AccessTokenAsync(), ct);
        return rows is JsonArray { Count: > 0 } array ? Ok(array[0]!.DeepClone()) : NotFound();
    }

    [HttpPost("groups")]
    public async Task<ActionResult<JsonNode>> CreateGroup(JsonObject body, CancellationToken ct) =>
        Ok(await supabase.InsertAsync("groups", body.Pick(GroupColumns), await AccessTokenAsync(), ct));

    [HttpPut("groups/{id:long}")]
    public async Task<ActionResult<JsonNode>> UpdateGroup(long id, JsonObject body, CancellationToken ct)
    {
        var row = await supabase.UpdateAsync("groups", $"id=eq.{id}", body.Pick(GroupColumns), await AccessTokenAsync(), ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpDelete("groups/{id:long}")]
    public async Task<IActionResult> DeleteGroup(long id, CancellationToken ct) =>
        await supabase.DeleteAsync("groups", $"id=eq.{id}", await AccessTokenAsync(), ct) ? NoContent() : NotFound();

    // ── Элсэлт, төлбөр ──

    [HttpPost("enrollments")]
    public async Task<ActionResult<JsonNode>> Enroll(JsonObject body, CancellationToken ct) =>
        Ok(await supabase.InsertAsync("enrollments", body.Pick(["group_id", "student_id", .. EnrollmentColumns]), await AccessTokenAsync(), ct));

    [HttpPatch("enrollments/{id:long}")]
    public async Task<ActionResult<JsonNode>> UpdateEnrollment(long id, JsonObject body, CancellationToken ct)
    {
        var row = await supabase.UpdateAsync("enrollments", $"id=eq.{id}", body.Pick(EnrollmentColumns), await AccessTokenAsync(), ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpDelete("enrollments/{id:long}")]
    public async Task<IActionResult> DeleteEnrollment(long id, CancellationToken ct) =>
        await supabase.DeleteAsync("enrollments", $"id=eq.{id}", await AccessTokenAsync(), ct) ? NoContent() : NotFound();

    // ── Суралцагчдын нэгдсэн мэдээлэл ──

    /// <summary>Суралцагч бүрийн анги, төлбөр, шалгалтын дүн, хүсэж буй сургууль (өргөдөл).</summary>
    [HttpGet("students")]
    public async Task<ActionResult<JsonNode>> Students(CancellationToken ct) =>
        Ok(await supabase.GetAsync(
            "rest/v1/profiles?role=eq.student&order=full_name.asc&select=" +
            "id,full_name,email,phone,birth_date,parent_name,parent_phone,korean_level,created_at," +
            "enrollments(id,status,payment_status,amount_due,amount_paid,group:groups(id,name))," +
            "exam_results(score,comment,exam:exams(id,title,max_score,exam_date,group_id))," +
            "applications(id,program,status,course:courses(name),partner:partners(name))",
            await AccessTokenAsync(), ct));
}
