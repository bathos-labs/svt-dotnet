using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Bathos.Graphics.SvgTemplates.Cli.Tests;

/// <summary>Observable text presentation, bounded previews and machine-output separation.</summary>
[TestClass]
public sealed class PresentationTests
{
    /// <summary>Escaping preserves ordinary Unicode and exposes terminal and directional controls.</summary>
    [TestMethod]
    public void UnicodeEscaping()
    {
        const string readable = "café 中文 😀 e\u0301 👩\u200d💻 \uFE0F …";
        Assert.AreEqual(readable, HumanText.Escape(readable));
        Assert.AreEqual("\\n\\r\\t\\\\\"", HumanText.Escape("\n\r\t\\\""));
        Assert.AreEqual("\"\\n\\r\\t\\\\\\\"\"", HumanText.Quote("\n\r\t\\\""));
        foreach (var c in Enumerable.Range(0, 0x10000).Select(i => (char)i).Where(c =>
            c is <= '\u001f' or >= '\u007f' and <= '\u009f' or '\u2028' or '\u2029' or '\u061c'
                or '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u206f'))
        {
            if (c is '\n' or '\r' or '\t') continue;
            Assert.AreEqual("\\u" + ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture), HumanText.Escape(c.ToString()));
        }
        Assert.AreEqual("\\uD800a\\uDC00😀", HumanText.Escape("\ud800a\udc00😀"));
    }

    /// <summary>The cap counts displayed scalars after escaping and never cuts an escape or surrogate pair.</summary>
    [TestMethod]
    public void PreviewBoundaries()
    {
        foreach (var length in new[] { 159, 160 })
            Assert.AreEqual('"' + new string('a', length) + '"', HumanText.Preview(new string('a', length)));
        Assert.AreEqual('"' + new string('a', 159) + "…\" (shortened)", HumanText.Preview(new string('a', 161)));
        Assert.AreEqual('"' + string.Concat(Enumerable.Repeat("😀", 159)) + "…\" (shortened)",
            HumanText.Preview(string.Concat(Enumerable.Repeat("😀", 161))));
        Assert.AreEqual('"' + new string('a', 158) + "\\n\"", HumanText.Preview(new string('a', 158) + "\n"));
        Assert.AreEqual('"' + new string('a', 158) + "…\" (shortened)", HumanText.Preview(new string('a', 158) + "\nx"));
        Assert.AreEqual('"' + new string('a', 154) + "…\" (shortened)", HumanText.Preview(new string('a', 154) + "\u202Ex"));
        Assert.AreEqual('"' + new string('a', 158) + "e…\" (shortened)", HumanText.Preview(new string('a', 158) + "e\u0301x"));
        Assert.AreEqual('"' + new string('a', 158) + "👩…\" (shortened)", HumanText.Preview(new string('a', 158) + "👩\u200d💻"));
        Assert.AreEqual("\"…\"", HumanText.Preview("…"));
        foreach (var (raw, escaped) in new[] { ("\"", "\\\""), ("\\", "\\\\"), ("\u001B", "\\u001B"), ("\uD800", "\\uD800") })
        {
            var prefix = new string('a', 160 - escaped.Length);
            Assert.AreEqual('"' + prefix + escaped + '"', HumanText.Preview(prefix + raw));
            Assert.AreEqual('"' + prefix + "…\" (shortened)", HumanText.Preview(prefix + raw + "x"));
        }
        Assert.AreEqual(new string('a', 159) + "… (shortened)", HumanText.Preview(new string('a', 161), false));
        // The display cost must be independent of the undisplayed suffix, including allocation.
        var huge = new string('a', 4_000_000);
        _ = HumanText.Preview(huge);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var preview = HumanText.Preview(huge);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsLessThan(8192L, allocated);
        Assert.AreEqual('"' + new string('a', 159) + "…\" (shortened)", preview);
    }

    /// <summary>Help bypasses acquisition, while validating supplied syntax and combinations.</summary>
    [TestMethod]
    public async Task HelpAndUsageStreams()
    {
        using var w = new Workspace();
        foreach (var command in new[] { "inspect", "bind" })
        {
            var help = await w.Process(command, "missing.svg", "--configuration", "missing.json", "-h");
            Assert.AreEqual(0, help.ExitCode);
            Assert.AreEqual("", help.Error);
            StringAssert.Contains(help.Output, "svt " + command);
            StringAssert.Contains(help.Output, "default:");
            var jsonHelp = await w.Run(command, "--help", "--diagnostics", "json");
            Assert.AreEqual(0, jsonHelp.ExitCode);
            Assert.HasCount(0, jsonHelp.Diagnostics);
            Assert.IsTrue(jsonHelp.Output.Contains('\n'));
            var duplicate = await w.Run(command, "-h", "--help", "--diagnostics", "json");
            Assert.AreEqual(2, duplicate.ExitCode);
            Assert.AreEqual("", duplicate.Output);
            Assert.AreEqual("svt.cli.usage", duplicate.Code);
        }
        foreach (var args in new[]
        {
            new[] { "inspect", "--help", "--format", "bad" },
            ["bind", "--help", "--output", "-", "--overwrite"],
            ["bind", "--help", "-", "--bindings", "-"],
            ["inspect", "-h", "--configuration", "-"],
            ["inspect", "--help", "--format"],
            ["--help", "inspect"], ["--version", "--help"]
        }) Assert.AreEqual(2, (await w.Run(args)).ExitCode);
        Assert.AreEqual((await w.Run("--help")).Output, (await w.Process("-h")).Output);
        w.Write("-h", CliTests.TemplateText);
        Assert.AreEqual(0, (await w.Run("inspect", "--", "-h")).ExitCode);
        var empty = await w.Process();
        Assert.AreEqual(2, empty.ExitCode);
        Assert.AreEqual("", empty.Output);
        StringAssert.Contains(empty.Error, "\nsvt inspect");
        Assert.IsFalse(empty.Error.Contains("\\n", StringComparison.Ordinal));
        var jsonError = await w.Process("--diagnostics", "json");
        Assert.AreEqual(2, jsonError.ExitCode);
        Assert.AreEqual("", jsonError.Output);
        Assert.AreEqual("svt.cli.usage", jsonError.Code);
        var invalid = await w.Run("inspect", "--bad\n\u001b\ud800");
        StringAssert.Contains(invalid.Error, "\\n\\u001B\\uD800");
        StringAssert.Contains(invalid.Error, "Use svt inspect --help");
    }

    /// <summary>Discovery reports raw default states and keeps full JSON evidence.</summary>
    [TestMethod]
    public async Task InspectionDefaultsAndPayloads()
    {
        using var w = new Workspace();
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" data-svt-version="0">
              <text data-svt-text="heading" data-svt-label="Heading">Welcome</text>
              <g data-svt-fill="shared" fill="red"/><g data-svt-stroke="shared" stroke="red"/>
              <g data-svt-fill="mixed" fill="red"/><g data-svt-stroke="mixed"/>
              <g data-svt-fill="absent"/><text data-svt-text="empty"/>
              <image data-svt-image="embedded" href="DaTa:invalid,secret-payload"/>
              <image data-svt-image="leading" href=" data:visible"/>
            </svg>
            """;
        w.Write("template.svg", svg);
        var text = await w.Process("inspect", "template.svg");
        Assert.AreEqual(0, text.ExitCode);
        StringAssert.Contains(text.Output, "Heading [heading] — text");
        StringAssert.Contains(text.Output, "Default: \"Welcome\"");
        StringAssert.Contains(text.Output, "Target: fill <g>, SVG line");
        StringAssert.Contains(text.Output, "UTF-16 column");
        Assert.AreEqual(2, text.Output.Split("\"red\"", StringSplitOptions.None).Length - 1); // Common once, Mixed once.
        StringAssert.Contains(text.Output, "Defaults differ");
        Assert.AreEqual(2, text.Output.Split("No authored default", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(text.Output, "Default: \"\"");
        StringAssert.Contains(text.Output, "Default: Embedded data (payload hidden)");
        Assert.IsFalse(text.Output.Contains("secret-payload", StringComparison.Ordinal));
        StringAssert.Contains(text.Output, "\" data:visible\"");
        var json = await w.Run("inspect", "template.svg", "--format", "json");
        var inputs = JsonNode.Parse(json.Output)?["inputs"]?.AsArray();
        Assert.IsNotNull(inputs);
        Assert.AreEqual("DaTa:invalid,secret-payload", (string?)inputs[5]?["default"]?["raw"]);
        w.Write("template.svg", "<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='0'/>");
        var empty = await w.Run("inspect", "template.svg");
        Assert.AreEqual(0, empty.ExitCode);
        Assert.AreEqual("No editable inputs found.\n", empty.Output);
        w.Write("template.svg", "<svg");
        var unusable = await w.Run("inspect", "template.svg");
        Assert.AreEqual(1, unusable.ExitCode);
        Assert.AreEqual("No usable template.\n", unusable.Output);
    }

    /// <summary>Full identifiers remain usable while labels, descriptions and raw values are bounded.</summary>
    [TestMethod]
    public async Task MetadataPreviewsAndDiscoveryOrder()
    {
        using var w = new Workspace();
        var longKey = new string('k', 200);
        var longText = new string('x', 200) + "é😀";
        w.Write("template.svg", $$"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:s="https://bathos-labs.github.io/svt" xmlns:r="http://www.w3.org/1999/02/22-rdf-syntax-ns#" data-svt-version="0">
            <metadata><r:RDF><r:Description r:about=""><s:inputs r:parseType="Literal">
              <s:input key="{{longKey}}" label="{{longText}}" order="20"><s:description>{{longText}}</s:description></s:input>
              <s:input key="second" order="10"/>
            </s:inputs></r:Description></r:RDF></metadata>
            <text data-svt-text="{{longKey}}">{{longText}}</text><text data-svt-text="second">Second</text>
            </svg>
            """);
        var text = await w.Run("inspect", "template.svg");
        Assert.AreEqual(0, text.ExitCode);
        StringAssert.Contains(text.Output, "[" + longKey + "]");
        Assert.AreEqual(3, text.Output.Split("(shortened)", StringSplitOptions.None).Length - 1);
        Assert.IsLessThan(text.Output.IndexOf("[second]", StringComparison.Ordinal), text.Output.IndexOf("[" + longKey + "]", StringComparison.Ordinal));
        var json = await w.Run("inspect", "template.svg", "--format", "json");
        Assert.AreEqual(longText, (string?)JsonNode.Parse(json.Output)?["inputs"]?[0]?["description"]);
        Assert.AreEqual(longText, (string?)JsonNode.Parse(json.Output)?["inputs"]?[0]?["default"]?["raw"]);
    }

    /// <summary>Human context shows both evidence sources without changing JSON indices or messages.</summary>
    [TestMethod]
    public async Task DiagnosticEvidence()
    {
        var location = TemplateInspector.Inspect(Encoding.UTF8.GetBytes(CliTests.TemplateText)).Template?.Inputs[0].Targets[0].Location;
        Assert.IsNotNull(location);
        var key = new string('k', 200) + "\u202E";
        var diagnostic = new CliDiagnostic("binding", "svt.binding.example", "warning", "Keep \"café\"\nvisible", key,
            location, [location], "MaxValueCharacters", "template.svg", "bindings.json", "/bindings/0", 0, new(2, 7));
        using var human = new MemoryStream();
        await Reports.DiagnosticsAsync(human, "bind", 1, [diagnostic], false);
        var text = Encoding.UTF8.GetString(human.ToArray());
        foreach (var part in new[] { "Warning: Keep \"café\"\\nvisible", "Source: \"template.svg\"", "Request: \"bindings.json\"",
            "SVG line 1, UTF-16 column", "JSON line 2, UTF-8 byte column 7", "binding 1", "JSON pointer: \"/bindings/0\"",
            key[..200] + "\\u202E", "Limit: MaxValueCharacters", "Related: SVG line", "Code: svt.binding.example" })
            StringAssert.Contains(text, part);
        using var json = new MemoryStream();
        await Reports.DiagnosticsAsync(json, "bind", 1, [diagnostic], true);
        var evidence = JsonNode.Parse(json.ToArray())?["diagnostics"]?[0];
        Assert.AreEqual(0, (int?)evidence?["assignment_index"]);
        Assert.AreEqual(key, (string?)evidence?["key"]);
        Assert.AreEqual(diagnostic.Message, (string?)evidence?["message"]);
        using var sameSource = new MemoryStream();
        await Reports.DiagnosticsAsync(sameSource, "bind", 1,
            [new("input", "svt.cli.json", "error", "Malformed JSON.", Source: "bindings.json", RequestSource: "bindings.json", JsonPosition: new(1, 7))], false);
        var sameText = Encoding.UTF8.GetString(sameSource.ToArray());
        Assert.AreEqual(1, sameText.Split("bindings.json", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(sameText, "JSON line 1, UTF-8 byte column 7");
        using var streams = new MemoryStream();
        await Reports.DiagnosticsAsync(streams, "bind", 3,
            [new("publication", "svt.cli.io", "error", "Cannot write.", Source: "-", RequestSource: "-")], false);
        var streamText = Encoding.UTF8.GetString(streams.ToArray());
        StringAssert.Contains(streamText, "Source: stdout");
        StringAssert.Contains(streamText, "Request: stdin");
        using var absent = new MemoryStream();
        await Reports.DiagnosticsAsync(absent, null, 2, [new("invocation", "svt.cli.usage", "error", "Missing command.")], false);
        var absentText = Encoding.UTF8.GetString(absent.ToArray());
        Assert.IsFalse(absentText.Contains("null", StringComparison.Ordinal));
        Assert.IsFalse(absentText.Contains("binding 1", StringComparison.Ordinal));
        foreach (var (phase, source, expected) in new[] { ("input", "-", "stdin"), ("publication", "-", "stdout"), ("input", "./-", "\"./-\"") })
        {
            using var stream = new MemoryStream();
            await Reports.DiagnosticsAsync(stream, null, 3, [new(phase, "svt.cli.io", "error", "Cannot read.", Source: source)], false);
            StringAssert.Contains(Encoding.UTF8.GetString(stream.ToArray()), "Source: " + expected);
        }
    }

    /// <summary>Acquisition failures retain caller paths and safe remedies without exception details.</summary>
    [TestMethod]
    public async Task IoAndRequestFailures()
    {
        using var w = new Workspace();
        var missing = await w.Process("inspect", "missing.svg");
        Assert.AreEqual(3, missing.ExitCode);
        StringAssert.Contains(missing.Error, "File not found");
        StringAssert.Contains(missing.Error, "\"missing.svg\"");
        Assert.IsFalse(missing.Error.Contains(w.Root, StringComparison.Ordinal));
        var folder = await w.Run("inspect", "missing-folder/file.svg");
        Assert.AreEqual(3, folder.ExitCode);
        StringAssert.Contains(folder.Error, "Folder not found");
        StringAssert.Contains(CliApplication.IoMessage(new UnauthorizedAccessException("SECRET"), false), "Access denied");
        StringAssert.Contains(CliApplication.IoMessage(new IOException("SECRET"), true), "Cannot write");
        StringAssert.Contains(CliApplication.IoMessage(new IOException("SECRET"), false), "Cannot read");
        Assert.IsFalse(CliApplication.IoMessage(new IOException("SECRET"), true).Contains("SECRET", StringComparison.Ordinal));
        w.Write("bindings.json", """{"bindings":[{"key":"t","kind":"text"}]}""");
        var request = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "-");
        Assert.AreEqual(1, request.ExitCode);
        StringAssert.Contains(request.Error, "required value");
        StringAssert.Contains(request.Error, "binding 1");
        StringAssert.Contains(request.Error, "/bindings/0/value");
    }

    /// <summary>Version and revision are independent embedded values, including genuine SemVer metadata.</summary>
    [TestMethod]
    public async Task VersionIdentity()
    {
        const string revision = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
        Assert.AreEqual("svt 1.0.0-beta.1+build.7 (commit abcdef012345)\n", CliVersion.Format("1.0.0-beta.1+build.7", revision));
        Assert.AreEqual("svt 1.0.0-beta.1+build.7\n", CliVersion.Format("1.0.0-beta.1+build.7", null));
        Assert.AreEqual("svt 1.0.0-beta.1\n", CliVersion.Format("1.0.0-beta.1", "not-a-revision"));
        Assert.AreEqual("svt 1.0.0 (commit aaaaaaaaaaaa)\n", CliVersion.Format("1.0.0", new string('A', 64)));
        Assert.AreEqual("svt 1.0.0\n", CliVersion.Format("1.0.0", new string('z', 40)));
        Assert.Throws<InvalidOperationException>(() => CliVersion.Format(null, revision));
        var attributes = typeof(CliApplication).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var product = attributes.Single(a => a.Key == "SvtProductVersion").Value;
        Assert.IsFalse(string.IsNullOrWhiteSpace(product));
        var commit = attributes.SingleOrDefault(a => a.Key == "SvtSourceRevision")?.Value;
        using var w = new Workspace(); // No repository or inputs are needed at runtime.
        var version = await w.Process("--version");
        Assert.AreEqual(0, version.ExitCode);
        Assert.AreEqual("", version.Error);
        Assert.AreEqual(CliVersion.Format(product, commit), version.Output);
    }
}
