namespace Bathos.Graphics.SvgTemplates;

internal static class InterpretationSyntax
{
    internal static bool Space(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f';
    internal static bool Letter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    internal static bool Digit(char c) => c is >= '0' and <= '9';
    internal static bool IdentifierStart(char c) => Letter(c) || c is '-' or '_';
    internal static bool IdentifierPart(char c) => IdentifierStart(c) || Digit(c);
    internal static bool Equal(ReadOnlySpan<char> text, string expected) => text.Equals(expected, StringComparison.OrdinalIgnoreCase);
    internal static int Hex(char c) => c is >= '0' and <= '9' ? c - '0' :
        c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;

    internal static void SkipSpace(ReadOnlySpan<char> text, ref int position, CancellationToken token)
    {
        while (position < text.Length && Space(text[position])) { token.ThrowIfCancellationRequested(); position++; }
    }

    internal static ReadOnlySpan<char> Trim(ReadOnlySpan<char> text, CancellationToken token)
    {
        var start = 0;
        SkipSpace(text, ref start, token);
        var end = text.Length;
        while (end > start && Space(text[end - 1])) { token.ThrowIfCancellationRequested(); end--; }
        return text[start..end];
    }

    internal static bool NumberStart(ReadOnlySpan<char> text, int position)
    {
        if (position < text.Length && text[position] is '+' or '-') position++;
        return position < text.Length && (Digit(text[position]) ||
            (text[position] == '.' && position + 1 < text.Length && Digit(text[position + 1])));
    }

    // The shared token grammar is used by both the budget scan and literal conversion.
    internal static int NumberEnd(ReadOnlySpan<char> text, int position, CancellationToken token, int maxLength = int.MaxValue)
    {
        var start = position;
        if (text[position] is '+' or '-') position++;
        while (position < text.Length && Digit(text[position]))
        {
            token.ThrowIfCancellationRequested();
            if (++position - start > maxLength) return position;
        }
        if (position + 1 < text.Length && text[position] == '.' && Digit(text[position + 1]))
        {
            position++;
            while (position < text.Length && Digit(text[position]))
            {
                token.ThrowIfCancellationRequested();
                if (++position - start > maxLength) return position;
            }
        }
        if (position < text.Length && text[position] is 'e' or 'E')
        {
            var exponent = position + 1;
            if (exponent < text.Length && text[exponent] is '+' or '-') exponent++;
            if (exponent < text.Length && Digit(text[exponent]))
            {
                position = exponent;
                while (position < text.Length && Digit(text[position]))
                {
                    token.ThrowIfCancellationRequested();
                    if (++position - start > maxLength) return position;
                }
            }
        }
        return position;
    }

    internal static bool ReferenceCharacters(ReadOnlySpan<char> text, CancellationToken token)
    {
        for (var i = 0; i < text.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var c = text[i];
            if (Letter(c) || Digit(c) || "-._~:/?#[]@!$&'()*+,;=".Contains(c, StringComparison.Ordinal)) continue;
            if (c != '%' || i + 2 >= text.Length || Hex(text[i + 1]) < 0 || Hex(text[i + 2]) < 0) return false;
            i += 2;
        }
        return true;
    }
}
