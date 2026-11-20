using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Bathos.Graphics.SvgTemplates.PackageValidation.Tests;

/// <summary>Package admission rejects accidental payloads and incorrect provenance.</summary>
[TestClass]
public sealed class PackageInspectorTests
{
    /// <summary>Signing can add a signature but cannot alter any original package content.</summary>
    [TestMethod]
    public void PublishedPackagePreservesEveryOriginalEntry()
    {
        var candidate = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["library"] = [1, 2], ["README"] = [3] };
        var published = new Dictionary<string, byte[]>(candidate, StringComparer.OrdinalIgnoreCase) { [".signature.p7s"] = [4] };
        PackageInspector.ComparePublished(candidate, published);
        published["library"] = [9];
        Assert.Throws<InvalidDataException>(() => PackageInspector.ComparePublished(candidate, published));
        published["library"] = [1, 2];
        published.Remove("README");
        published["readme"] = [3];
        Assert.Throws<InvalidDataException>(() => PackageInspector.ComparePublished(candidate, published));
        published.Remove("readme");
        published["README"] = [3];
        published["extra"] = [5];
        Assert.Throws<InvalidDataException>(() => PackageInspector.ComparePublished(candidate, published));
        published.Remove("extra");
        published[".signature.p7s"] = [];
        Assert.Throws<InvalidDataException>(() => PackageInspector.ComparePublished(candidate, published));
        published.Remove(".signature.p7s");
        Assert.Throws<InvalidDataException>(() => PackageInspector.ComparePublished(candidate, published));
    }
    private const string Version = "0.1.0-preview.g0123456789ab.n0123456789abcdef";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static string Metadata => $"""
        <package><metadata><id>{PackageInspector.PackageId}</id><version>{Version}</version>
        <authors>Bathos Pty Ltd</authors><description>Portable SVG templates.</description>
        <license type="expression">MIT</license><readme>README.md</readme>
        <repository type="git" url="{PackageInspector.Repository}" commit="{Commit}" />
        <dependencies><group targetFramework="net10.0" /></dependencies></metadata></package>
        """;

    /// <summary>A normal BCL-only package has metadata independent of the symbols format.</summary>
    [TestMethod]
    public void MetadataAndSymbolsHaveExplicitRequirements()
    {
        PackageInspector.ValidateMetadata(Encoding.UTF8.GetBytes(Metadata), Version, Commit, false);
        var symbols = XDocument.Parse(Metadata);
        symbols.Descendants("authors").Remove();
        symbols.Descendants("license").Remove();
        symbols.Descendants("readme").Remove();
        PackageInspector.ValidateMetadata(Encoding.UTF8.GetBytes(symbols.ToString()), Version, Commit, true);
        Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateMetadata(Encoding.UTF8.GetBytes(symbols.ToString()), Version, Commit, false));
    }

    /// <summary>Identity, dependencies, license, framework and source identity cannot drift.</summary>
    [TestMethod]
    public void MetadataRejectsDrift()
    {
        var invalid = new[]
        {
            Metadata.Replace(Version, "0.1.0-other", StringComparison.Ordinal),
            Metadata.Replace(Commit, new string('f', 40), StringComparison.Ordinal),
            Metadata.Replace("MIT", "Apache-2.0", StringComparison.Ordinal),
            Metadata.Replace("net10.0", "net9.0", StringComparison.Ordinal),
            Metadata.Replace("<group targetFramework=\"net10.0\" />", "<group targetFramework=\"net10.0\"><dependency id=\"Renderer\" version=\"1.0.0\" /></group>", StringComparison.Ordinal),
            Metadata.Replace("README.md", "missing.md", StringComparison.Ordinal),
            Metadata.Replace("</metadata>", "<frameworkReferences><group targetFramework=\"net10.0\"><frameworkReference name=\"Microsoft.AspNetCore.App\" /></group></frameworkReferences></metadata>", StringComparison.Ordinal),
        };
        foreach (var value in invalid)
            Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateMetadata(Encoding.UTF8.GetBytes(value), Version, Commit, false));
        Assert.Throws<System.Xml.XmlException>(() => PackageInspector.ValidateMetadata(Encoding.UTF8.GetBytes("<!DOCTYPE package [<!ENTITY x 'value'>]>" + Metadata), Version, Commit, false));
    }

    /// <summary>Archive names cannot overwrite or escape their intended package entries.</summary>
    [TestMethod]
    public void ArchiveRejectsAliasesAndTraversal()
    {
        foreach (var names in new[] { new[] { "../outside" }, new[] { "/absolute" }, new[] { "C:/absolute" }, new[] { "a\\b" }, new[] { "a//b" }, new[] { "a", "A" } })
        {
            var path = Path.GetTempFileName();
            try
            {
                using (var stream = File.Open(path, FileMode.Truncate))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    foreach (var name in names)
                    {
                        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                        writer.Write("content");
                    }
                Assert.Throws<InvalidDataException>(() => PackageInspector.ReadArchive(path));
            }
            finally { File.Delete(path); }
        }
    }

    /// <summary>The allowlist excludes tests, CLI binaries, native tools and build hooks.</summary>
    [TestMethod]
    public void PackagePayloadIsRestricted()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [PackageInspector.PackageId + ".nuspec"] = [],
            ["lib/net10.0/" + PackageInspector.PackageId + ".dll"] = [],
            ["lib/net10.0/" + PackageInspector.PackageId + ".xml"] = [],
            ["README.md"] = [],
            ["LICENSE"] = [],
            ["NOTICE"] = [],
            ["_rels/.rels"] = [],
            ["[Content_Types].xml"] = [],
            ["package/services/metadata/core-properties/nuget.psmdcp"] = [],
        };
        PackageInspector.ValidateContents(files, false);
        foreach (var name in new[] { "lib/net10.0/svt.dll", "tests/fixture.svg", "runtimes/native.dll", "build/inject.targets" })
        {
            files.Add(name, []);
            Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateContents(files, false));
            files.Remove(name);
        }
        files.Remove("NOTICE");
        Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateContents(files, false));
    }

    /// <summary>Highly compressed unexpected content is bounded before decompression.</summary>
    [TestMethod]
    public void ArchiveSizeIsBounded()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Open(path, FileMode.Truncate))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            using (var entry = zip.CreateEntry("large").Open())
                entry.Write(new byte[16 * 1024 * 1024 + 1]);
            Assert.Throws<InvalidDataException>(() => PackageInspector.ReadArchive(path));
        }
        finally { File.Delete(path); }
    }

    /// <summary>Portable debug paths exclude machine directories and path traversal.</summary>
    [TestMethod]
    public void SourcePathsMustBeMapped()
    {
        PackageInspector.ValidateSourcePath("/_/src/Library/File.cs");
        foreach (var value in new[] { "/private/work/File.cs", "C:\\workspace\\File.cs", "/_/../File.cs", "/_/a/./File.cs", "/_/C:/File.cs" })
            Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateSourcePath(value));
    }
    /// <summary>Tool metadata cannot hide package dependencies or an incorrect command type.</summary>
    [TestMethod]
    public void ToolMetadataRequiresExplicitToolTypeAndNoDependencies()
    {
        var metadata = Metadata.Replace($"<id>{PackageInspector.PackageId}</id>", $"<id>{PackageInspector.ToolPackageId}</id>", StringComparison.Ordinal)
            .Replace("<dependencies><group targetFramework=\"net10.0\" /></dependencies>", "<packageTypes><packageType name=\"DotnetTool\" /></packageTypes>", StringComparison.Ordinal);
        PackageInspector.ValidateMetadata(Encoding.UTF8.GetBytes(metadata), Version, Commit, false, true);
        foreach (var invalid in new[]
        {
            metadata.Replace("DotnetTool", "Library", StringComparison.Ordinal),
            metadata.Replace("</metadata>", "<dependencies><dependency id=\"Extra\" version=\"1.0.0\" /></dependencies></metadata>", StringComparison.Ordinal),
            metadata.Replace(PackageInspector.ToolPackageId, PackageInspector.PackageId, StringComparison.Ordinal),
        })
            Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateMetadata(Encoding.UTF8.GetBytes(invalid), Version, Commit, false, true));
    }

    /// <summary>Tool symbols have an exact two-assembly allowlist.</summary>
    [TestMethod]
    public void ToolSymbolPayloadRejectsMissingAndAdditionalFiles()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [PackageInspector.ToolPackageId + ".nuspec"] = [],
            ["tools/net10.0/any/svt.pdb"] = [],
            ["tools/net10.0/any/" + PackageInspector.PackageId + ".pdb"] = [],
            ["_rels/.rels"] = [],
            ["[Content_Types].xml"] = [],
            ["package/services/metadata/core-properties/nuget.psmdcp"] = [],
        };
        PackageInspector.ValidateToolContents(files, true);
        files["tools/net10.0/any/extra.dll"] = [];
        Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateToolContents(files, true));
        files.Remove("tools/net10.0/any/extra.dll");
        files.Remove("tools/net10.0/any/svt.pdb");
        Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateToolContents(files, true));
    }

    /// <summary>Candidate archives are deterministic and never overwrite an extraction directory.</summary>
    [TestMethod]
    public void CandidateArchivePreservesBytesAndRefusesReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "svt-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "images"));
            File.WriteAllBytes(Path.Combine(source, "images", "frame.png"), [1, 2, 3]);
            var first = Path.Combine(root, "first.zip");
            var second = Path.Combine(root, "second.zip");
            CandidateArchive.Create(source, first);
            CandidateArchive.Create(source, second);
            CollectionAssert.AreEqual(File.ReadAllBytes(first), File.ReadAllBytes(second));
            Assert.Throws<IOException>(() => CandidateArchive.Create(source, first));
            var output = Path.Combine(root, "output");
            CandidateArchive.Extract(first, output);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(output, "images", "frame.png")));
            Assert.Throws<IOException>(() => CandidateArchive.Extract(first, output));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Recovery cannot extract path aliases, traversal or duplicate assets.</summary>
    [TestMethod]
    public void CandidateArchiveRejectsUnsafeNamesBeforeWriting()
    {
        foreach (var names in new[] { new[] { "../outside" }, new[] { "/absolute" }, new[] { "C:/absolute" }, new[] { "name.", "name" }, new[] { "a", "A" } })
        {
            var root = Path.Combine(Path.GetTempPath(), "svt-archive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "candidate.zip");
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                    foreach (var name in names)
                    {
                        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                        writer.Write("evidence");
                    }
                var output = Path.Combine(root, "output");
                Assert.Throws<InvalidDataException>(() => CandidateArchive.Extract(path, output));
                Assert.IsFalse(Directory.Exists(output));
            }
            finally { Directory.Delete(root, true); }
        }
    }

    /// <summary>Runtime admission rejects roll-forward, command changes and unlisted dependencies.</summary>
    [TestMethod]
    public void ToolRuntimeRequiresThePortableDeclaredClosure()
    {
        const string prefix = "tools/net10.0/any/";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [prefix + "DotnetToolSettings.xml"] = Encoding.UTF8.GetBytes("<DotNetCliTool Version=\"1\"><Commands><Command Name=\"svt\" EntryPoint=\"svt.dll\" Runner=\"dotnet\" /></Commands></DotNetCliTool>"),
            [prefix + "svt.runtimeconfig.json"] = Encoding.UTF8.GetBytes("""{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}"""),
            [prefix + "svt.deps.json"] = Encoding.UTF8.GetBytes("""
                {"runtimeTarget":{"name":".NETCoreApp,Version=v10.0"},"libraries":{"svt/@VERSION@":{"type":"project"},"@ID@/@VERSION@":{"type":"project"}},
                "targets":{".NETCoreApp,Version=v10.0":{"svt/@VERSION@":{"dependencies":{"@ID@":"@VERSION@"},"runtime":{"svt.dll":{}}},"@ID@/@VERSION@":{"runtime":{"@ID@.dll":{}}}}}}
                """.Replace("@VERSION@", Version, StringComparison.Ordinal).Replace("@ID@", PackageInspector.PackageId, StringComparison.Ordinal)),
        };
        PackageInspector.ValidateToolRuntime(files, Version);
        foreach (var (name, before, after) in new[]
        {
            ("DotnetToolSettings.xml", "Name=\"svt\"", "Name=\"another\""),
            ("svt.runtimeconfig.json", "\"tfm\":", "\"rollForward\":\"Major\",\"tfm\":"),
            ("svt.runtimeconfig.json", "net10.0", "net9.0"),
            ("svt.deps.json", "\"dependencies\":{", "\"dependencies\":{\"Unexpected\":\"1.0.0\","),
        })
        {
            var key = prefix + name;
            var original = files[key];
            files[key] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace(before, after, StringComparison.Ordinal));
            Assert.Throws<InvalidDataException>(() => PackageInspector.ValidateToolRuntime(files, Version));
            files[key] = original;
        }
    }

}
