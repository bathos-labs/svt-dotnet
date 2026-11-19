using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bathos.Graphics.SvgTemplates.Cli;

internal static class Reports
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    internal static async Task WriteJsonAsync(Stream stream, object value, CancellationToken token)
    {
        await JsonSerializer.SerializeAsync(stream, value, Options, token).ConfigureAwait(false);
        await stream.WriteAsync(new byte[] { (byte)'\n' }, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
    internal static async Task WriteTextAsync(Stream stream, string text, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
    internal static Task InspectAsync(Stream stream, InspectionResult result, bool json, CancellationToken token)
    {
        if (json) return WriteJsonAsync(stream, new
        {
            FormatVersion = 0,
            TemplateAvailable = result.Template is not null,
            Inputs = result.Template?.Inputs ?? [],
            InputGroups = result.Template?.InputGroups.Select(group => new
            {
                group.Key,
                group.Label,
                group.Description,
                group.Order,
                InputKeys = group.Inputs.Select(input => input.Key),
            }).ToArray() ?? []
        }, token);
        var report = new StringBuilder();
        if (result.Template is null) report.Append("No usable template.\n");
        else if (result.Template.Inputs.IsEmpty) report.Append("No editable inputs found.\n");
        foreach (var input in result.Template?.Inputs ?? [])
        {
            report.Append(HumanText.Preview(input.Label, false)).Append(" [").Append(HumanText.Escape(input.Key))
                .Append("] — ").Append(input.Kind.ToString().ToLowerInvariant()).Append('\n');
            if (input.Description is { } description)
                report.Append("  ").Append(HumanText.Preview(description, false)).Append('\n');
            report.Append(input.Default.State == InputDefaultState.Mixed
                ? "  Defaults differ" : "  Default: " + DefaultText(input.Kind, input.Default.Raw)).Append('\n');
            foreach (var target in input.Targets)
            {
                report.Append("  Target: ").Append(target.Hook.ToString().ToLowerInvariant())
                    .Append(" <").Append(HumanText.Escape(target.ElementName)).Append(">, ")
                    .Append(SvgLocation(target.Location)).Append('\n');
                if (input.Default.State == InputDefaultState.Mixed)
                    report.Append("    Default: ").Append(DefaultText(input.Kind, target.Default.Raw)).Append('\n');
            }
        }
        return WriteTextAsync(stream, report.ToString(), token);
    }

    private static string DefaultText(InputKind kind, string? raw) => raw is null ? "No authored default"
        : kind == InputKind.Image && raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? "Embedded data (payload hidden)" : HumanText.Preview(raw);

    private static string SvgLocation(SourceLocation location) =>
        FormattableString.Invariant($"SVG line {location.Line}, UTF-16 column {location.Column}");

    internal static Task DiagnosticsAsync(Stream stream, string? command, int exitCode,
        List<CliDiagnostic> diagnostics, bool json, bool showUsage = false)
    {
        // Reporting happens after publication. Caller cancellation must not turn a committed file into a rollback.
        if (json) return WriteJsonAsync(stream, new
        {
            FormatVersion = 0,
            Command = command,
            Outcome = exitCode == 0 ? "succeeded" : exitCode == 130 ? "cancelled" : "failed",
            ExitCode = exitCode,
            Diagnostics = diagnostics
        }, CancellationToken.None);
        if (diagnostics.Count == 0) return Task.CompletedTask;
        var text = new StringBuilder();
        foreach (var d in diagnostics)
        {
            text.Append(d.Severity == "warning" ? "Warning: " : "Error: ").Append(HumanText.Escape(d.Message)).Append('\n');
            if (d.Source is { } source) Detail("Source", source == "-" ? d.Phase == "publication" ? "stdout" : "stdin" : HumanText.Quote(source));
            if (d.Location is { } location) text.Append("  ").Append(SvgLocation(location)).Append('\n');
            if (!d.RelatedLocations.IsDefaultOrEmpty)
                foreach (var related in d.RelatedLocations) Detail("Related", SvgLocation(related));
            if (d.RequestSource is { } request && (request != d.Source || request == "-" && d.Phase == "publication"))
                Detail("Request", request == "-" ? "stdin" : HumanText.Quote(request));
            if (d.JsonPosition is { } position)
                text.Append(FormattableString.Invariant($"  JSON line {position.Line}, UTF-8 byte column {position.ByteColumn}\n"));
            if (d.AssignmentIndex is { } index) text.Append(FormattableString.Invariant($"  binding {(long)index + 1}\n"));
            if (d.JsonPointer is { } pointer) Detail("JSON pointer", HumanText.Quote(pointer));
            if (d.Key is { } key) Detail("Key", HumanText.Quote(key));
            if (d.Limit is { } limit) Detail("Limit", HumanText.Escape(limit));
            Detail("Code", HumanText.Escape(d.Code));
        }
        if (showUsage) text.Append('\n').Append(CliHelp.Overview).Append('\n');
        else if (diagnostics.Any(d => d.Code == "svt.cli.usage"))
            text.Append("Use svt ").Append(command is "inspect" or "bind" ? command + " " : "").Append("--help for help.\n");
        return WriteTextAsync(stream, text.ToString(), CancellationToken.None);

        void Detail(string label, string value) => text.Append("  ").Append(label).Append(": ").Append(value).Append('\n');
    }
}
