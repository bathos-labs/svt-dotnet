using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Bathos.Graphics.SvgTemplates.PackageValidation;

internal sealed record InspectionReport(string PackageId, string Version, string Commit,
    string PackageSha256, string SymbolsSha256, string AssemblySha256, string[] PackageFiles,
    string[] SymbolFiles, string[] AssemblyReferences, string[] SourceDocuments);

internal static class PackageInspector
{
    internal const string PackageId = "Bathos.Graphics.SvgTemplates";
    internal const string Repository = "https://github.com/bathos-labs/svt-dotnet";
    private static readonly Guid SourceLinkKind = new("cc110556-a091-4d38-9fec-25ab9a351a6a");
    private static readonly Guid EmbeddedSourceKind = new("0e8a571b-6926-466e-b4ad-8ab04611f5fe");

    internal static InspectionReport Inspect(string package, string symbols, string version, string commit)
    {
        var files = ReadArchive(package);
        var symbolFiles = ReadArchive(symbols);
        ValidateContents(files, false);
        ValidateContents(symbolFiles, true);
        ValidateMetadata(files[PackageId + ".nuspec"], version, commit, false);
        ValidateMetadata(symbolFiles[PackageId + ".nuspec"], version, commit, true);
        foreach (var name in new[] { "README.md", "LICENSE", "NOTICE", "lib/net10.0/" + PackageId + ".xml" })
            Require(files[name].Length > 0, "Empty required file: " + name);
        Require(Encoding.UTF8.GetString(files["LICENSE"]).Contains("MIT License", StringComparison.Ordinal), "Missing MIT license text.");
        var xml = ReadXml(files["lib/net10.0/" + PackageId + ".xml"]);
        Require(xml.Root?.Element("assembly")?.Element("name")?.Value == PackageId, "Incorrect XML documentation assembly.");
        Require(xml.Descendants("member").Any(e => (string?)e.Attribute("name") == "T:Bathos.Graphics.SvgTemplates.BindingConfiguration"), "Missing public API documentation.");

        var assemblyBytes = files["lib/net10.0/" + PackageId + ".dll"];
        using var assembly = new PEReader(new MemoryStream(assemblyBytes, false));
        Require(assembly.HasMetadata && assembly.PEHeaders.CorHeader is { Flags: var flags } && flags.HasFlag(CorFlags.ILOnly), "Library is not managed IL.");
        var metadata = assembly.GetMetadataReader();
        Require(metadata.GetString(metadata.GetAssemblyDefinition().Name) == PackageId, "Incorrect assembly name.");
        Require(metadata.GetTableRowCount(TableIndex.ImplMap) == 0, "Native imports are not allowed.");
        var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray();
        Require(references.All(n => n.StartsWith("System.", StringComparison.Ordinal)), "Unexpected non-BCL assembly reference.");
        var codeView = assembly.ReadDebugDirectory().Where(d => d.Type == DebugDirectoryEntryType.CodeView).ToArray();
        Require(codeView.Length == 1, "Expected one portable debug reference.");
        var debug = assembly.ReadCodeViewDebugDirectoryData(codeView[0]);
        ValidateSourcePath(debug.Path);

        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(symbolFiles["lib/net10.0/" + PackageId + ".pdb"], false));
        var pdb = provider.GetMetadataReader();
        var header = pdb.DebugMetadataHeader ?? throw new InvalidDataException("Missing portable PDB header.");
        var contentId = new BlobContentId(header.Id);
        Require(contentId.Guid == debug.Guid && contentId.Stamp == codeView[0].Stamp && debug.Age == 1, "Symbols do not match the assembly.");
        var documents = pdb.Documents.Select(h => pdb.GetString(pdb.GetDocument(h).Name)).ToArray();
        Require(documents.Length > 0, "Symbols contain no source documents.");
        foreach (var document in documents) ValidateSourcePath(document);
        var links = new List<byte[]>();
        foreach (var handle in pdb.CustomDebugInformation)
        {
            var info = pdb.GetCustomDebugInformation(handle);
            var kind = pdb.GetGuid(info.Kind);
            Require(kind != EmbeddedSourceKind, "Unexpected embedded source.");
            if (kind == SourceLinkKind) links.Add(pdb.GetBlobBytes(info.Value));
        }
        Require(links.Count == 1, "Expected one Source Link record.");
        using var sourceLink = JsonDocument.Parse(links[0]);
        var mappings = sourceLink.RootElement.GetProperty("documents").EnumerateObject().ToArray();
        Require(mappings.Length == 1 && mappings[0].Name == "/_/*" &&
            mappings[0].Value.GetString() == $"https://raw.githubusercontent.com/bathos-labs/svt-dotnet/{commit}/*", "Unexpected Source Link mapping.");
        return new(PackageId, version, commit, FileHash(package), FileHash(symbols), Hash(assemblyBytes),
            files.Keys.Order(StringComparer.Ordinal).ToArray(), symbolFiles.Keys.Order(StringComparer.Ordinal).ToArray(), references, documents);
    }

    internal static Dictionary<string, byte[]> ReadArchive(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            Require(!string.IsNullOrEmpty(name) && !name.Contains('\\') && !name.StartsWith('/') &&
                !name.Contains(':') && name.Split('/').All(p => p.Length > 0 && p is not "." and not ".."), "Unsafe archive entry.");
            total += entry.Length;
            Require(total <= 16 * 1024 * 1024 && entry.Length <= 16 * 1024 * 1024, "Package inspection size limit exceeded.");
            using var stream = entry.Open();
            var bytes = new byte[(int)entry.Length];
            stream.ReadExactly(bytes);
            Require(stream.ReadByte() == -1, "Invalid archive entry length.");
            Require(files.TryAdd(name, bytes), "Duplicate archive entry: " + name);
        }
        return files;
    }

    internal static void ValidateContents(IReadOnlyDictionary<string, byte[]> files, bool symbols)
    {
        var required = symbols
            ? new[] { PackageId + ".nuspec", "lib/net10.0/" + PackageId + ".pdb", "_rels/.rels", "[Content_Types].xml" }
            : new[] { PackageId + ".nuspec", "lib/net10.0/" + PackageId + ".dll", "lib/net10.0/" + PackageId + ".xml", "README.md", "LICENSE", "NOTICE", "_rels/.rels", "[Content_Types].xml" };
        foreach (var name in required) Require(files.ContainsKey(name), "Missing package file: " + name);
        var metadata = files.Keys.Where(n => n.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) && n.EndsWith(".psmdcp", StringComparison.Ordinal)).ToArray();
        Require(metadata.Length == 1 && files.Count == required.Length + 1 && files.Keys.All(n => required.Contains(n, StringComparer.Ordinal) || n == metadata[0]), "Unexpected package content.");
    }

    internal static void ValidateMetadata(byte[] bytes, string version, string commit, bool symbols)
    {
        var document = ReadXml(bytes);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = document.Root?.Element(ns + "metadata") ?? throw new InvalidDataException("Missing package metadata.");
        string? Value(string name) => metadata.Element(ns + name)?.Value;
        Require(Value("id") == PackageId && Value("version") == version, "Package identity mismatch.");
        Require(!string.IsNullOrWhiteSpace(Value("description")), "Missing package description.");
        if (!symbols)
        {
            Require(!string.IsNullOrWhiteSpace(Value("authors")), "Missing package authors.");
            Require(Value("license") == "MIT" && (string?)metadata.Element(ns + "license")?.Attribute("type") == "expression", "Incorrect license metadata.");
        }
        var repository = metadata.Element(ns + "repository");
        Require((string?)repository?.Attribute("type") == "git" && (string?)repository?.Attribute("url") == Repository &&
            (string?)repository?.Attribute("commit") == commit, "Incorrect repository provenance.");
        Require(!metadata.Descendants(ns + "dependency").Any(), "Production package dependencies are prohibited.");
        Require(!metadata.Descendants(ns + "frameworkReference").Any() && !metadata.Descendants(ns + "frameworkAssembly").Any(), "Additional framework dependencies are prohibited.");
        var groups = metadata.Descendants(ns + "group").ToArray();
        Require(groups.Length == 1 && (string?)groups[0].Attribute("targetFramework") == "net10.0", "Incorrect target framework.");
        if (!symbols) Require(Value("readme") == "README.md", "Missing package README metadata.");
    }

    internal static void ValidateSourcePath(string path) => Require(path.StartsWith("/_/", StringComparison.Ordinal) &&
        !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(p => p is not "." and not ".."), "Nonportable debug/source path.");

    private static XDocument ReadXml(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        return XDocument.Load(reader);
    }

    private static string FileHash(string path) => Hash(File.ReadAllBytes(path));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
