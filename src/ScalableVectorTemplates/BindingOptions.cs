namespace ScalableVectorTemplates;

/// <summary>Immutable initialization options for binding preparation.</summary>
public sealed class BindingOptions
{
    /// <summary>Gets how unknown supplied keys are handled.</summary>
    public UnknownKeyBehavior UnknownKeyBehavior { get; init; } = UnknownKeyBehavior.Error;
    /// <summary>Gets the positive MaxBindings budget.</summary>
    public int MaxBindings { get; init; } = 10_000;
    /// <summary>Gets the positive MaxValueCharacters budget.</summary>
    public int MaxValueCharacters { get; init; } = 1_048_576;
    /// <summary>Gets the positive MaxTotalValueCharacters budget.</summary>
    public int MaxTotalValueCharacters { get; init; } = 8_388_608;
    /// <summary>Gets the positive MaxTotalEncodedImageBytes budget.</summary>
    public int MaxTotalEncodedImageBytes { get; init; } = 33_554_432;
    /// <summary>Gets the positive MaxOutputBytes budget.</summary>
    public int MaxOutputBytes { get; init; } = 67_108_864;
    /// <summary>Gets the positive MaxDiagnostics budget.</summary>
    public int MaxDiagnostics { get; init; } = 1_000;
    internal BindingOptions Snapshot()
    {
        var copy = new BindingOptions
        {
            MaxBindings = MaxBindings,
            MaxValueCharacters = MaxValueCharacters,
            MaxTotalValueCharacters = MaxTotalValueCharacters,
            MaxTotalEncodedImageBytes = MaxTotalEncodedImageBytes,
            MaxOutputBytes = MaxOutputBytes,
            MaxDiagnostics = MaxDiagnostics,
            UnknownKeyBehavior = UnknownKeyBehavior,
        };
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxBindings, nameof(MaxBindings));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxValueCharacters, nameof(MaxValueCharacters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxTotalValueCharacters, nameof(MaxTotalValueCharacters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxTotalEncodedImageBytes, nameof(MaxTotalEncodedImageBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxOutputBytes, nameof(MaxOutputBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copy.MaxDiagnostics, nameof(MaxDiagnostics));
        if (copy.UnknownKeyBehavior is not UnknownKeyBehavior.Error and not UnknownKeyBehavior.IgnoreWithWarning)
            throw new ArgumentOutOfRangeException(nameof(UnknownKeyBehavior));
        return copy;
    }
}
