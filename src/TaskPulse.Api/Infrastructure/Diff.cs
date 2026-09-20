using System.Text.Json;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Infrastructure;

// Field-level differences between two versions of a record, for the audit trail: `{ "label": {"from": "A", "to": "B"} }`.
// Catalog attributes are compared key by key (`attributes.color`), so a history reads as what actually changed.
public static class Diff
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static IReadOnlyDictionary<string, FieldChange>? Of(CatalogItem before, CatalogItem after)
    {
        var changes = new Dictionary<string, FieldChange>();
        Add(changes, "label", before.Label, after.Label);
        Add(changes, "parents", before.Parents, after.Parents);
        Add(changes, "sort", before.Sort, after.Sort);
        var oldAttributes = Keys(before.Attributes);
        var newAttributes = Keys(after.Attributes);
        foreach (var key in oldAttributes.Keys.Union(newAttributes.Keys).Order(StringComparer.Ordinal))
        {
            oldAttributes.TryGetValue(key, out var was);
            newAttributes.TryGetValue(key, out var now);
            if (was != now)
            {
                changes["attributes." + key] = new FieldChange(Parse(was), Parse(now));
            }
        }

        return changes.Count == 0 ? null : changes;
    }

    public static IReadOnlyDictionary<string, FieldChange>? Of(TaskItem before, TaskItem after)
    {
        var changes = new Dictionary<string, FieldChange>();
        Add(changes, "title", before.Title, after.Title);
        Add(changes, "description", before.Description, after.Description);
        Add(changes, "status", before.Status, after.Status);
        Add(changes, "priority", before.Priority, after.Priority);
        Add(changes, "dueAt", before.DueAt, after.DueAt);
        Add(changes, "assigneeId", before.AssigneeId, after.AssigneeId);
        Add(changes, "assigneeName", before.AssigneeName, after.AssigneeName);
        Add(changes, "labels", before.Labels, after.Labels);
        return changes.Count == 0 ? null : changes;
    }

    public static string? Serialize(IReadOnlyDictionary<string, FieldChange>? changes)
        => changes is null ? null : JsonSerializer.Serialize(changes, Json);

    private static void Add<T>(Dictionary<string, FieldChange> changes, string field, T before, T after)
    {
        var same = before is IEnumerable<string> a && after is IEnumerable<string> b ? a.SequenceEqual(b) : Equals(before, after);
        if (!same)
        {
            changes[field] = new FieldChange(before, after);
        }
    }

    // attribute values as their raw JSON text, so `1` and `"1"` are different and objects compare by content
    private static Dictionary<string, string> Keys(JsonElement? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is { ValueKind: JsonValueKind.Object } o)
        {
            foreach (var property in o.EnumerateObject())
            {
                result[property.Name] = property.Value.GetRawText();
            }
        }

        return result;
    }

    private static JsonElement? Parse(string? raw) => raw is null ? null : JsonSerializer.Deserialize<JsonElement>(raw);
}
