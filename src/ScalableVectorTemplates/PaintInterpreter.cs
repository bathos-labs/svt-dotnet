using System.Collections.Frozen;

namespace ScalableVectorTemplates;

internal static class PaintInterpreter
{
    // CSS Color 4 (2026-11-18), section 6.2 and Appendix A, plus SVG/CSS context keywords.
    private static readonly FrozenSet<string> Context = new[]
    {
        "currentcolor", "inherit", "initial", "unset", "revert", "revert-layer", "context-fill", "context-stroke",
        "accentcolor", "accentcolortext", "activetext", "buttonborder", "buttonface", "buttontext", "canvas", "canvastext",
        "field", "fieldtext", "graytext", "highlight", "highlighttext", "linktext", "mark", "marktext", "selecteditem", "selecteditemtext", "visitedtext",
        "activeborder", "activecaption", "appworkspace", "background", "buttonhighlight", "buttonshadow", "captiontext",
        "inactiveborder", "inactivecaption", "inactivecaptiontext", "infobackground", "infotext", "menu", "menutext",
        "scrollbar", "threeddarkshadow", "threedface", "threedhighlight", "threedlightshadow", "threedshadow", "window", "windowframe", "windowtext"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static (PaintInterpretationKind Kind, SrgbColor? Color, InterpretationLimit? Limit) Parse(
        ReadOnlySpan<char> raw, InterpretationOptions limits, CancellationToken token)
    {
        var text = InterpretationSyntax.Trim(raw, token);
        if (!PaintScan.Read(text, limits, token, out var functions, out var exceeded))
            return (PaintInterpretationKind.UnsupportedOrUnrecognized, null, exceeded);
        if (InterpretationSyntax.Equal(text, "none")) return (PaintInterpretationKind.NoPaint, null, null);
        if (Context.Contains(text.ToString())) return (PaintInterpretationKind.RequiresContext, null, null);
        if (functions.Count > 0 && functions[0].Start == 0 && InterpretationSyntax.Equal(text[..functions[0].NameLength], "url"))
        {
            var end = functions[0].End;
            if (end == text.Length) return (PaintInterpretationKind.ResourceReference, null, null);
            if (!InterpretationSyntax.Space(text[end])) return Unsupported();
            InterpretationSyntax.SkipSpace(text, ref end, token);
            var fallback = text[end..];
            if (InterpretationSyntax.Equal(fallback, "none") || InterpretationSyntax.Equal(fallback, "currentcolor") ||
                IsContext(functions, end, text.Length) || ColorLiteral.TryRead(fallback, token, out _))
                return (PaintInterpretationKind.ResourceReference, null, null);
            return Unsupported();
        }
        if (IsContext(functions, 0, text.Length)) return (PaintInterpretationKind.RequiresContext, null, null);
        if (ColorLiteral.TryRead(text, token, out var color)) return (PaintInterpretationKind.Literal, color, null);
        return Unsupported();
    }

    private static bool IsContext(List<PaintScan.Function> functions, int start, int end) =>
        functions.Any(function => function.Start == start && function.End == end && function.HasVariable);

    private static (PaintInterpretationKind, SrgbColor?, InterpretationLimit?) Unsupported() => (PaintInterpretationKind.UnsupportedOrUnrecognized, null, null);
}
