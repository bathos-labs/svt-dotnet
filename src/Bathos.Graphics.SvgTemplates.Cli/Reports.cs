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
    internal static string Quote(string? text) => JsonSerializer.Serialize(text, Options);
    internal static Task InspectAsync(Stream stream, InspectionResult result, bool json, CancellationToken token)
    {
        if (json) return WriteJsonAsync(stream, new
        {
            FormatVersion = 0,
            TemplateAvailable = result.Template is not null,
            Inputs = result.Template?.Inputs ?? []
        }, token);
        var report = new StringBuilder(result.Template is null ? "No usable template.\n" : "Template inputs:\n");
        foreach (var input in result.Template?.Inputs ?? [])
        {
            report.Append(Quote(input.Key)).Append("  ").Append(input.Kind).Append("  label=").Append(Quote(input.Label))
                .Append("  default=").Append(input.Default.State).Append(' ').Append(Quote(input.Default.Raw)).Append('\n');
            foreach (var target in input.Targets)
                report.Append("  ").Append(target.Hook).Append(" <").Append(target.ElementName).Append("> at ")
                    .Append(target.Location.Line).Append(':').Append(target.Location.Column).Append("  ")
                    .Append(target.Default.Source).Append(' ').Append(target.Default.State).Append(' ')
                    .Append(Quote(target.Default.Raw)).Append('\n');
        }
        return WriteTextAsync(stream, report.ToString(), token);
    }
    internal static Task DiagnosticsAsync(Stream stream, string? command, int exitCode,
        List<CliDiagnostic> diagnostics, bool json)
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
            text.Append(d.Severity).Append(' ').Append(d.Code).Append(" [").Append(d.Phase).Append("] ")
                .Append(Quote(d.Source)).Append(' ');
            if (d.Location is { } location) text.Append(location.Line).Append(':').Append(location.Column).Append(' ');
            text.Append(Quote(d.Message));
            if (d.Key is not null) text.Append(" key=").Append(Quote(d.Key));
            if (d.Limit is not null) text.Append(" limit=").Append(Quote(d.Limit));
            text.Append('\n');
        }
        return WriteTextAsync(stream, text.ToString(), CancellationToken.None);
    }
}
