using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskPulse.Api.Models;

public static partial class CatalogLimits
{
    public const int CodeMaxLength = 64;
    public const int LabelMaxLength = 120;
    public const int ParentsMax = 50;
    public const int AttributesMaxBytes = 4096;
    public const int ListMax = 500;
    public const string CodePattern = "^[a-z0-9][a-z0-9-]{0,63}$";

    [GeneratedRegex(CodePattern)]
    private static partial Regex CodeRegex();

    public static bool IsCode(string? value) => value is not null && CodeRegex().IsMatch(value);

    public static string Slug(string label)
    {
        var chars = label.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var collapsed = Regex.Replace(new string(chars), "-+", "-").Trim('-');
        return collapsed.Length > CodeMaxLength ? collapsed[..CodeMaxLength].TrimEnd('-') : collapsed;
    }
}

public sealed record CatalogItem(
    Guid Id,
    string Kind,
    string Code,
    string Label,
    IReadOnlyList<string> Parents,
    JsonElement? Attributes,
    int Sort,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CatalogKindSummary(string Kind, int Count);

public sealed record CatalogListQuery
{
    public string? Parent { get; init; }

    [StringLength(TaskLimits.SearchMaxLength, ErrorMessage = "q must be at most {1} characters.")]
    public string? Q { get; init; }
}

public abstract record CatalogItemRequest : IValidatableObject
{
    [Required(ErrorMessage = "Label is required.")]
    [StringLength(CatalogLimits.LabelMaxLength, ErrorMessage = "Label must be at most {1} characters.")]
    public string? Label { get; init; }

    public string[]? Parents { get; init; }

    public JsonElement? Attributes { get; init; }

    public int? Sort { get; init; }

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            yield return new ValidationResult("Label is required.", [nameof(Label)]);
        }

        if (Parents is { Length: > CatalogLimits.ParentsMax })
        {
            yield return new ValidationResult($"At most {CatalogLimits.ParentsMax} parents are allowed.", [nameof(Parents)]);
        }
        else if (Parents is not null && Parents.Any(p => !CatalogLimits.IsCode(p)))
        {
            yield return new ValidationResult($"Every parent must be a code matching {CatalogLimits.CodePattern}.", [nameof(Parents)]);
        }

        if (Attributes is { } attributes && attributes.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
        {
            yield return new ValidationResult("Attributes must be a JSON object.", [nameof(Attributes)]);
        }
        else if (Attributes is { ValueKind: JsonValueKind.Object } value && value.GetRawText().Length > CatalogLimits.AttributesMaxBytes)
        {
            yield return new ValidationResult($"Attributes must be at most {CatalogLimits.AttributesMaxBytes} bytes.", [nameof(Attributes)]);
        }
    }
}

public sealed record CreateCatalogItemRequest : CatalogItemRequest
{
    public string? Code { get; init; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (Code is not null && !CatalogLimits.IsCode(Code))
        {
            yield return new ValidationResult($"Code must match {CatalogLimits.CodePattern}.", [nameof(Code)]);
        }
    }
}

public sealed record UpdateCatalogItemRequest : CatalogItemRequest;
