using Microsoft.AspNetCore.Mvc;

namespace TaskPulse.Api.Infrastructure;

// Weak ETags built from PostgreSQL's row version (xmin): GET answers with `ETag: W/"<version>"`, and a PUT that
// carries `If-Match` with a different version is refused with 412 instead of overwriting someone else's change.
public static class ETags
{
    public static string For(uint version) => $"W/\"{version}\"";

    public static void Set(HttpResponse response, uint version) => response.Headers.ETag = For(version);

    public static uint? Expected(HttpRequest request)
    {
        var header = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header) || header == "*")
        {
            return null;
        }

        var value = header.Trim();
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        value = value.Trim('"');
        return uint.TryParse(value, out var version) ? version : uint.MaxValue;
    }

    public static ObjectResult PreconditionFailed(ControllerBase controller)
        => controller.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "The record changed since you read it.", detail: "Re-read it and send its current ETag in If-Match.");
}
