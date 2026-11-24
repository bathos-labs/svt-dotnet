namespace ScalableVectorTemplates;

/// <summary>Positive, inclusive per-target interpretation work budgets.</summary>
public sealed class InterpretationOptions
{
    /// <summary>Gets the maximum complete paint length in UTF-16 code units.</summary>
    public int MaxPaintCharacters { get; init; } = 4096;
    /// <summary>Gets the maximum ordinary reference length in UTF-16 code units.</summary>
    public int MaxReferenceCharacters { get; init; } = 4096;
    /// <summary>Gets the maximum data-header length through its comma in UTF-16 code units.</summary>
    public int MaxDataUriHeaderCharacters { get; init; } = 1024;
    /// <summary>Gets the maximum numeric-token length, excluding its unit.</summary>
    public int MaxNumericTokenCharacters { get; init; } = 64;
    /// <summary>Gets the maximum simultaneously open function parentheses.</summary>
    public int MaxNestingDepth { get; init; } = 16;

    internal InterpretationOptions Snapshot()
    {
        var copy = new InterpretationOptions
        {
            MaxPaintCharacters = MaxPaintCharacters,
            MaxReferenceCharacters = MaxReferenceCharacters,
            MaxDataUriHeaderCharacters = MaxDataUriHeaderCharacters,
            MaxNumericTokenCharacters = MaxNumericTokenCharacters,
            MaxNestingDepth = MaxNestingDepth
        };
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxPaintCharacters, nameof(MaxPaintCharacters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxReferenceCharacters, nameof(MaxReferenceCharacters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxDataUriHeaderCharacters, nameof(MaxDataUriHeaderCharacters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxNumericTokenCharacters, nameof(MaxNumericTokenCharacters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxNestingDepth, nameof(MaxNestingDepth));
        return copy;
    }
}
