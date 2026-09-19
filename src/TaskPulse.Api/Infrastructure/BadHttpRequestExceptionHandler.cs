using Microsoft.AspNetCore.Diagnostics;

namespace TaskPulse.Api.Infrastructure;

public sealed class BadHttpRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;

        var problem = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read.",
                Detail = badRequest.Message,
            },
        };

        if (badRequest.InnerException is { } inner)
        {
            problem.ProblemDetails.Extensions["reason"] = inner.Message;
        }

        return await problemDetails.TryWriteAsync(problem);
    }
}
