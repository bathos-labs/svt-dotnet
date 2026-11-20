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
    string[] SymbolFiles, string[] AssemblyReferences, string[] SourceDocuments, string SymbolKey,
    string SymbolChecksum, string PdbSha256, string? BundledLibrarySha256 = null, string? BundledLibraryPdbSha256 = null);

internal static class PackageInspector
{
    internal const string PackageId = "Bathos.Graphics.SvgTemplates";
    internal const string ToolPackageId = PackageId + ".Cli";
    private const string ToolPrefix = "tools/net10.0/any/";
    internal const string Repository = "https://github.com/bathos-labs/svt-dotnet";
    private static readonly string[] LegalFiles = ["README.md", "LICENSE", "NOTICE"];
    private static readonly Guid SourceLinkKind = new("cc110556-a091-4d38-9fec-25ab9a351a6a");
    private static readonly Guid EmbeddedSourceKind = new("0e8a571b-6926-466e-b4ad-8ab04611f5fe");

    // Repository signing changes ZIP bytes. All original entry names and contents must survive.
    // Signature trust is checked separately by `dotnet nuget verify --all`.
    internal static void ComparePublished(IReadOnlyDictionary<string, byte[]> candidate, IReadOnlyDictionary<string, byte[]> published)
    {
        Require(!candidate.ContainsKey(".signature.p7s"), "Candidate must be unsigned.");
        Require(published.TryGetValue(".signature.p7s", out var signature) && signature.Length > 0, "Missing repository signature.");
        Require(published.Count == candidate.Count + 1, "Published package file set changed.");
        foreach (var (name, bytes) in candidate)
            Require(published.Keys.Contains(name, StringComparer.Ordinal) && published.TryGetValue(name, out var actual) && bytes.AsSpan().SequenceEqual(actual), "Published package content changed: " + name);
    }

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
        return InspectAssembly(package, symbols, PackageId, PackageId, version, commit, files, symbolFiles,
            assemblyBytes, symbolFiles["lib/net10.0/" + PackageId + ".pdb"]);
    }

    internal static InspectionReport InspectTool(string package, string symbols, string version, string commit, string library, string librarySymbols)
    {
        var original = Inspect(library, librarySymbols, version, commit);
        var files = ReadArchive(package);
        var symbolFiles = ReadArchive(symbols);
        ValidateToolContents(files, false);
        ValidateToolContents(symbolFiles, true);
        ValidateMetadata(files[ToolPackageId + ".nuspec"], version, commit, false, true);
        ValidateMetadata(symbolFiles[ToolPackageId + ".nuspec"], version, commit, true, true);
        foreach (var name in new[] { "README.md", "LICENSE", "NOTICE" })
            Require(files[name].Length > 0, "Empty required file: " + name);
        Require(Encoding.UTF8.GetString(files["LICENSE"]).Contains("MIT License", StringComparison.Ordinal), "Missing MIT license text.");
        Require(Hash(files[ToolPrefix + PackageId + ".dll"]) == original.AssemblySha256, "Bundled library differs.");
        Require(Hash(files[ToolPrefix + PackageId + ".pdb"]) == original.PdbSha256, "Bundled library symbols differ.");
        foreach (var name in new[] { "svt", PackageId })
            Require(files[ToolPrefix + name + ".pdb"].AsSpan().SequenceEqual(symbolFiles[ToolPrefix + name + ".pdb"]), "Tool symbols differ.");
        ValidateToolRuntime(files, version);
        var report = InspectAssembly(package, symbols, ToolPackageId, "svt", version, commit, files, symbolFiles,
            files[ToolPrefix + "svt.dll"], symbolFiles[ToolPrefix + "svt.pdb"]);
        return report with { BundledLibrarySha256 = original.AssemblySha256, BundledLibraryPdbSha256 = original.PdbSha256 };
    }

    internal static void ValidateToolContents(IReadOnlyDictionary<string, byte[]> files, bool symbols)
    {
        var payload = symbols ? new[] { PackageId + ".pdb", "svt.pdb" } : new[]
        {
            PackageId + ".dll", PackageId + ".pdb", PackageId + ".xml", "DotnetToolSettings.xml",
            "svt.deps.json", "svt.dll", "svt.pdb", "svt.runtimeconfig.json", "svt.xml",
        };
        var required = new[] { ToolPackageId + ".nuspec", "_rels/.rels", "[Content_Types].xml" }
            .Concat(payload.Select(n => ToolPrefix + n)).Concat(symbols ? [] : LegalFiles).ToArray();
        ValidateFileSet(files, required);
    }

    internal static void ValidateToolRuntime(IReadOnlyDictionary<string, byte[]> files, string version)
    {
        var settings = ReadXml(files[ToolPrefix + "DotnetToolSettings.xml"]);
        var commands = settings.Root?.Element("Commands")?.Elements("Command").ToArray();
        Require(settings.Root?.Name == "DotNetCliTool" && (string?)settings.Root.Attribute("Version") == "1" && commands?.Length == 1,
            "Invalid tool command metadata.");
        var command = commands?.Single() ?? throw new InvalidDataException("Missing tool command.");
        Require((string?)command.Attribute("Name") == "svt" && (string?)command.Attribute("EntryPoint") == "svt.dll" &&
            (string?)command.Attribute("Runner") == "dotnet", "Incorrect tool command.");
        using var runtime = JsonDocument.Parse(files[ToolPrefix + "svt.runtimeconfig.json"]);
        var options = runtime.RootElement.GetProperty("runtimeOptions");
        Require(options.GetProperty("tfm").GetString() == "net10.0" &&
            options.GetProperty("framework").GetProperty("name").GetString() == "Microsoft.NETCore.App" &&
            options.GetProperty("framework").GetProperty("version").GetString() == "10.0.0" &&
            !options.TryGetProperty("rollForward", out _) && !options.TryGetProperty("frameworks", out _) &&
            !options.TryGetProperty("includedFrameworks", out _), "Unexpected tool runtime requirements.");
        using var deps = JsonDocument.Parse(files[ToolPrefix + "svt.deps.json"]);
        var names = new[] { "svt/" + version, PackageId + "/" + version };
        Require(deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString() == ".NETCoreApp,Version=v10.0", "Incorrect runtime target.");
        var libraries = deps.RootElement.GetProperty("libraries").EnumerateObject().ToArray();
        Require(libraries.Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)) &&
            libraries.All(x => x.Value.GetProperty("type").GetString() == "project"), "Unexpected tool dependency.");
        var targets = deps.RootElement.GetProperty("targets").EnumerateObject().ToArray();
        Require(targets.Length == 1 && targets[0].Name == ".NETCoreApp,Version=v10.0", "Unexpected runtime targets.");
        Require(targets[0].Value.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)), "Incorrect runtime closure.");
        var dependencies = targets[0].Value.GetProperty(names[0]).GetProperty("dependencies").EnumerateObject().ToArray();
        Require(dependencies.Length == 1 && dependencies[0].Name == PackageId && dependencies[0].Value.GetString() == version,
            "Incorrect bundled library dependency.");
        Require(!targets[0].Value.GetProperty(names[1]).TryGetProperty("dependencies", out _), "Library must remain dependency-free.");
        foreach (var (name, file) in new[] { (names[0], "svt.dll"), (names[1], PackageId + ".dll") })
        {
            var target = targets[0].Value.GetProperty(name);
            Require(target.GetProperty("runtime").EnumerateObject().Select(x => x.Name).SequenceEqual(new[] { file }) &&
                !target.TryGetProperty("native", out _) && !target.TryGetProperty("runtimeTargets", out _), "Unexpected runtime payload.");
        }
    }

    private static InspectionReport InspectAssembly(string package, string symbols, string id, string assemblyName, string version, string commit,
        IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<string, byte[]> symbolFiles, byte[] assemblyBytes, byte[] pdbBytes)
    {
        using var assembly = new PEReader(new MemoryStream(assemblyBytes, false));
        Require(assembly.HasMetadata && assembly.PEHeaders.CorHeader is { Flags: var flags } && flags.HasFlag(CorFlags.ILOnly), "Assembly is not managed IL.");
        var metadata = assembly.GetMetadataReader();
        Require(metadata.GetString(metadata.GetAssemblyDefinition().Name) == assemblyName, "Incorrect assembly name.");
        Require(metadata.GetTableRowCount(TableIndex.ImplMap) == 0, "Native imports are not allowed.");
        var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray();
        Require(references.All(n => n.StartsWith("System.", StringComparison.Ordinal) || (assemblyName == "svt" && n == PackageId)), "Unexpected non-BCL assembly reference.");
        var codeView = assembly.ReadDebugDirectory().Where(d => d.Type == DebugDirectoryEntryType.CodeView).ToArray();
        Require(codeView.Length == 1, "Expected one portable debug reference.");
        var debug = assembly.ReadCodeViewDebugDirectoryData(codeView[0]);
        ValidateSourcePath(debug.Path);
        var checksums = assembly.ReadDebugDirectory().Where(d => d.Type == DebugDirectoryEntryType.PdbChecksum).ToArray();
        Require(checksums.Length == 1, "Expected one PDB checksum.");
        var checksum = assembly.ReadPdbChecksumDebugDirectoryData(checksums[0]);
        Require(checksum.AlgorithmName == "SHA256", "Expected SHA256 PDB checksum.");

        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdbBytes, false));
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
        return new(id, version, commit, FileHash(package), FileHash(symbols), Hash(assemblyBytes),
            files.Keys.Order(StringComparer.Ordinal).ToArray(), symbolFiles.Keys.Order(StringComparer.Ordinal).ToArray(), references, documents,
            debug.Guid.ToString("N").ToUpperInvariant() + "FFFFFFFF",
            "SHA256:" + Convert.ToHexString(checksum.Checksum.AsSpan()), Hash(pdbBytes));
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
        ValidateFileSet(files, required);
    }

    private static void ValidateFileSet(IReadOnlyDictionary<string, byte[]> files, string[] required)
    {
        foreach (var name in required) Require(files.ContainsKey(name), "Missing package file: " + name);
        var metadata = files.Keys.Where(n => n.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) && n.EndsWith(".psmdcp", StringComparison.Ordinal)).ToArray();
        Require(metadata.Length == 1 && files.Count == required.Length + 1 && files.Keys.All(n => required.Contains(n, StringComparer.Ordinal) || n == metadata[0]), "Unexpected package content.");
    }

    internal static void ValidateMetadata(byte[] bytes, string version, string commit, bool symbols, bool tool = false)
    {
        var document = ReadXml(bytes);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = document.Root?.Element(ns + "metadata") ?? throw new InvalidDataException("Missing package metadata.");
        string? Value(string name) => metadata.Element(ns + name)?.Value;
        Require(Value("id") == (tool ? ToolPackageId : PackageId) && Value("version") == version, "Package identity mismatch.");
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
        Require(tool ? groups.Length == 0 : groups.Length == 1 && (string?)groups[0].Attribute("targetFramework") == "net10.0", "Incorrect target framework.");
        if (tool)
        {
            var types = metadata.Descendants(ns + "packageType").ToArray();
            Require(types.Length == 1 && (string?)types[0].Attribute("name") == (symbols ? "SymbolsPackage" : "DotnetTool"), "Incorrect tool package type.");
        }
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
