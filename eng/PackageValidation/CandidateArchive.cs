using System.IO.Compression;

namespace ScalableVectorTemplates.PackageValidation;

internal static class CandidateArchive
{
    private const long MaximumSize = 1024L * 1024 * 1024;
    internal static void Create(string directory, string destination)
    {
        using var stream = new FileStream(destination, FileMode.CreateNew);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        long total = 0;
        foreach (var path in Enumerate(directory).Order(StringComparer.Ordinal))
        {
            var length = new FileInfo(path).Length;
            total = checked(total + length);
            if (total > MaximumSize) throw new InvalidDataException("Candidate archive exceeds 1 GiB.");
            var name = Path.GetRelativePath(directory, path).Replace('\\', '/');
            ValidateName(name);
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var input = File.OpenRead(path);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static IEnumerable<string> Enumerate(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Candidate cannot contain links.");
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                foreach (var child in Enumerate(path)) yield return child;
            }
            else yield return path;
        }
    }

    internal static void Extract(string archive, string destination)
    {
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Extraction destination already exists.");
        using var zip = ZipFile.OpenRead(archive);
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            ValidateName(entry.FullName);
            if (!names.Add(entry.FullName)) throw new InvalidDataException("Duplicate candidate entry.");
            total = checked(total + entry.Length);
            if (total > MaximumSize || zip.Entries.Count > 20000) throw new InvalidDataException("Candidate archive exceeds limits.");
        }
        Directory.CreateDirectory(destination);
        foreach (var entry in zip.Entries)
        {
            var path = Path.Combine(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidDataException("Missing entry directory."));
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew);
            // Zip length is not trusted as an allocation or decompression bound.
            var buffer = new byte[81920];
            long written = 0;
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                written += count;
                if (written > entry.Length) throw new InvalidDataException("Candidate entry size changed.");
                output.Write(buffer, 0, count);
            }
            if (written != entry.Length) throw new InvalidDataException("Candidate entry is truncated.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':') ||
            name.Split('/').Any(part => string.IsNullOrEmpty(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidDataException("Unsafe candidate archive path.");
    }
}
