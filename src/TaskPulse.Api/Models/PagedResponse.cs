namespace TaskPulse.Api.Models;

// Page/PageSize/Total describe the offset view; NextCursor (when the page was full) continues the same list by keyset:
// pass it as ?cursor= and page numbers keep counting from the one you sent.
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, string? NextCursor = null);
