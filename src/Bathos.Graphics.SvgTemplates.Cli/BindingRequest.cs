using System.Text.Json;

namespace Bathos.Graphics.SvgTemplates.Cli;

internal sealed record BindingRequest(string Key, string Kind, string? Value, string? File, string? MediaType)
{
    internal static BindingRequest[] Parse(JsonInput input, int maximum, CancellationToken token)
    {
        input.Object(input.Root, "", "format_version", "bindings");
        input.Version();
        if (!input.Root.TryGetProperty("bindings", out var bindings)) return [];
        if (bindings.ValueKind != JsonValueKind.Array) throw input.Error("Expected a bindings array.", "/bindings");
        if (bindings.GetArrayLength() > maximum) throw CliFailure.Limit("input", input.Source, "MaxBindings");
        var result = new BindingRequest[bindings.GetArrayLength()];
        for (var i = 0; i < result.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = bindings[i];
            var pointer = $"/bindings/{i}";
            input.Object(entry, pointer, "key", "kind", "value", "file", "media_type");
            var key = input.String(entry, "key", pointer);
            var kind = input.String(entry, "kind", pointer);
            if (kind == "image_content")
            {
                var media = input.String(entry, "media_type", pointer);
                if (entry.TryGetProperty("file", out _) == entry.TryGetProperty("value", out _))
                    throw input.Error("Image content requires exactly one of file or value.", pointer);
                var file = entry.TryGetProperty("file", out _) ? input.String(entry, "file", pointer) : null;
                if (file is { Length: 0 }) throw input.Error("An image file path must not be empty.", pointer + "/file");
                result[i] = new(key, kind, file is null ? input.String(entry, "value", pointer) : null, file, media);
            }
            else if (kind is "text" or "paint" or "image_reference")
            {
                input.Object(entry, pointer, "key", "kind", "value");
                result[i] = new(key, kind, input.String(entry, "value", pointer), null, null);
            }
            else throw input.Error($"Use text, paint, image_reference or image_content for kind; received \"{kind}\".", pointer + "/kind");
        }
        return result;
    }
}
