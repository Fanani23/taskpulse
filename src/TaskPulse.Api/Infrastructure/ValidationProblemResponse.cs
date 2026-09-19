using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TaskPulse.Api.Infrastructure;

public static class ValidationProblemResponse
{
    public static IActionResult Create(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Instance = context.HttpContext.Request.Path,
        };

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}
