using Microsoft.AspNetCore.Mvc;

namespace GuideOne.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public ActionResult<PingResponse> Get() => Ok(new PingResponse("pong", DateTimeOffset.UtcNow));
}

public record PingResponse(string Message, DateTimeOffset Timestamp);
