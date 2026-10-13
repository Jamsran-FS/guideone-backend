using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GuideOne.Api.Auth;
using GuideOne.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

/// <summary>
/// Багш: өөрийн ангиуд, суралцагчид, материал, зар, шалгалт ба дүн.
/// Өөр багшийн ангид хандах эрхгүй — Supabase RLS (teaches_group) хамгаална.
/// </summary>
[Authorize(Policy = Policies.Teacher)]
[Route("api/teacher")]
public partial class TeacherController(SupabaseRest supabase) : ApiControllerBase
{
    private static readonly string[] ExamColumns = ["title", "description", "exam_date", "max_score"];

    [HttpGet("groups")]
    public async Task<ActionResult<JsonNode>> Groups(CancellationToken ct) =>
        Ok(await supabase.GetAsync(
            $"rest/v1/groups?teacher_id=eq.{UserId}&select=*,course:courses(id,name),enrollments(count)&order=is_active.desc,created_at.desc",
            await AccessTokenAsync(), ct));

    /// <summary>Ангийн бүх мэдээлэл: суралцагчид (төлбөргүй), материал, зар, шалгалт + дүн.</summary>
    [HttpGet("groups/{id:long}")]
    public async Task<ActionResult<JsonNode>> Group(long id, CancellationToken ct)
    {
        var token = await AccessTokenAsync();
        var group = supabase.GetAsync($"rest/v1/groups?id=eq.{id}&teacher_id=eq.{UserId}&select=*,course:courses(id,name)", token, ct);
        var students = supabase.GetAsync(
            $"rest/v1/enrollments?group_id=eq.{id}&select=id,status,student:profiles(id,full_name,email,phone,parent_name,parent_phone,korean_level)&order=created_at.asc",
            token, ct);
        var materials = supabase.GetAsync($"rest/v1/materials?group_id=eq.{id}&order=created_at.desc", token, ct);
        var announcements = supabase.GetAsync($"rest/v1/announcements?group_id=eq.{id}&order=created_at.desc", token, ct);
        var exams = supabase.GetAsync(
            $"rest/v1/exams?group_id=eq.{id}&select=*,exam_results(student_id,score,comment)&order=exam_date.asc.nullslast,id.asc",
            token, ct);
        await Task.WhenAll(group, students, materials, announcements, exams);

        if (await group is not JsonArray { Count: > 0 } groups) return NotFound();
        await supabase.AddFileUrlsAsync(await materials, token, ct);
        return new JsonObject
        {
            ["group"] = groups[0]!.DeepClone(),
            ["students"] = await students,
            ["materials"] = await materials,
            ["announcements"] = await announcements,
            ["exams"] = await exams,
        };
    }

    // ── Материал (гарын авлага, лекц, семинар) ──

    [HttpPost("groups/{id:long}/materials")]
    [RequestSizeLimit(MaterialFiles.MaxBytes + 256 * 1024)]
    public async Task<ActionResult<JsonNode>> AddMaterial(long id, [FromForm] MaterialForm form, CancellationToken ct)
    {
        if (form.File is null && string.IsNullOrWhiteSpace(form.LinkUrl))
            return Problem(statusCode: 400, detail: "Attach a file or provide a link.");
        if (form.File is { Length: 0 or > MaterialFiles.MaxBytes })
            return Problem(statusCode: 400, detail: "The file must be 20 MB or smaller.");

        var token = await AccessTokenAsync();
        var row = new JsonObject
        {
            ["group_id"] = id,
            ["author_id"] = UserId,
            ["kind"] = form.Kind,
            ["title"] = form.Title.Trim(),
            ["description"] = form.Description?.Trim() ?? "",
            ["link_url"] = string.IsNullOrWhiteSpace(form.LinkUrl) ? null : form.LinkUrl.Trim(),
        };

        if (form.File is { } file)
        {
            var extension = SafeExtension().Match(Path.GetExtension(file.FileName).ToLowerInvariant()).Value;
            var path = $"{id}/{Guid.NewGuid():N}{extension}";
            await using var stream = file.OpenReadStream();
            await supabase.UploadAsync($"storage/v1/object/{MaterialFiles.Bucket}/{path}", stream, file.ContentType, token, ct);
            row["file_path"] = path;
            row["file_name"] = Path.GetFileName(file.FileName) is { Length: > 200 } name ? name[..200] : Path.GetFileName(file.FileName);
        }

        return Ok(await supabase.InsertAsync("materials", row, token, ct));
    }

    [HttpDelete("materials/{id:long}")]
    public async Task<IActionResult> DeleteMaterial(long id, CancellationToken ct)
    {
        var token = await AccessTokenAsync();
        var rows = await supabase.SendAsync(HttpMethod.Delete, $"rest/v1/materials?id=eq.{id}", null, token, "return=representation", ct);
        if (rows is not JsonArray { Count: > 0 } deleted) return NotFound();
        if (deleted[0].Text("file_path") is { } path) await supabase.DeleteFileAsync(MaterialFiles.Bucket, path, token, ct);
        return NoContent();
    }

    // ── Зар мэдээ ──

    [HttpPost("groups/{id:long}/announcements")]
    public async Task<ActionResult<JsonNode>> Announce(long id, AnnouncementRequest request, CancellationToken ct) =>
        Ok(await supabase.InsertAsync("announcements", new JsonObject
        {
            ["group_id"] = id,
            ["author_id"] = UserId,
            ["title"] = request.Title.Trim(),
            ["body"] = request.Body?.Trim() ?? "",
        }, await AccessTokenAsync(), ct));

    [HttpDelete("announcements/{id:long}")]
    public async Task<IActionResult> DeleteAnnouncement(long id, CancellationToken ct) =>
        await supabase.DeleteAsync("announcements", $"id=eq.{id}", await AccessTokenAsync(), ct) ? NoContent() : NotFound();

    // ── Шалгалт ба дүн ──

    [HttpPost("groups/{id:long}/exams")]
    public async Task<ActionResult<JsonNode>> CreateExam(long id, JsonObject body, CancellationToken ct)
    {
        var row = body.Pick(ExamColumns);
        row["group_id"] = id;
        row["created_by"] = UserId;
        return Ok(await supabase.InsertAsync("exams", row, await AccessTokenAsync(), ct));
    }

    [HttpPut("exams/{id:long}")]
    public async Task<ActionResult<JsonNode>> UpdateExam(long id, JsonObject body, CancellationToken ct)
    {
        var row = await supabase.UpdateAsync("exams", $"id=eq.{id}", body.Pick(ExamColumns), await AccessTokenAsync(), ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpDelete("exams/{id:long}")]
    public async Task<IActionResult> DeleteExam(long id, CancellationToken ct) =>
        await supabase.DeleteAsync("exams", $"id=eq.{id}", await AccessTokenAsync(), ct) ? NoContent() : NotFound();

    /// <summary>Шалгалтын дүнг бөөнөөр хадгална. Зөвхөн тухайн ангийн суралцагчид, 0..max_score.</summary>
    [HttpPut("exams/{id:long}/results")]
    public async Task<ActionResult<JsonNode>> SaveResults(long id, List<ResultRequest> results, CancellationToken ct)
    {
        var token = await AccessTokenAsync();
        var exams = await supabase.GetAsync($"rest/v1/exams?id=eq.{id}&select=group_id,max_score", token, ct);
        if (exams is not JsonArray { Count: > 0 } found) return NotFound();
        var groupId = found[0]!["group_id"]!.GetValue<long>();
        var maxScore = found[0]!["max_score"]!.GetValue<decimal>();

        var enrolled = await supabase.GetAsync($"rest/v1/enrollments?group_id=eq.{groupId}&select=student_id", token, ct);
        var studentIds = (enrolled as JsonArray ?? []).Select(r => r.Text("student_id")).OfType<string>().ToHashSet();

        var rows = new JsonArray();
        foreach (var r in results)
        {
            if (!studentIds.Contains(r.StudentId.ToString()))
                return Problem(statusCode: 400, detail: "A student is not enrolled in this group.");
            if (r.Score is < 0 || r.Score > maxScore)
                return Problem(statusCode: 400, detail: $"Scores must be between 0 and {maxScore}.");
            rows.Add(new JsonObject
            {
                ["exam_id"] = id,
                ["student_id"] = r.StudentId.ToString(),
                ["score"] = r.Score,
                ["comment"] = r.Comment?.Trim() ?? "",
            });
        }
        if (rows.Count == 0) return Ok(new JsonArray());

        return Ok(await supabase.SendAsync(HttpMethod.Post, "rest/v1/exam_results?on_conflict=exam_id,student_id", rows, token,
            "resolution=merge-duplicates,return=representation", ct));
    }

    [GeneratedRegex(@"^\.[a-z0-9]{1,8}$")]
    private static partial Regex SafeExtension();
}

public class MaterialForm
{
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(4000)] public string? Description { get; set; }
    [Required, RegularExpression("^(handbook|lecture|seminar|other)$")] public string Kind { get; set; } = "lecture";
    [Url, MaxLength(1000)] public string? LinkUrl { get; set; }
    public IFormFile? File { get; set; }
}

public record AnnouncementRequest([Required, MaxLength(200)] string Title, [MaxLength(8000)] string? Body);

public record ResultRequest(Guid StudentId, decimal? Score, [MaxLength(1000)] string? Comment);
