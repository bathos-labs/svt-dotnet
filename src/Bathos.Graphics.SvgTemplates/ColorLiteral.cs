using System.Globalization;

namespace Bathos.Graphics.SvgTemplates;

internal static class ColorLiteral
{
    private enum Unit { Number, Percent, Degrees, Grads, Radians, Turns }

    internal static bool TryRead(ReadOnlySpan<char> text, CancellationToken token, out SrgbColor? color)
    {
        token.ThrowIfCancellationRequested();
        color = null;
        if (text.IsEmpty) return false;
        if (text[0] == '#') return Hex(text[1..], out color);
        if (InterpretationSyntax.Equal(text, "transparent")) { color = new(0, 0, 0, 0); return true; }
        var open = text.IndexOf('(');
        if (open < 0) return NamedColors.TryGet(text, out color);
        var name = text[..open];
        var hsl = InterpretationSyntax.Equal(name, "hsl") || InterpretationSyntax.Equal(name, "hsla");
        if ((!hsl && !InterpretationSyntax.Equal(name, "rgb") && !InterpretationSyntax.Equal(name, "rgba")) || text[^1] != ')') return false;
        var body = text[(open + 1)..^1];
        Span<double> values = stackalloc double[4];
        Span<Unit> units = stackalloc Unit[4];
        var position = 0;
        InterpretationSyntax.SkipSpace(body, ref position, token);
        if (!Component(body, ref position, token, out values[0], out units[0])) return false;
        var beforeSpace = position;
        InterpretationSyntax.SkipSpace(body, ref position, token);
        var comma = position < body.Length && body[position] == ',';
        for (var i = 1; i < 3; i++)
        {
            if (comma)
            {
                if (position == body.Length || body[position++] != ',') return false;
                InterpretationSyntax.SkipSpace(body, ref position, token);
            }
            else if (position == beforeSpace) return false;
            if (!Component(body, ref position, token, out values[i], out units[i])) return false;
            beforeSpace = position;
            InterpretationSyntax.SkipSpace(body, ref position, token);
        }
        values[3] = 1;
        units[3] = Unit.Number;
        if (position < body.Length)
        {
            if (body[position++] != (comma ? ',' : '/')) return false;
            InterpretationSyntax.SkipSpace(body, ref position, token);
            if (!Component(body, ref position, token, out values[3], out units[3])) return false;
            InterpretationSyntax.SkipSpace(body, ref position, token);
        }
        if (position != body.Length || units[3] is not (Unit.Number or Unit.Percent)) return false;
        var alpha = Clamp(values[3], units[3] == Unit.Percent ? 100 : 1);
        if (!hsl)
        {
            for (var i = 0; i < 3; i++) if (units[i] is not (Unit.Number or Unit.Percent)) return false;
            if (comma && (units[0] != units[1] || units[1] != units[2])) return false;
            color = new(Clamp(values[0], units[0] == Unit.Percent ? 100 : 255),
                Clamp(values[1], units[1] == Unit.Percent ? 100 : 255), Clamp(values[2], units[2] == Unit.Percent ? 100 : 255), alpha);
            return true;
        }
        if (units[0] == Unit.Percent || units[1] != Unit.Percent || units[2] != Unit.Percent) return false;
        var period = units[0] switch { Unit.Grads => 400, Unit.Radians => 2 * Math.PI, Unit.Turns => 1, _ => 360 };
        var hue = values[0] % period;
        if (hue < 0) hue += period;
        hue = hue / period * 6;
        var saturation = Clamp(values[1], 100);
        var lightness = Clamp(values[2], 100);
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue % 2 - 1));
        var m = lightness - chroma / 2;
        var (red, green, blue) = hue switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        color = new(red + m, green + m, blue + m, alpha);
        return true;
    }

    private static double Clamp(double value, double maximum) => Math.Clamp(value, 0, maximum) / maximum;

    private static bool Component(ReadOnlySpan<char> text, ref int position, CancellationToken token, out double value, out Unit unit)
    {
        value = 0;
        unit = Unit.Number;
        if (!InterpretationSyntax.NumberStart(text, position)) return false;
        var start = position;
        position = InterpretationSyntax.NumberEnd(text, position, token);
        if (!double.TryParse(text[start..position], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value)) return false;
        if (position < text.Length && text[position] == '%') { position++; unit = Unit.Percent; return true; }
        start = position;
        while (position < text.Length && InterpretationSyntax.Letter(text[position])) { token.ThrowIfCancellationRequested(); position++; }
        var suffix = text[start..position];
        if (suffix.IsEmpty) return true;
        if (InterpretationSyntax.Equal(suffix, "deg")) unit = Unit.Degrees;
        else if (InterpretationSyntax.Equal(suffix, "grad")) unit = Unit.Grads;
        else if (InterpretationSyntax.Equal(suffix, "rad")) unit = Unit.Radians;
        else if (InterpretationSyntax.Equal(suffix, "turn")) unit = Unit.Turns;
        else return false;
        return true;
    }

    private static bool Hex(ReadOnlySpan<char> text, out SrgbColor? color)
    {
        color = null;
        if (text.Length is not (3 or 4 or 6 or 8)) return false;
        Span<int> components = stackalloc int[4];
        components[3] = 255;
        var shortForm = text.Length < 5;
        for (var i = 0; i < (shortForm ? text.Length : text.Length / 2); i++)
        {
            var high = InterpretationSyntax.Hex(text[shortForm ? i : i * 2]);
            var low = shortForm ? high : InterpretationSyntax.Hex(text[i * 2 + 1]);
            if (high < 0 || low < 0) return false;
            components[i] = high * 16 + low;
        }
        color = new(components[0] / 255d, components[1] / 255d, components[2] / 255d, components[3] / 255d);
        return true;
    }
}
