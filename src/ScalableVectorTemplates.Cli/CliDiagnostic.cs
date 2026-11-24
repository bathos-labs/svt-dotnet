using System.Collections.Immutable;

namespace ScalableVectorTemplates.Cli;

internal sealed record JsonPosition(long Line, long ByteColumn);

internal sealed record CliDiagnostic(
    string Phase, string Code, string Severity, string Message,
    string? Key = null, SourceLocation? Location = null,
    ImmutableArray<SourceLocation> RelatedLocations = default, string? Limit = null,
    string? Source = null, string? RequestSource = null, string? JsonPointer = null,
    int? AssignmentIndex = null, JsonPosition? JsonPosition = null)
{
    internal static CliDiagnostic FromLibrary(Diagnostic d, string phase, string source,
        string? request = null, int? index = null) => new(phase, d.Code,
            d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.Message,
            d.Key, d.Location, d.RelatedLocations, d.Limit, source, request,
            index is { } i ? $"/bindings/{i}" : null, index);
}

// Expected boundary failures carry structured context; unexpected defects reach the top-level handler.
internal sealed class CliFailure(int exitCode, CliDiagnostic diagnostic) : Exception(diagnostic.Message)
{
    internal int ExitCode { get; } = exitCode;
    internal CliDiagnostic Diagnostic { get; } = diagnostic with
    { RelatedLocations = diagnostic.RelatedLocations.IsDefault ? [] : diagnostic.RelatedLocations };

    internal static CliFailure Limit(string phase, string source, string limit, int exitCode = 1) =>
        new(exitCode, new(phase, "svt.cli.limit", "error", "The input exceeds its configured limit.",
            Limit: limit, Source: source));
}
