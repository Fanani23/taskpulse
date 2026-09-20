using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace TaskPulse.Api.Infrastructure;

// Keyset ("cursor") paging for the task list: the cursor is the sort key of the last row a client saw, so the next
// page is `WHERE (sort key) > (cursor)` — no OFFSET scan over everything before it, and a row inserted or moved
// meanwhile never shifts the pages (an offset page would repeat or skip one). Opaque to clients: base64url JSON.
public sealed record TaskCursor(bool Done, bool DueNull, DateTime? DueAt, DateTime CreatedAt, Guid Id, float? Rank)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Encode() => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(this, Json));

    // null for a cursor that was not produced by this API (or by another sort order)
    public static TaskCursor? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 400)
        {
            return null;
        }

        try
        {
            var cursor = JsonSerializer.Deserialize<TaskCursor>(Base64Url.DecodeFromChars(value), Json);
            return cursor is { Id: var id } && id != Guid.Empty ? cursor : null;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return null;
        }
    }
}
