using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/preferences")]
[Tags("Preferences")]
public sealed class PreferencesController(IPreferencesService preferences, ICurrentUser user) : ControllerBase
{
    private const string UserRoute = "{userId:regex(^[[A-Za-z0-9]][[A-Za-z0-9._@+-]]{{0,63}}$)}";

    [HttpGet(UserRoute, Name = "GetPreferences")]
    [EndpointSummary("UI preferences of one user; the defaults with saved=false until they have been saved once.")]
    [ProducesResponseType<Preferences>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Preferences>> Get(string userId, CancellationToken cancellationToken)
        => Ok(await preferences.GetAsync(userId, cancellationToken) ?? Preferences.Defaults(userId));

    [HttpPut(UserRoute, Name = "SavePreferences")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Create or replace your own preferences (the userId must be user-<sub> of the bearer token).")]
    [ProducesResponseType<Preferences>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Preferences>> Save(string userId, UpsertPreferencesRequest request, CancellationToken cancellationToken)
        => Owns(userId) ? Ok(await preferences.UpsertAsync(userId, request, cancellationToken)) : NotYours();

    [HttpDelete(UserRoute, Name = "DeletePreferences")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Forget your own preferences.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string userId, CancellationToken cancellationToken)
        => !Owns(userId) ? NotYours() : await preferences.DeleteAsync(userId, cancellationToken) ? NoContent() : NotFound();

    private bool Owns(string userId)
        => user.IsAdmin || string.Equals(userId, user.PreferencesKey, StringComparison.OrdinalIgnoreCase) || string.Equals(userId, user.Sub, StringComparison.OrdinalIgnoreCase);

    private ObjectResult NotYours()
        => Problem(statusCode: StatusCodes.Status403Forbidden, title: "You can only change your own preferences.", detail: $"Your key is {user.PreferencesKey}.");
}
