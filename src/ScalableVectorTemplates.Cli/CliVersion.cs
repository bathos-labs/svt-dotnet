using System.Reflection;

namespace ScalableVectorTemplates.Cli;

internal static class CliVersion
{
    internal static string Display()
    {
        var metadata = typeof(CliVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        return Format(metadata.SingleOrDefault(a => a.Key == "SvtProductVersion")?.Value,
            metadata.SingleOrDefault(a => a.Key == "SvtSourceRevision")?.Value);
    }

    internal static string Format(string? version, string? revision)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("The CLI build has no product version.");
        var commit = revision is { Length: 40 or 64 } && revision.All(char.IsAsciiHexDigit)
            ? " (commit " + revision[..12].ToLowerInvariant() + ")" : "";
        return "svt " + HumanText.Escape(version) + commit + "\n";
    }
}
