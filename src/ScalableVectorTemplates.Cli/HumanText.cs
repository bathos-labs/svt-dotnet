using System.Globalization;
using System.Text;

namespace ScalableVectorTemplates.Cli;

// Text presentation only: raw evidence and JSON serialization never pass through this formatter.
internal static class HumanText
{
    internal const int PreviewLength = 160;
    internal static string Escape(string text) => Render(text, false, false);
    internal static string Quote(string text) => Render(text, true, false);
    internal static string Preview(string text, bool quoted = true) => Render(text, quoted, true);

    private static string Render(string text, bool quoted, bool preview)
    {
        var output = new StringBuilder();
        if (quoted) output.Append('"');
        var count = 0;
        var shortenedEnd = output.Length;
        var shortened = false;
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            var pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            var width = pair ? 2 : 1;
            var escape = c switch
            {
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\\' => "\\\\",
                '"' when quoted => "\\\"",
                _ => null
            };
            if (escape is null && (NeedsEscape(c) || (char.IsSurrogate(c) && !pair)))
                escape = "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
            var cost = escape?.Length ?? 1; // A supplementary scalar occupies one displayed code point.
            if (preview && count + cost > PreviewLength)
            {
                output.Length = shortenedEnd;
                output.Append('…');
                shortened = true;
                break;
            }
            if (escape is not null) output.Append(escape);
            else output.Append(text.AsSpan(i, width));
            i += width;
            if (preview)
            {
                count += cost;
                if (count < PreviewLength) shortenedEnd = output.Length;
            }
        }
        if (quoted) output.Append('"');
        if (shortened) output.Append(" (shortened)");
        return output.ToString();
    }

    private static bool NeedsEscape(char c) => c is <= '\u001f' or >= '\u007f' and <= '\u009f'
        or '\u2028' or '\u2029' or '\u061c' or '\u200e' or '\u200f'
        or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u206f';
}
