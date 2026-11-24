namespace ScalableVectorTemplates;

/// <summary>Policy for supplied keys absent from the inspected template.</summary>
public enum UnknownKeyBehavior
{
    /// <summary>Reject the binding operation.</summary>
    Error,
    /// <summary>Ignore the assignment and report a warning.</summary>
    IgnoreWithWarning,
}

/// <summary>Reusable, immutable binding and image preparation policy.</summary>
public sealed class BindingConfiguration
{
    /// <summary>Gets the immutable default configuration.</summary>
    public static BindingConfiguration Default { get; } = new();
    /// <summary>Validates and captures independent option snapshots.</summary>
    public BindingConfiguration(BindingOptions? bindingOptions = null, EncodedImageOptions? imageOptions = null)
    {
        BindingOptions = (bindingOptions ?? new()).Snapshot();
        ImageOptions = (imageOptions ?? new()).Snapshot();
    }
    /// <summary>Gets binding policy and budgets.</summary>
    public BindingOptions BindingOptions { get; }
    /// <summary>Gets image capture and admission budgets.</summary>
    public EncodedImageOptions ImageOptions { get; }
}
