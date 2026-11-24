using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScalableVectorTemplates.Cli.Tests;

/// <summary>Presentation at actual request, filesystem and inspection boundaries.</summary>
[TestClass]
public sealed class PresentationBoundaryTests
{
    /// <summary>An image file named dash is a file even when the binding request comes from stdin.</summary>
    [TestMethod]
    public async Task ImageFileNamedDash()
    {
        using var w = new Workspace();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { bindings = new[] { new { key = "i", kind = "image_content", file = "-", media_type = "image/png" } } });
        string[] args = ["bind", "template.svg", "--bindings", "-", "--output", "-"];
        var missing = await w.RunWithInput(bytes, args);
        Assert.AreEqual(3, missing.ExitCode);
        Assert.AreEqual("", missing.Output);
        StringAssert.Contains(missing.Error, "Source: \"./-\"");
        StringAssert.Contains(missing.Error, "Request: stdin");
        StringAssert.Contains(missing.Error, "/bindings/0/file");
        var json = await w.RunWithInput(bytes, [.. args, "--diagnostics", "json"]);
        Assert.AreEqual("./-", (string?)json.Diagnostics[0]?["source"]);
        Assert.AreEqual("-", (string?)json.Diagnostics[0]?["request_source"]);

        await File.WriteAllBytesAsync(w.PathOf("-"), [1, 2]);
        w.Write("config.json", """{"image":{"max_encoded_image_bytes":1}}""");
        var limited = await w.RunWithInput(bytes, [.. args, "--configuration", "config.json"]);
        Assert.AreEqual(1, limited.ExitCode);
        StringAssert.Contains(limited.Error, "Source: \"./-\"");
        StringAssert.Contains(limited.Error, "Limit: MaxEncodedImageBytes");
        Assert.IsFalse(limited.Error.Contains(w.Root, StringComparison.Ordinal));
        var invalidMedia = JsonSerializer.SerializeToUtf8Bytes(new { bindings = new[] { new { key = "i", kind = "image_content", file = "-", media_type = "invalid" } } });
        var prepared = await w.RunWithInput(invalidMedia, args);
        Assert.AreEqual(1, prepared.ExitCode);
        StringAssert.Contains(prepared.Error, "Source: \"./-\"");
        StringAssert.Contains(prepared.Error, "Request: stdin");
    }

    /// <summary>Output diagnostics retain the caller's filename and do not suggest overriding an input collision.</summary>
    [TestMethod]
    public async Task PublicationPathsAndRemedies()
    {
        using var w = new Workspace();
        w.Write("result.svg", "keep");
        string[] args = ["bind", "template.svg", "--bindings", "bindings.json", "--output"];
        var exists = await w.Process([.. args, "result.svg"]);
        Assert.AreEqual(3, exists.ExitCode);
        Assert.AreEqual("", exists.Output);
        StringAssert.Contains(exists.Error, "Source: \"result.svg\"");
        StringAssert.Contains(exists.Error, "--overwrite");
        Assert.IsFalse(exists.Error.Contains(w.Root, StringComparison.Ordinal));
        var json = await w.Run([.. args, "result.svg", "--diagnostics", "json"]);
        Assert.AreEqual("result.svg", (string?)json.Diagnostics[0]?["source"]);
        var conflict = await w.Run([.. args, "template.svg", "--overwrite"]);
        Assert.AreEqual(3, conflict.ExitCode);
        StringAssert.Contains(conflict.Error, "Source: \"template.svg\"");
        StringAssert.Contains(conflict.Error, "svt.cli.output-conflict");
        Assert.IsFalse(conflict.Error.Contains("--overwrite", StringComparison.Ordinal));
        Directory.CreateDirectory(w.PathOf("folder"));
        var folder = await w.Run([.. args, "folder"]);
        Assert.AreEqual(3, folder.ExitCode);
        StringAssert.Contains(folder.Error, "folder");
        StringAssert.Contains(folder.Error, "Choose a filename");
        Assert.IsFalse(folder.Error.Contains("--overwrite", StringComparison.Ordinal));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(w.PathOf("result.svg")));
        Assert.AreEqual(CliTests.TemplateText, await File.ReadAllTextAsync(w.PathOf("template.svg")));
    }

    /// <summary>Empty inventories do not hide real structural findings or suppress failure status.</summary>
    [TestMethod]
    public async Task EmptyDiscoveryWithErrors()
    {
        using var w = new Workspace();
        w.Write("template.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" data-svt-version="0">
              <text data-svt-text="heading"><tspan>Child</tspan></text>
            </svg>
            """);
        var result = await w.Process("inspect", "template.svg");
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("No editable inputs found.\n", result.Output);
        StringAssert.Contains(result.Error, "Error:");
        StringAssert.Contains(result.Error, "SVG line 2, UTF-16 column");
        var json = await w.Run("inspect", "template.svg", "--format", "json", "--diagnostics", "json");
        Assert.IsTrue((bool?)JsonNode.Parse(json.Output)?["template_available"]);
        var inputs = JsonNode.Parse(json.Output)?["inputs"]?.AsArray();
        Assert.IsNotNull(inputs);
        Assert.HasCount(0, inputs);
        Assert.HasCount(1, json.Diagnostics);
    }

    /// <summary>Real parser positions count UTF-8 bytes, exclude a BOM and remain separate from SVG coordinates.</summary>
    [TestMethod]
    public async Task JsonPositionsAndBindingLocations()
    {
        using var w = new Workspace();
        const string line = "  \"é\": 0, \"é\": 1}";
        var expectedColumn = Encoding.UTF8.GetByteCount(line[..line.LastIndexOf("\"é\"", StringComparison.Ordinal)]) + 1;
        byte[] request = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("{\n" + line)];
        string[] args = ["bind", "template.svg", "--bindings", "-", "--output", "-"];
        var text = await w.RunWithInput(request, args);
        Assert.AreEqual(1, text.ExitCode);
        Assert.AreEqual("", text.Output);
        StringAssert.Contains(text.Error, "Source: stdin");
        StringAssert.Contains(text.Error, "JSON line 2, UTF-8 byte column " + expectedColumn.ToString(CultureInfo.InvariantCulture));
        Assert.IsFalse(text.Error.Contains("SVG line", StringComparison.Ordinal));
        var json = await w.RunWithInput(request, [.. args, "--diagnostics", "json"]);
        Assert.AreEqual(expectedColumn, (int?)json.Diagnostics[0]?["json_position"]?["byte_column"]);

        w.Bindings(new { key = "t", kind = "paint", value = "red" });
        var mismatch = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "-");
        Assert.AreEqual(1, mismatch.ExitCode);
        StringAssert.Contains(mismatch.Error, "Source: \"template.svg\"");
        StringAssert.Contains(mismatch.Error, "Request: \"bindings.json\"");
        StringAssert.Contains(mismatch.Error, "SVG line 1, UTF-16 column");
        StringAssert.Contains(mismatch.Error, "binding 1");
        StringAssert.Contains(mismatch.Error, "JSON pointer: \"/bindings/0\"");
        Assert.IsFalse(mismatch.Error.Contains("JSON line", StringComparison.Ordinal));
    }

    /// <summary>Aggregate failures retain the request and exact limit without assigning blame to one entry.</summary>
    [TestMethod]
    public async Task AggregateImageLimitHasNoAssignment()
    {
        using var w = new Workspace();
        w.Write("config.json", """{"binding":{"max_total_encoded_image_bytes":3}}""");
        w.Bindings(new { key = "i", kind = "image_content", value = "AAE=", media_type = "image/png" },
            new { key = "unused", kind = "image_content", value = "AAE=", media_type = "image/png" });
        var result = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "-", "--configuration", "config.json");
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("", result.Output);
        StringAssert.Contains(result.Error, "Source: \"bindings.json\"");
        StringAssert.Contains(result.Error, "Limit: MaxTotalEncodedImageBytes");
        Assert.IsFalse(result.Error.Contains("binding 2", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.Contains("JSON pointer:", StringComparison.Ordinal));
        var json = await w.Bind("--configuration", "config.json");
        Assert.IsNull(json.Diagnostics[0]?["assignment_index"]);
        Assert.IsNull(json.Diagnostics[0]?["json_pointer"]);
    }
}
