namespace ScalableVectorTemplates;

/// <summary>Positive inspection budgets.</summary>
public sealed class InspectionOptions
{
    /// <summary>Gets the MaxSourceBytes budget.</summary>
    public int MaxSourceBytes { get; init; } = 16777216;
    /// <summary>Gets the MaxXmlCharacters budget.</summary>
    public int MaxXmlCharacters { get; init; } = 16777216;
    /// <summary>Gets the MaxElementDepth budget.</summary>
    public int MaxElementDepth { get; init; } = 64;
    /// <summary>Gets the MaxElements budget.</summary>
    public int MaxElements { get; init; } = 100000;
    /// <summary>Gets the MaxAttributesPerElement budget.</summary>
    public int MaxAttributesPerElement { get; init; } = 256;
    /// <summary>Gets the MaxHooks budget.</summary>
    public int MaxHooks { get; init; } = 10000;
    /// <summary>Gets the MaxDiagnostics budget.</summary>
    public int MaxDiagnostics { get; init; } = 1000;

    internal InspectionOptions Snapshot()
    {
        var snapshot = new InspectionOptions
        {
            MaxSourceBytes = MaxSourceBytes,
            MaxXmlCharacters = MaxXmlCharacters,
            MaxElementDepth = MaxElementDepth,
            MaxElements = MaxElements,
            MaxAttributesPerElement = MaxAttributesPerElement,
            MaxHooks = MaxHooks,
            MaxDiagnostics = MaxDiagnostics,
        };
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.MaxSourceBytes, nameof(MaxSourceBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.MaxXmlCharacters, nameof(MaxXmlCharacters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.MaxElementDepth, nameof(MaxElementDepth));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.MaxElements, nameof(MaxElements));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.MaxAttributesPerElement, nameof(MaxAttributesPerElement));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.MaxHooks, nameof(MaxHooks));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshot.MaxDiagnostics, nameof(MaxDiagnostics));
        return snapshot;
    }
}
