using Microsoft.AspNetCore.Diagnostics;

namespace GuideOne.Api.Infrastructure;

/// <summary>SupabaseException-ийг ProblemDetails хариу болгоно (Supabase-ийн 5xx → 502).</summary>
public sealed class SupabaseExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not SupabaseException error) return false;

        var status = error.Status >= 500 ? StatusCodes.Status502BadGateway : error.Status;
        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Detail = error.Message, Extensions = { ["code"] = error.Code } },
        });
    }
}
