namespace NavyThunder.Data;

/// <summary>Aggregated validation failure with per-document messages.</summary>
public sealed class DataValidationException(IReadOnlyList<string> errors) : Exception(Format(errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;

    private static string Format(IReadOnlyList<string> errors)
        => "Data validation failed:\n  - " + string.Join("\n  - ", errors);
}
