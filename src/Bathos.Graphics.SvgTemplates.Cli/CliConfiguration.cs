using System.Text.Json;

namespace Bathos.Graphics.SvgTemplates.Cli;

internal sealed record CliConfiguration(InspectionOptions Inspection, BindingConfiguration Binding,
    bool AllowScoped = false, int MaxBindingsFileBytes = 67108864)
{
    internal static CliConfiguration Default { get; } = new(new(), BindingConfiguration.Default);

    internal static CliConfiguration Parse(JsonInput input)
    {
        input.Object(input.Root, "", "format_version", "inspection", "binding", "image", "cli");
        input.Version();
        JsonElement Group(string name, params string[] allowed)
        {
            if (!input.Root.TryGetProperty(name, out var group)) return default;
            input.Object(group, "/" + name, allowed);
            return group;
        }
        int Number(JsonElement group, string groupName, string name, int fallback)
        {
            if (group.ValueKind == JsonValueKind.Undefined || !group.TryGetProperty(name, out var value)) return fallback;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number <= 0)
                throw input.Error("Expected a positive 32-bit integer.", "/" + groupName + "/" + name);
            return number;
        }
        string Choice(JsonElement group, string groupName, string name, string first, string second)
        {
            if (group.ValueKind == JsonValueKind.Undefined || !group.TryGetProperty(name, out _)) return first;
            var value = input.String(group, name, "/" + groupName);
            return value == first || value == second ? value : throw input.Error("Unknown policy value.", "/" + groupName + "/" + name);
        }
        var inspection = Group("inspection", "max_source_bytes", "max_xml_characters", "max_element_depth", "max_elements",
            "max_attributes_per_element", "max_hooks", "max_diagnostics");
        var binding = Group("binding", "unknown_key_behavior", "max_bindings", "max_value_characters",
            "max_total_value_characters", "max_total_encoded_image_bytes", "max_output_bytes", "max_diagnostics");
        var image = Group("image", "max_encoded_image_bytes", "max_media_type_characters");
        var cli = Group("cli", "inspection_errors", "max_bindings_file_bytes");
        var i = Default.Inspection;
        var b = Default.Binding.BindingOptions;
        var e = Default.Binding.ImageOptions;
        return new(new()
        {
            MaxSourceBytes = Number(inspection, "inspection", "max_source_bytes", i.MaxSourceBytes),
            MaxXmlCharacters = Number(inspection, "inspection", "max_xml_characters", i.MaxXmlCharacters),
            MaxElementDepth = Number(inspection, "inspection", "max_element_depth", i.MaxElementDepth),
            MaxElements = Number(inspection, "inspection", "max_elements", i.MaxElements),
            MaxAttributesPerElement = Number(inspection, "inspection", "max_attributes_per_element", i.MaxAttributesPerElement),
            MaxHooks = Number(inspection, "inspection", "max_hooks", i.MaxHooks),
            MaxDiagnostics = Number(inspection, "inspection", "max_diagnostics", i.MaxDiagnostics),
        }, new(new()
        {
            UnknownKeyBehavior = Choice(binding, "binding", "unknown_key_behavior", "error", "ignore_with_warning") == "error" ? UnknownKeyBehavior.Error : UnknownKeyBehavior.IgnoreWithWarning,
            MaxBindings = Number(binding, "binding", "max_bindings", b.MaxBindings),
            MaxValueCharacters = Number(binding, "binding", "max_value_characters", b.MaxValueCharacters),
            MaxTotalValueCharacters = Number(binding, "binding", "max_total_value_characters", b.MaxTotalValueCharacters),
            MaxTotalEncodedImageBytes = Number(binding, "binding", "max_total_encoded_image_bytes", b.MaxTotalEncodedImageBytes),
            MaxOutputBytes = Number(binding, "binding", "max_output_bytes", b.MaxOutputBytes),
            MaxDiagnostics = Number(binding, "binding", "max_diagnostics", b.MaxDiagnostics),
        }, new()
        {
            MaxEncodedImageBytes = Number(image, "image", "max_encoded_image_bytes", e.MaxEncodedImageBytes),
            MaxMediaTypeCharacters = Number(image, "image", "max_media_type_characters", e.MaxMediaTypeCharacters),
        }), Choice(cli, "cli", "inspection_errors", "fail", "allow_scoped") == "allow_scoped",
            Number(cli, "cli", "max_bindings_file_bytes", Default.MaxBindingsFileBytes));
    }

    internal CliConfiguration Apply(Invocation invocation)
    {
        var b = Binding.BindingOptions;
        var policy = invocation.UnknownKeys switch
        {
            "error" => UnknownKeyBehavior.Error,
            "ignore-with-warning" => UnknownKeyBehavior.IgnoreWithWarning,
            _ => b.UnknownKeyBehavior,
        };
        return this with
        {
            AllowScoped = invocation.InspectionErrors switch { "fail" => false, "allow-scoped" => true, _ => AllowScoped },
            Binding = new(new()
            {
                UnknownKeyBehavior = policy,
                MaxBindings = b.MaxBindings,
                MaxValueCharacters = b.MaxValueCharacters,
                MaxTotalValueCharacters = b.MaxTotalValueCharacters,
                MaxTotalEncodedImageBytes = b.MaxTotalEncodedImageBytes,
                MaxOutputBytes = b.MaxOutputBytes,
                MaxDiagnostics = b.MaxDiagnostics,
            }, Binding.ImageOptions),
        };
    }
}
