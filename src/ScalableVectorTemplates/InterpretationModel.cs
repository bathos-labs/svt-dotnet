namespace ScalableVectorTemplates;

/// <summary>The bounded interpretation of an authored paint value.</summary>
public enum PaintInterpretationKind
{
    /// <summary>No authored attribute.</summary>
    Absent,
    /// <summary>A supported self-contained sRGB color.</summary>
    Literal,
    /// <summary>The authored value requests no paint.</summary>
    NoPaint,
    /// <summary>A recognized dependency on external CSS or painting context.</summary>
    RequiresContext,
    /// <summary>A paint-server reference, with no fallback evaluation.</summary>
    ResourceReference,
    /// <summary>No interpretation under the supported grammar and budgets.</summary>
    UnsupportedOrUnrecognized
}

/// <summary>The lexical shape of an authored image reference, without resource validation.</summary>
public enum ImageInterpretationKind
{
    /// <summary>No authored attribute.</summary>
    Absent,
    /// <summary>A present zero-length string.</summary>
    Empty,
    /// <summary>A fragment-only reference shape.</summary>
    FragmentReference,
    /// <summary>A relative reference shape.</summary>
    RelativeReference,
    /// <summary>A reference with an explicit scheme.</summary>
    AbsoluteReference,
    /// <summary>A recognized data-URI header; payload remains unexamined.</summary>
    DataUri,
    /// <summary>No classification under the supported grammar and budgets.</summary>
    UnsupportedOrUnrecognized
}

/// <summary>The work budget that prevented interpretation.</summary>
public enum InterpretationLimit
{
    /// <summary>Complete raw paint length.</summary>
    PaintCharacters,
    /// <summary>Complete ordinary image-reference length.</summary>
    ReferenceCharacters,
    /// <summary>Data-URI header length, including its comma.</summary>
    DataUriHeaderCharacters,
    /// <summary>Numeric token length, without unit or percent suffix.</summary>
    NumericTokenCharacters,
    /// <summary>Simultaneously open function parentheses.</summary>
    NestingDepth
}

/// <summary>Immutable normalized encoded sRGB with unpremultiplied alpha.</summary>
public sealed class SrgbColor
{
    internal SrgbColor(double red, double green, double blue, double alpha)
    {
        Red = Normalize(red);
        Green = Normalize(green);
        Blue = Normalize(blue);
        Alpha = Normalize(alpha);
    }

    /// <summary>Gets the red component in [0,1].</summary>
    public double Red { get; }
    /// <summary>Gets the green component in [0,1].</summary>
    public double Green { get; }
    /// <summary>Gets the blue component in [0,1].</summary>
    public double Blue { get; }
    /// <summary>Gets unpremultiplied alpha in [0,1].</summary>
    public double Alpha { get; }

    private static double Normalize(double value) => value <= 0 ? 0 : Math.Min(value, 1);
}

/// <summary>Immutable paint interpretation retaining the original target evidence.</summary>
public sealed class PaintInterpretation
{
    internal PaintInterpretation(TemplateTarget target, PaintInterpretationKind kind,
        SrgbColor? color = null, InterpretationLimit? exceededLimit = null)
    {
        Target = target;
        Kind = kind;
        Color = color;
        ExceededLimit = exceededLimit;
    }
    /// <summary>Gets the exact supplied target instance.</summary>
    public TemplateTarget Target { get; }
    /// <summary>Gets the interpretation outcome.</summary>
    public PaintInterpretationKind Kind { get; }
    /// <summary>Gets the color only for Literal.</summary>
    public SrgbColor? Color { get; }
    /// <summary>Gets the exceeded work budget, if any.</summary>
    public InterpretationLimit? ExceededLimit { get; }
}

/// <summary>Immutable image interpretation retaining the original reference evidence.</summary>
public sealed class ImageInterpretation
{
    internal ImageInterpretation(TemplateTarget target, ImageInterpretationKind kind,
        string? declaredMediaType = null, InterpretationLimit? exceededLimit = null)
    {
        Target = target;
        Kind = kind;
        DeclaredMediaType = declaredMediaType;
        ExceededLimit = exceededLimit;
    }
    /// <summary>Gets the exact supplied target instance.</summary>
    public TemplateTarget Target { get; }
    /// <summary>Gets the lexical classification.</summary>
    public ImageInterpretationKind Kind { get; }
    /// <summary>Gets an explicit recognized data-header type/subtype, without parameters.</summary>
    public string? DeclaredMediaType { get; }
    /// <summary>Gets the exceeded work budget, if any.</summary>
    public InterpretationLimit? ExceededLimit { get; }
}
