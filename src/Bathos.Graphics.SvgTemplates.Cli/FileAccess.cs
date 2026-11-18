using System.Text;

namespace Bathos.Graphics.SvgTemplates.Cli;

internal sealed class FileAccess(string workingDirectory, Stream standardInput)
{
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
    internal string WorkingDirectory { get; } = Path.GetFullPath(workingDirectory);
    internal IReadOnlySet<string> Consumed => _consumed;
    internal string FullPath(string path)
    {
        // Preserve a directory-qualified operand's intent before GetFullPath removes its final segment.
        if (Path.EndsInDirectorySeparator(path) || Path.GetFileName(path) is "." or "..")
            throw new IOException("Expected a file path, not a directory-qualified path.");
        return Path.GetFullPath(path, WorkingDirectory);
    }

    internal async Task<byte[]> ReadAsync(string path, int maximum, string limit, string phase,
        CancellationToken token, int exitCode = 1)
    {
        if (path == "-") return await ReadBoundedAsync(standardInput, maximum, limit, phase, path, token, exitCode).ConfigureAwait(false);
        var resolved = Resolve(FullPath(path), token);
        _consumed.Add(EntryIdentity(resolved));
        await using var stream = new FileStream(resolved, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadBoundedAsync(stream, maximum, limit, phase, path, token, exitCode).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, string limit, string phase,
        string source, CancellationToken token, int exitCode = 1)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, (long)maximum - buffer.Length + 1)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > maximum) throw CliFailure.Limit(phase, source, limit, exitCode);
            if (buffer.Capacity < buffer.Length + count)
                buffer.Capacity = (int)Math.Min(maximum, Math.Max(buffer.Length + count, (long)buffer.Capacity * 2));
            buffer.Write(chunk, 0, count);
        }
    }

    // Canonicalize spelling by querying the directory, not by guessing case sensitivity from the OS.
    // Different hard-linked names intentionally remain different entries.
    internal static string Resolve(string path, CancellationToken token, bool followFinal = true, int links = 0)
    {
        token.ThrowIfCancellationRequested();
        if (links > 64) throw new IOException("Too many symbolic links.");
        if ((Path.EndsInDirectorySeparator(path) || Path.GetFileName(path) is "." or "..") && !Directory.Exists(path))
            throw new IOException("A directory-qualified link target must name a directory.");
        path = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(path);
        if (parent is null)
        {
            // Drive and UNC root identifiers are case-insensitive even when a child directory is not.
            return OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        }
        var resolvedParent = Resolve(parent, token, links: links);
        var name = Path.GetFileName(path);
        var candidate = Path.Combine(resolvedParent, name);
        var info = new FileInfo(candidate);
        var exists = File.Exists(candidate) || Directory.Exists(candidate) || info.LinkTarget is not null;
        if (exists)
        {
            var folded = new List<string>();
            var exact = false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(resolvedParent))
            {
                token.ThrowIfCancellationRequested();
                var entryName = Path.GetFileName(entry);
                if (entryName == name) { candidate = entry; exact = true; break; }
                if (string.Equals(entryName.Normalize(NormalizationForm.FormC), name.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase))
                    folded.Add(entry);
            }
            if (!exact)
            {
                if (folded.Count != 1) throw new IOException("Cannot resolve filesystem path spelling unambiguously.");
                candidate = folded[0];
            }
        }
        var target = new FileInfo(candidate).LinkTarget;
        if (followFinal && target is not null)
        {
            var resolvedTarget = Path.GetFullPath(target, resolvedParent);
            if ((Path.EndsInDirectorySeparator(target) || Path.GetFileName(target) is "." or "..") && !Directory.Exists(resolvedTarget))
                throw new IOException("A directory-qualified link target must name a directory.");
            return Resolve(resolvedTarget, token, links: links + 1);
        }
        return candidate;
    }

    // Normalize equivalent Windows namespace spellings for collision comparison only. Keep the
    // original resolved spelling for I/O: extended paths have different parsing rules.
    internal static string EntryIdentity(string resolved)
    {
        if (!OperatingSystem.IsWindows()) return resolved;
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
            resolved.StartsWith(@"\\.\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + resolved[8..];
        if ((resolved.StartsWith(@"\\?\", StringComparison.Ordinal) || resolved.StartsWith(@"\\.\", StringComparison.Ordinal)) &&
            resolved.Length >= 7 && char.IsAsciiLetter(resolved[4]) && resolved[5] == ':' && resolved[6] == '\\')
            return resolved[4..];
        return resolved;
    }

    internal static bool IsIoFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or NotSupportedException or ArgumentException or ObjectDisposedException;
}
