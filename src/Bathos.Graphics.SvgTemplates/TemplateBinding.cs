namespace Bathos.Graphics.SvgTemplates;

/// <summary>An immutable, explicitly typed replacement instruction.</summary>
public sealed class TemplateBinding
{
    private TemplateBinding(string key, InputKind kind, string? value, EncodedImage? image)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        Kind = kind;
        Value = value;
        Image = image;
    }
    /// <summary>Gets the exact input key.</summary>
    public string Key { get; }
    /// <summary>Gets the intended replacement kind.</summary>
    public InputKind Kind { get; }
    internal string? Value { get; }
    internal EncodedImage? Image { get; }
    /// <summary>Creates literal text replacement, including an empty value.</summary>
    public static TemplateBinding Text(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(key, InputKind.Text, value, null);
    }
    /// <summary>Creates raw paint replacement.</summary>
    public static TemplateBinding Paint(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(key, InputKind.Paint, value, null);
    }
    /// <summary>Creates raw image-reference replacement without resolving it.</summary>
    public static TemplateBinding ImageReference(string key, string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(key, InputKind.Image, reference, null);
    }
    /// <summary>Creates replacement from independently captured encoded content.</summary>
    public static TemplateBinding ImageContent(string key, EncodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new(key, InputKind.Image, null, image);
    }
}
