using Microsoft.AspNetCore.Mvc;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/preferences")]
[Tags("Preferences")]
public sealed class PreferencesController(IPreferencesService preferences) : ControllerBase
{
    private const string UserRoute = "{userId:regex(^[[A-Za-z0-9]][[A-Za-z0-9._@+-]]{{0,63}}$)}";

    [HttpGet(UserRoute, Name = "GetPreferences")]
    [EndpointSummary("Saved UI preferences of one user; 404 until they have been saved once.")]
    [ProducesResponseType<Preferences>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Preferences>> Get(string userId, CancellationToken cancellationToken)
        => await preferences.GetAsync(userId, cancellationToken) is { } item ? Ok(item) : NotFound();

    [HttpPut(UserRoute, Name = "SavePreferences")]
    [EndpointSummary("Create or replace the preferences of one user.")]
    [ProducesResponseType<Preferences>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Preferences>> Save(string userId, UpsertPreferencesRequest request, CancellationToken cancellationToken)
        => Ok(await preferences.UpsertAsync(userId, request, cancellationToken));

    [HttpDelete(UserRoute, Name = "DeletePreferences")]
    [EndpointSummary("Forget the preferences of one user.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string userId, CancellationToken cancellationToken)
        => await preferences.DeleteAsync(userId, cancellationToken) ? NoContent() : NotFound();
}
