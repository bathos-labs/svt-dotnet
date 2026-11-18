using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Bathos.Graphics.SvgTemplates.PackageValidation.Tests;

/// <summary>Package admission rejects accidental payloads and incorrect provenance.</summary>
[TestClass]
public sealed class PackageInspectorTests
{
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
}
