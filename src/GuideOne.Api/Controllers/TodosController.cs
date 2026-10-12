using GuideOne.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TodosController(Supabase.Client supabase) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TodoResponse>>> Get()
    {
        await supabase.InitializeAsync();
        var response = await supabase.From<Todo>().Get();
        return Ok(response.Models.Select(t => new TodoResponse(t.Id, t.Name)));
    }
}

public record TodoResponse(long Id, string Name);
