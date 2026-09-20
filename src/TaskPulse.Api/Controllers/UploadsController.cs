using Microsoft.AspNetCore.Mvc;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/uploads")]
[Tags("Uploads")]
public sealed class UploadsController(IUploadService uploads) : ControllerBase
{
    [HttpGet(Name = "ListUploads")]
    [EndpointSummary("Newest uploads first (max 100), optionally filtered by the source tag.")]
    [ProducesResponseType<IReadOnlyList<UploadItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UploadItem>>> List([FromQuery] UploadListQuery query, CancellationToken cancellationToken)
        => Ok(await uploads.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}", Name = "GetUpload")]
    [EndpointSummary("Metadata of one upload.")]
    [ProducesResponseType<UploadItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadItem>> Get(Guid id, CancellationToken cancellationToken)
        => await uploads.GetAsync(id, cancellationToken) is { } item ? Ok(item) : NotFound();

    [HttpGet("{id:guid}/content", Name = "DownloadUpload")]
    [EndpointSummary("The stored bytes, served with the original content type.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var content = await uploads.OpenAsync(id, cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=3600";
        return File(content.Content, content.Item.ContentType, content.Item.FileName, enableRangeProcessing: true);
    }

    [HttpPost(Name = "CreateUploads")]
    [EndpointSummary("Store one or more files (multipart/form-data: files[], optional source tag and note).")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<IReadOnlyList<UploadItem>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<IReadOnlyList<UploadItem>>> Create([FromForm] UploadForm form, CancellationToken cancellationToken)
    {
        if (form.Files is null || form.Files.Count == 0)
        {
            ModelState.AddModelError("files", "At least one file is required.");
            return ValidationProblem(ModelState);
        }

        var result = await uploads.CreateAsync(form, cancellationToken);
        return result.Rejection switch
        {
            UploadRejection.None => Created(Url.RouteUrl("ListUploads")!, result.Items),
            UploadRejection.TooLarge => Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "File too large.", detail: result.FileName),
            UploadRejection.UnsupportedType => Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Unsupported file type (png, jpeg, webp, pdf or plain text).", detail: result.FileName),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: $"At most {UploadLimits.FilesPerRequestMax} files per request."),
        };
    }

    [HttpDelete("{id:guid}", Name = "DeleteUpload")]
    [EndpointSummary("Delete an upload and its stored bytes.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => await uploads.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
