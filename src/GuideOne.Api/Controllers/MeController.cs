using System.Text.Json.Nodes;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

/// <summary>Нэвтэрсэн суралцагчийн өөрийн мэдээлэл: өргөдөл, анги, материал, зар, шалгалтын дүн.</summary>
[Authorize]
[Route("api/me")]
public class MeController(SupabaseRest supabase) : ApiControllerBase
{
    private const string ApplicationSelect = "*,course:courses(id,name),partner:partners(id,name)";

    [HttpGet("applications")]
    public async Task<ActionResult<JsonNode>> Applications(CancellationToken ct) =>
        Ok(await supabase.GetAsync(
            $"rest/v1/applications?student_id=eq.{UserId}&select={ApplicationSelect}&order=created_at.desc",
            await AccessTokenAsync(), ct));

    [HttpPost("applications")]
    public async Task<ActionResult<JsonNode>> Apply(JsonObject body, CancellationToken ct)
    {
        var row = body.Pick(["program", "course_id", "partner_id", "message"]);
        row["student_id"] = UserId;
        return Ok(await supabase.InsertAsync("applications", row, await AccessTokenAsync(), ct));
    }

    /// <summary>
    /// Суралцагчийн самбар. RLS нь зөвхөн өөрийн ангийн материал, зар, шалгалт,
    /// өөрийн дүнг (exam_results) буцаадаг.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<JsonNode>> Dashboard(CancellationToken ct)
    {
        var token = await AccessTokenAsync();
        var enrollments = supabase.GetAsync(
            $"rest/v1/enrollments?student_id=eq.{UserId}&select=id,status,payment_status,amount_due,amount_paid," +
            "group:groups(id,name,description,start_date,end_date,course:courses(name),teacher:profiles!teacher_id(full_name,email,phone))" +
            "&order=created_at.desc",
            token, ct);
        var materials = supabase.GetAsync("rest/v1/materials?select=*,group:groups(name)&order=created_at.desc&limit=100", token, ct);
        var announcements = supabase.GetAsync("rest/v1/announcements?select=*,group:groups(name)&order=created_at.desc&limit=50", token, ct);
        var exams = supabase.GetAsync(
            $"rest/v1/exams?select=*,group:groups(name),exam_results(score,comment)&exam_results.student_id=eq.{UserId}&order=exam_date.desc.nullslast",
            token, ct);
        await Task.WhenAll(enrollments, materials, announcements, exams);

        await supabase.AddFileUrlsAsync(await materials, token, ct);
        return new JsonObject
        {
            ["enrollments"] = await enrollments,
            ["materials"] = await materials,
            ["announcements"] = await announcements,
            ["exams"] = await exams,
        };
    }
}
