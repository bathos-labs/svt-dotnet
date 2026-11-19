using System.Text;

namespace Bathos.Graphics.SvgTemplates;

internal static class ImageInterpreter
{
    internal static ImageInterpretation Parse(TemplateTarget target, ReadOnlySpan<char> text,
        InterpretationOptions limits, CancellationToken token)
    {
        if (text.IsEmpty) return new(target, ImageInterpretationKind.Empty);
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return Data(target, text, limits, token);
        if (text.Length > limits.MaxReferenceCharacters)
            return new(target, ImageInterpretationKind.UnsupportedOrUnrecognized, exceededLimit: InterpretationLimit.ReferenceCharacters);
        if (!InterpretationSyntax.ReferenceCharacters(text, token)) return Unsupported(target);
        if (text[0] == '#') return new(target, ImageInterpretationKind.FragmentReference);
        for (var i = 0; i < text.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (text[i] is '/' or '?' or '#') break;
            if (text[i] != ':') continue;
            if (i == 0 || !InterpretationSyntax.Letter(text[0])) return Unsupported(target);
            for (var j = 1; j < i; j++)
            {
                token.ThrowIfCancellationRequested();
                if (!InterpretationSyntax.Letter(text[j]) && !InterpretationSyntax.Digit(text[j]) && text[j] is not ('+' or '-' or '.'))
                    return Unsupported(target);
            }
            return new(target, ImageInterpretationKind.AbsoluteReference);
        }
        return new(target, ImageInterpretationKind.RelativeReference);
    }

    private static ImageInterpretation Data(TemplateTarget target, ReadOnlySpan<char> text,
        InterpretationOptions limits, CancellationToken token)
    {
        var comma = -1;
        // Even failed headers never require access to payload characters.
        for (var i = 0; i < Math.Min(text.Length, limits.MaxDataUriHeaderCharacters); i++)
        {
            token.ThrowIfCancellationRequested();
            if (text[i] == ',') { comma = i; break; }
        }
        if (comma < 0) return new(target, ImageInterpretationKind.UnsupportedOrUnrecognized,
            exceededLimit: text.Length > limits.MaxDataUriHeaderCharacters ? InterpretationLimit.DataUriHeaderCharacters : null);
        var header = text[5..comma];
        var first = header.IndexOf(';');
        var type = first < 0 ? header : header[..first];
        string? mediaType = null;
        if (!type.IsEmpty)
        {
            var slash = type.IndexOf('/');
            if (slash < 0 || !Token(type[..slash], token, out var major) || !Token(type[(slash + 1)..], token, out var minor)) return Unsupported(target);
            mediaType = major + "/" + minor;
        }
        var position = first;
        while (position >= 0)
        {
            token.ThrowIfCancellationRequested();
            var rest = header[(position + 1)..];
            var next = rest.IndexOf(';');
            var item = next < 0 ? rest : rest[..next];
            if (InterpretationSyntax.Equal(item, "base64"))
            {
                if (next >= 0) return Unsupported(target);
            }
            else
            {
                var equals = item.IndexOf('=');
                if (equals < 0 || !Token(item[..equals], token, out _) || equals == item.Length - 1) return Unsupported(target);
                var value = item[(equals + 1)..];
                if (value.Contains('#') || value.Contains('\'') || !InterpretationSyntax.ReferenceCharacters(value, token)) return Unsupported(target);
            }
            position = next < 0 ? -1 : position + 1 + next;
        }
        return new(target, ImageInterpretationKind.DataUri, mediaType);
    }

    private static bool Token(ReadOnlySpan<char> text, CancellationToken token, out string value)
    {
        value = string.Empty;
        if (text.IsEmpty) return false;
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var c = text[i];
            if (c == '%')
            {
                if (i + 2 >= text.Length) return false;
                var high = InterpretationSyntax.Hex(text[i + 1]);
                var low = InterpretationSyntax.Hex(text[i + 2]);
                if (high < 0 || low < 0) return false;
                c = (char)(high * 16 + low);
                i += 2;
            }
            else if (c is '#' or '^') return false;
            if (!InterpretationSyntax.Letter(c) && !InterpretationSyntax.Digit(c) && !"!#$&^_.+-".Contains(c, StringComparison.Ordinal)) return false;
            result.Append(c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c);
        }
        value = result.ToString();
        return true;
    }

    private static ImageInterpretation Unsupported(TemplateTarget target) => new(target, ImageInterpretationKind.UnsupportedOrUnrecognized);
}
