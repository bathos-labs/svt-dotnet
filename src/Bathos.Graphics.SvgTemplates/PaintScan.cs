namespace Bathos.Graphics.SvgTemplates;

// A single iterative preflight pass owns lexical rejection and work-budget precedence.
internal static class PaintScan
{
    internal readonly record struct Function(int Start, int NameLength, int End, bool HasVariable);
    private readonly record struct Frame(int Start, int NameLength, int VariablesBefore);

    internal static bool Read(ReadOnlySpan<char> text, InterpretationOptions limits, CancellationToken token,
        out List<Function> functions, out InterpretationLimit? exceeded)
    {
        functions = [];
        exceeded = null;
        var stack = new List<Frame>();
        var variables = 0;
        var i = 0;
        while (i < text.Length)
        {
            token.ThrowIfCancellationRequested();
            var c = text[i];
            if (InterpretationSyntax.Space(c)) { i++; continue; }
            if (c < ' ' || c > '~' || c is '\\' or '{' or '}' or '[' or ']' or ';' ||
                (c == '/' && i + 1 < text.Length && text[i + 1] == '*')) return false;
            if (c is '\'' or '"')
            {
                if (!Quoted(text, ref i, token, out _, out _)) return false;
                continue;
            }
            if (c == '#')
            {
                i++;
                while (i < text.Length && InterpretationSyntax.IdentifierPart(text[i])) { token.ThrowIfCancellationRequested(); i++; }
                continue;
            }
            if (InterpretationSyntax.NumberStart(text, i))
            {
                var end = InterpretationSyntax.NumberEnd(text, i, token, limits.MaxNumericTokenCharacters);
                if (end - i > limits.MaxNumericTokenCharacters) { exceeded = InterpretationLimit.NumericTokenCharacters; return false; }
                i = end;
                if (i < text.Length && text[i] == '%') i++;
                else while (i < text.Length && InterpretationSyntax.IdentifierPart(text[i])) { token.ThrowIfCancellationRequested(); i++; }
                continue;
            }
            if (InterpretationSyntax.IdentifierStart(c))
            {
                var start = i++;
                while (i < text.Length && InterpretationSyntax.IdentifierPart(text[i])) { token.ThrowIfCancellationRequested(); i++; }
                if (i == text.Length || text[i] != '(') continue;
                var nameLength = i - start;
                if (stack.Count >= limits.MaxNestingDepth) { exceeded = InterpretationLimit.NestingDepth; return false; }
                i++;
                var name = text.Slice(start, nameLength);
                if (InterpretationSyntax.Equal(name, "url"))
                {
                    if (!Url(text, ref i, token)) return false;
                    if (stack.Count == 0) functions.Add(new(start, nameLength, i, false));
                    continue;
                }
                stack.Add(new(start, nameLength, variables));
                if (InterpretationSyntax.Equal(name, "var"))
                {
                    InterpretationSyntax.SkipSpace(text, ref i, token);
                    var keyStart = i;
                    while (i < text.Length && InterpretationSyntax.IdentifierPart(text[i])) { token.ThrowIfCancellationRequested(); i++; }
                    if (i - keyStart < 3 || text[keyStart] != '-' || text[keyStart + 1] != '-') return false;
                    InterpretationSyntax.SkipSpace(text, ref i, token);
                    if (i == text.Length || text[i] is not (',' or ')')) return false;
                    variables++;
                }
                continue;
            }
            if (c == '(') return false;
            if (c == ')')
            {
                if (stack.Count == 0) return false;
                var frame = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0) functions.Add(new(frame.Start, frame.NameLength, i + 1, variables > frame.VariablesBefore));
            }
            i++;
        }
        return stack.Count == 0;
    }

    private static bool Quoted(ReadOnlySpan<char> text, ref int position, CancellationToken token, out int start, out int length)
    {
        var quote = text[position++];
        start = position;
        length = 0;
        while (position < text.Length)
        {
            token.ThrowIfCancellationRequested();
            var c = text[position++];
            if (c == quote) { length = position - start - 1; return true; }
            if (c is '\\' or '\r' or '\n' or '\f' || c > '~' || (c < ' ' && c != '\t')) return false;
        }
        return false;
    }

    private static bool Url(ReadOnlySpan<char> text, ref int position, CancellationToken token)
    {
        InterpretationSyntax.SkipSpace(text, ref position, token);
        if (position == text.Length) return false;
        int start, length;
        if (text[position] is '\'' or '"')
        {
            if (!Quoted(text, ref position, token, out start, out length)) return false;
        }
        else
        {
            start = position;
            while (position < text.Length && text[position] != ')' && !InterpretationSyntax.Space(text[position]))
            {
                token.ThrowIfCancellationRequested();
                if (text[position] is '(' or '\'' or '"') return false;
                position++;
            }
            length = position - start;
        }
        if (length == 0 || !InterpretationSyntax.ReferenceCharacters(text.Slice(start, length), token)) return false;
        InterpretationSyntax.SkipSpace(text, ref position, token);
        return position < text.Length && text[position++] == ')';
    }
}
