namespace Bathos.Graphics.SvgTemplates;

/// <summary>Explicit, pure interpretation of individual immutable authored targets.</summary>
public static class DefaultInterpreter
{
    /// <summary>Interprets a fill or stroke without changing discovery or binding evidence.</summary>
    public static PaintInterpretation InterpretPaint(TemplateTarget target, InterpretationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(target);
        if (target.Hook is not (TargetHook.Fill or TargetHook.Stroke))
            throw new ArgumentException("Paint interpretation requires a fill or stroke target.", nameof(target));
        var limits = (options ?? new()).Snapshot();
        PaintInterpretation result;
        if (target.Default.Raw is not { } raw) result = new(target, PaintInterpretationKind.Absent);
        else if (raw.Length > limits.MaxPaintCharacters)
            result = new(target, PaintInterpretationKind.UnsupportedOrUnrecognized, exceededLimit: InterpretationLimit.PaintCharacters);
        else
        {
            var parsed = PaintInterpreter.Parse(raw.AsSpan(), limits, cancellationToken);
            result = new(target, parsed.Kind, parsed.Color, parsed.Limit);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Classifies an image reference without opening it or examining data-URI payloads.</summary>
    public static ImageInterpretation InterpretImage(TemplateTarget target, InterpretationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(target);
        if (target.Hook != TargetHook.Image)
            throw new ArgumentException("Image interpretation requires an image target.", nameof(target));
        var limits = (options ?? new()).Snapshot();
        var result = target.Default.Raw is { } raw
            ? ImageInterpreter.Parse(target, raw.AsSpan(), limits, cancellationToken)
            : new ImageInterpretation(target, ImageInterpretationKind.Absent);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
