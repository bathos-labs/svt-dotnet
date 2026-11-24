using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ScalableVectorTemplates.Cli;

internal sealed class JsonInput : IDisposable
{
    private readonly JsonDocument _document;
    private readonly bool _configuration;
    internal string Source { get; }
    internal JsonElement Root => _document.RootElement;

    internal JsonInput(byte[] bytes, string source, bool configuration, CancellationToken token)
    {
        Source = source;
        _configuration = configuration;
        var data = bytes.AsMemory();
        if (data.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) data = data[3..];
        try
        {
            token.ThrowIfCancellationRequested();
            _ = new UTF8Encoding(false, true).GetCharCount(data.Span);
            var reader = new Utf8JsonReader(data.Span, new JsonReaderOptions { MaxDepth = 17 });
            var objects = new Stack<HashSet<string>?>();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        if (reader.CurrentDepth >= 16)
                            throw CliFailure.Limit(configuration ? "configuration" : "input", source, "cli.max_json_depth", configuration ? 2 : 1);
                        objects.Push(reader.TokenType == JsonTokenType.StartObject ? new(StringComparer.Ordinal) : null);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray: objects.Pop(); break;
                    case JsonTokenType.String:
                    case JsonTokenType.PropertyName:
                        if (!ValidSurrogates(reader.ValueSpan))
                            throw Error("Unpaired Unicode surrogate escape.", position: Position(data.Span, reader.TokenStartIndex));
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            var name = reader.GetString() ?? throw new InvalidOperationException("A property token must contain a name.");
                            var properties = objects.Peek() ?? throw new InvalidOperationException("A property requires an object.");
                            if (!properties.Add(name)) throw Error("Duplicate JSON property.", position: Position(data.Span, reader.TokenStartIndex));
                        }
                        break;
                }
            }
            _document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException e)
        {
            throw Error("Malformed JSON.", position: e.LineNumber is { } line && e.BytePositionInLine is { } column
                ? new(line + 1, column + 1) : null);
        }
        catch (DecoderFallbackException) { throw Error("JSON must contain valid UTF-8."); }
    }

    internal CliFailure Error(string message, string? pointer = null, JsonPosition? position = null) =>
        new(_configuration ? 2 : 1, new(_configuration ? "configuration" : "input",
            _configuration ? "svt.cli.configuration" : "svt.cli.json", "error", message,
            Source: Source, RequestSource: Source, JsonPointer: pointer, AssignmentIndex: AssignmentIndex(pointer), JsonPosition: position));

    private static int? AssignmentIndex(string? pointer)
    {
        if (pointer is null) return null;
        var parts = pointer.Split('/');
        return parts.Length >= 3 && parts[1] == "bindings" &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
    }

    internal void Object(JsonElement element, string pointer, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Error("Expected an object.", pointer);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Error($"Unknown property: {property.Name}.", pointer + "/" + EscapePointer(property.Name));
    }
    internal JsonElement Required(JsonElement element, string name, string pointer = "") =>
        element.TryGetProperty(name, out var value) ? value : throw Error($"Add the required {name} property.", pointer + "/" + name);
    internal string String(JsonElement element, string name, string pointer = "")
    {
        var value = Required(element, name, pointer);
        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text : throw Error($"Use a string for {name}.", pointer + "/" + name);
    }
    internal void Version()
    {
        if (!Root.TryGetProperty("format_version", out var value)) return;
        if (value.ValueKind != JsonValueKind.Number || value.GetRawText() != "0")
            throw Error("Expected integer format_version 0.", "/format_version");
    }
    internal static string EscapePointer(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    public void Dispose() => _document.Dispose();

    private static JsonPosition Position(ReadOnlySpan<byte> data, long offset)
    {
        long line = 1, column = 1;
        foreach (var value in data[..checked((int)offset)])
        {
            if (value == (byte)'\n') { line++; column = 1; }
            else column++;
        }
        return new(line, column);
    }
    private static bool ValidSurrogates(ReadOnlySpan<byte> value)
    {
        // Utf8JsonReader already validates escape syntax. Inspect code units before GetString can replace them.
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != (byte)'\\') continue;
            if (value[++i] != (byte)'u') continue;
            var code = Hex(value.Slice(i + 1, 4));
            i += 4;
            if (code is >= 0xdc00 and <= 0xdfff) return false;
            if (code is < 0xd800 or > 0xdbff) continue;
            if (i + 6 >= value.Length || value[i + 1] != (byte)'\\' || value[i + 2] != (byte)'u') return false;
            var low = Hex(value.Slice(i + 3, 4));
            if (low is < 0xdc00 or > 0xdfff) return false;
            i += 6;
        }
        return true;
    }
    private static int Hex(ReadOnlySpan<byte> digits)
    {
        var result = 0;
        foreach (var digit in digits)
            result = result * 16 + (digit <= '9' ? digit - '0' : (digit | 32) - 'a' + 10);
        return result;
    }
}
