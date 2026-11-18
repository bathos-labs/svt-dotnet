using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Bathos.Graphics.SvgTemplates.Cli.Tests;

/// <summary>Observable CLI protocol, lifecycle and process integration.</summary>
[TestClass]
public sealed class CliTests
{
    internal const string TemplateText = "<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='0'><text data-svt-text='t'>original</text><image data-svt-image='i'/></svg>";
    internal const string EmptyBindings = "{\"format_version\":0,\"bindings\":[]}";

    /// <summary>Portable conformance executes through the real command entry point.</summary>
    [TestMethod]
    public async Task InspectionFixturesThroughProcess()
    {
        using var workspace = new Workspace();
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inspection");
        foreach (var folder in Directory.GetDirectories(root))
        {
            var input = Path.Combine(folder, File.Exists(Path.Combine(folder, "input.svg")) ? "input.svg" : "input.bin");
            var args = new List<string> { "inspect", input, "--format", "json", "--diagnostics", "json" };
            if (File.Exists(Path.Combine(folder, "options.json")))
            {
                var inspection = new JsonObject();
                var options = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "options.json")))?.AsObject();
                Assert.IsNotNull(options);
                foreach (var option in options)
                    if (option.Key != "format_version") inspection[JsonNamingPolicy.SnakeCaseLower.ConvertName(option.Key)] = option.Value?.DeepClone();
                var config = workspace.Write("configuration.json", new JsonObject { ["format_version"] = 0, ["inspection"] = inspection }.ToJsonString());
                args.AddRange(["--configuration", config]);
            }
            var result = await workspace.Process(args.ToArray());
            var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "expected.json")));
            Assert.IsNotNull(expected);
            var report = JsonNode.Parse(result.Output);
            Assert.IsNotNull(report);
            Assert.IsTrue(JsonNode.DeepEquals(expected["inputs"], report["inputs"]), folder);
            Assert.IsTrue(JsonNode.DeepEquals(expected["template_available"], report["template_available"]), folder);
            CheckDiagnostics(expected["diagnostics"], result.Diagnostics);
            var expectedExit = expected["diagnostics"]?.AsArray().Any(d => (string?)d?["severity"] == "error") == true ? 1 : 0;
            Assert.AreEqual(expectedExit, result.ExitCode, folder);
        }
    }

    /// <summary>Binding fixtures reuse typed requests and preserve semantic XML output.</summary>
    [TestMethod]
    public async Task SubstitutionFixturesThroughProcess()
    {
        using var workspace = new Workspace();
        foreach (var folder in Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Fixtures", "substitution")))
        {
            var result = await workspace.Process("bind", Path.Combine(folder, "input.svg"), "--bindings", Path.Combine(folder, "bindings.json"),
                "--output", "-", "--diagnostics", "json");
            var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "expected.json")));
            Assert.IsNotNull(expected);
            CheckDiagnostics(expected["diagnostics"], result.Diagnostics);
            if ((bool?)expected["document_available"] == true)
            {
                Assert.AreEqual(0, result.ExitCode);
                Assert.IsTrue(XNode.DeepEquals(XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "output.svg")), LoadOptions.PreserveWhitespace),
                    XDocument.Parse(result.Output, LoadOptions.PreserveWhitespace)), folder);
            }
            else { Assert.AreEqual(1, result.ExitCode); Assert.AreEqual("", result.Output); }
        }
    }
    private static void CheckDiagnostics(JsonNode? expected, JsonArray actual)
    {
        Assert.IsNotNull(expected);
        var array = expected.AsArray();
        Assert.AreEqual(array.Count, actual.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var expectedEntry = array[i];
            Assert.IsNotNull(expectedEntry);
            foreach (var field in expectedEntry.AsObject())
                Assert.IsTrue(JsonNode.DeepEquals(field.Value, actual[i]?[field.Key]), field.Key);
        }
    }

    /// <summary>Help/version do not read input; invalid invocations have distinct exit status.</summary>
    [TestMethod]
    public async Task InvocationAndHelp()
    {
        using var w = new Workspace();
        foreach (var args in new string[][] { [], ["render"], ["inspect"], ["bind", "template.svg"],
            ["inspect", "template.svg", "extra"], ["inspect", "template.svg", "--format", "JSON"],
            ["inspect", "template.svg", "--format", "json", "--format", "text"],
            ["inspect", "template.svg", "--overwrite"], ["inspect", "template.svg", "--configuration", "-"],
            ["bind", "-", "--bindings", "-", "--output", "-"],
            ["bind", "template.svg", "--bindings", "bindings.json", "--output", "-", "--overwrite"],
            ["inspect", "template.svg", "--format"], ["inspect", "template.svg", "--unknown-keys", "error"] })
        {
            var result = await w.Run(args.Concat(["--diagnostics", "json"]).ToArray());
            Assert.AreEqual(2, result.ExitCode, string.Join(' ', args));
            Assert.AreEqual("svt.cli.usage", result.Code);
            Assert.AreEqual("", result.Output);
        }
        foreach (var args in new string[][] { ["--help"], ["--version"], ["inspect", "--help"], ["bind", "--help"] })
        {
            var result = await w.Process(args);
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreNotEqual("", result.Output);
            Assert.AreEqual("", result.Error);
        }
        w.Write("-template.svg", TemplateText);
        var escaped = await w.Process("inspect", "--format", "json", "--", "-template.svg");
        Assert.AreEqual(0, escaped.ExitCode);
        Assert.IsNotNull(JsonNode.Parse(escaped.Output));
    }

    /// <summary>Strict transport rejects malformed shape independently of binder admission.</summary>
    [TestMethod]
    [DataRow("{\"format_version\":0,\"bindings\":[],\"bindings\":[]}")]
    [DataRow("{\"format_version\":0,\"bindin\\u0067s\":[],\"bindings\":[]}")]
    [DataRow("{\"format_version\":0,\"bindings\":[],\"typo\":true}")]
    [DataRow("{\"format_version\":1,\"bindings\":[]}")]
    [DataRow("{\"format_version\":0.0,\"bindings\":[]}")]
    [DataRow("{\"format_version\":0,\"bindings\":[],}")]
    [DataRow("{\"format_version\":0,/*comment*/\"bindings\":[]}")]
    [DataRow("{\"format_version\":0,\"bindings\":null}")]
    [DataRow("[]")]
    [DataRow("{\"format_version\":0,\"bindings\":[{\"key\":\"t\",\"kind\":\"text\",\"value\":null}]}")]
    [DataRow("{\"format_version\":0,\"bindings\":[{\"key\":\"t\",\"kind\":\"Text\",\"value\":\"a\"}]}")]
    [DataRow("{\"format_version\":0,\"bindings\":[{\"key\":\"i\",\"kind\":\"image_content\",\"media_type\":\"image/png\",\"file\":\"a\",\"value\":\"\"}]}")]
    [DataRow("{\"format_version\":0,\"bindings\":[{\"key\":\"t\",\"kind\":\"text\",\"value\":\"a\",\"media_type\":\"image/png\"}]}")]
    public async Task StrictBindingJson(string json)
    {
        using var w = new Workspace();
        w.Write("bindings.json", json);
        var result = await w.Bind();
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("svt.cli.json", result.Code);
        Assert.AreEqual("", result.Output);
    }

    /// <summary>Malformed Unicode fails before inspection, including ignored and duplicate assignments.</summary>
    [TestMethod]
    public async Task UnicodeAndJsonPositions()
    {
        using var w = new Workspace();
        foreach (var value in new[] { "\\ud800", "\\udfff", "\\ud800x", "\\ud800\\ud800", "\\udfff\\ud800" })
        {
            w.Write("template.svg", "malformed xml");
            w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"missing\",\"kind\":\"text\",\"value\":\"" + value + "\"}]}");
            var result = await w.Bind("--unknown-keys", "ignore-with-warning");
            Assert.AreEqual("svt.cli.json", result.Code);
            Assert.AreEqual("input", (string?)result.Diagnostics[0]?["phase"]);
            Assert.IsNotNull(result.Diagnostics[0]?["json_position"]);
        }
        w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[],\"\\ud800\":0}");
        Assert.AreEqual("svt.cli.json", (await w.Bind()).Code);
        w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"t\",\"kind\":\"text\",\"value\":\"ok\"},{\"key\":\"t\",\"kind\":\"text\",\"value\":\"\\udfff\"}]}");
        Assert.AreEqual("svt.cli.json", (await w.Bind()).Code);
        w.Write("template.svg", TemplateText);
        w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"t\",\"kind\":\"text\",\"value\":\"\\ud83d\\ude00\"}]}");
        var valid = await w.Bind();
        Assert.AreEqual(0, valid.ExitCode);
        Assert.AreEqual("😀", XDocument.Parse(valid.Output).Root?.Elements().First().Value);
        w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"t\",\"kind\":\"text\",\"value\":\"\\u0000\"}]}");
        Assert.AreEqual("svt.binding.value", (await w.Bind()).Code);
        w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"missing\",\"kind\":\"text\",\"value\":\"\\u0000\"}]}");
        Assert.AreEqual(0, (await w.Bind("--unknown-keys", "ignore-with-warning")).ExitCode);
        w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"missing\",\"kind\":\"text\",\"value\":\"\"},{\"key\":\"missing\",\"kind\":\"text\",\"value\":\"\"}]}");
        Assert.AreEqual("svt.binding.duplicate", (await w.Bind()).Code);
        var malformed = Encoding.UTF8.GetBytes("{\"é\":0,\"é\":1}");
        await File.WriteAllBytesAsync(w.PathOf("bindings.json"), [0xef, 0xbb, 0xbf, .. malformed]);
        var positions = (await w.Bind()).Diagnostics[0]?["json_position"];
        Assert.AreEqual(1L, (long?)positions?["line"]);
        Assert.AreEqual(9L, (long?)positions?["byte_column"]);
        await File.WriteAllBytesAsync(w.PathOf("bindings.json"), [0xc3, 0x28]);
        Assert.AreEqual("svt.cli.json", (await w.Bind()).Code);
    }

    /// <summary>Default inspection policy fails closed and opt-in recovery preserves original error severity.</summary>
    [TestMethod]
    public async Task InspectionPoliciesAndDiagnosticSources()
    {
        using var w = new Workspace();
        w.Write("template.svg", TemplateText.Replace("</svg>", "<text data-svt-text='bad'/><text data-svt-text='bad'/></svg>", StringComparison.Ordinal));
        w.Bindings(new { key = "t", kind = "text", value = "new" });
        var strict = await w.Bind();
        Assert.AreEqual(1, strict.ExitCode);
        Assert.AreEqual("", strict.Output);
        var recover = await w.Bind("--inspection-errors", "allow-scoped");
        Assert.AreEqual(0, recover.ExitCode);
        Assert.AreEqual("error", (string?)recover.Diagnostics[0]?["severity"]);
        Assert.IsTrue(recover.Output.Contains("new", StringComparison.Ordinal));
        var inspect = await w.Run("inspect", "template.svg", "--format", "json", "--diagnostics", "json");
        Assert.AreEqual(1, inspect.ExitCode);
        Assert.IsTrue((bool?)JsonNode.Parse(inspect.Output)?["template_available"]);
        w.Bindings(new { key = "t", kind = "paint", value = "red" });
        var wrong = await w.Bind("--inspection-errors", "allow-scoped");
        Assert.AreEqual(1, wrong.ExitCode);
        var diagnostic = wrong.Diagnostics.Last();
        Assert.AreEqual("svt.binding.kind", (string?)diagnostic?["code"]);
        Assert.AreEqual("template.svg", (string?)diagnostic?["source"]);
        Assert.AreEqual("bindings.json", (string?)diagnostic?["request_source"]);
        Assert.AreEqual("/bindings/0", (string?)diagnostic?["json_pointer"]);
        Assert.IsNotNull(diagnostic?["location"]);
        w.Write("template.svg", "<bad/>");
        Assert.AreEqual(1, (await w.Bind("--inspection-errors", "allow-scoped")).ExitCode);
    }

    /// <summary>Configuration is strict, validated in full, and overridden only after successful validation.</summary>
    [TestMethod]
    public async Task ConfigurationPrecedence()
    {
        using var w = new Workspace();
        w.Bindings(new { key = "missing", kind = "text", value = "unused" });
        w.Write("config.json", "{\"format_version\":0,\"binding\":{\"unknown_key_behavior\":\"ignore_with_warning\"}}");
        Assert.AreEqual(0, (await w.Bind("--configuration", "config.json")).ExitCode);
        Assert.AreEqual(1, (await w.Bind("--configuration", "config.json", "--unknown-keys", "error")).ExitCode);
        foreach (var json in new[] { "{\"format_version\":0,\"binding\":{\"unknown_key_behavior\":\"invalid\"}}",
            "{\"format_version\":0,\"image\":{\"max_encoded_image_bytes\":0}}", "{\"format_version\":0,\"cli\":null}",
            "{\"format_version\":0,\"image\":{\"max_media_type_characters\":2147483648}}",
            "{\"format_version\":0,\"inspection\":{\"max_hooks\":1.5}}", "{\"format_version\":0,\"image\":{\"typo\":1}}",
            "{\"format_version\":0,\"cli\":{\"inspection_errors\":\"\\ud800\"}}" })
        {
            w.Write("config.json", json);
            var result = await w.Run("inspect", "template.svg", "--configuration", "config.json", "--diagnostics", "json");
            Assert.AreEqual(2, result.ExitCode);
            Assert.AreEqual("svt.cli.configuration", result.Code);
            Assert.AreEqual(2, (await w.Bind("--configuration", "config.json", "--unknown-keys", "error")).ExitCode);
        }
    }

    /// <summary>Encoded file acquisition is relative to the request, explicit, and never skipped for ignored keys.</summary>
    [TestMethod]
    public async Task ImageLifecycleAndPaths()
    {
        using var w = new Workspace();
        Directory.CreateDirectory(w.PathOf("values"));
        await File.WriteAllBytesAsync(w.PathOf("values/picture.bin"), [0, 1, 2]);
        w.Write("values/bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"i\",\"kind\":\"image_content\",\"file\":\"picture.bin\",\"media_type\":\"IMAGE/PNG\"}]}");
        var result = await w.Process("bind", "template.svg", "--bindings", "values/bindings.json", "--output", "-", "--diagnostics", "json");
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.Output.Contains("data:image/png;base64,AAEC", StringComparison.Ordinal));
        w.Bindings(new { key = "missing", kind = "image_content", file = "missing.png", media_type = "image/png" });
        Assert.AreEqual(3, (await w.Bind("--unknown-keys", "ignore-with-warning")).ExitCode);
        w.Bindings(new { key = "missing", kind = "image_content", value = "", media_type = "text/plain" });
        var invalid = await w.Bind("--unknown-keys", "ignore-with-warning");
        Assert.AreEqual("svt.image.media-type", invalid.Code);
        Assert.IsNull(invalid.Diagnostics[0]?["key"]);
        Assert.AreEqual(0, (int?)invalid.Diagnostics[0]?["assignment_index"]);
        foreach (var value in new[] { "A", "AAE", "AAE=\n", "_AE=", "data:image/png;base64,AAE=", "AA=A" })
        {
            w.Bindings(new { key = "i", kind = "image_content", value, media_type = "image/png" });
            var malformed = await w.Bind();
            Assert.AreEqual("svt.cli.base64", malformed.Code, value);
            Assert.AreEqual("bindings.json", (string?)malformed.Diagnostics[0]?["source"]);
            Assert.AreEqual("/bindings/0/value", (string?)malformed.Diagnostics[0]?["json_pointer"]);
        }
        w.Bindings(new { key = "i", kind = "image_reference", value = "missing/unread.svg?x=1" });
        Assert.AreEqual(0, (await w.Bind()).ExitCode);
        w.Bindings(new { key = "i", kind = "image_content", value = "", media_type = "image/png" });
        Assert.IsTrue((await w.Bind()).Output.Contains("data:image/png;base64,", StringComparison.Ordinal));
    }

    /// <summary>Only one input owns stdin, raw XML encoding is preserved through discovery, and output stays separate.</summary>
    [TestMethod]
    public async Task StdinAndOutputChannels()
    {
        using var w = new Workspace();
        var inspect = await w.RunWithInput(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(TemplateText)).ToArray(),
            "inspect", "-", "--format", "json", "--diagnostics", "json");
        Assert.AreEqual(0, inspect.ExitCode);
        Assert.AreEqual(0, inspect.Diagnostics.Count);
        var bind = await w.RunWithInput(Encoding.UTF8.GetBytes(EmptyBindings), "bind", "template.svg", "--bindings", "-", "--output", "-", "--diagnostics", "json");
        Assert.AreEqual(0, bind.ExitCode);
        Assert.IsFalse(bind.Output.StartsWith("<?xml", StringComparison.Ordinal));
        Assert.DoesNotEndWith("\n", bind.Output);
        Assert.AreEqual(0, (await w.ProcessWithInput(TemplateText, "bind", "-", "--bindings", "bindings.json", "--output", "-", "--diagnostics", "json")).ExitCode);
        var toFile = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "result.svg", "--diagnostics", "json");
        Assert.AreEqual(0, toFile.ExitCode);
        Assert.AreEqual("", toFile.Output);
        var bytes = await File.ReadAllBytesAsync(w.PathOf("result.svg"));
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        var text = await w.Run("inspect", "template.svg");
        Assert.AreEqual("", text.Error);
        Assert.IsTrue(text.Output.Contains("original", StringComparison.Ordinal));
    }

    /// <summary>Text presentation escapes authored control characters and JSON output preserves their values.</summary>
    [TestMethod]
    public async Task HumanReportsAndRepeatedCalls()
    {
        using var w = new Workspace();
        w.Write("template.svg", TemplateText.Replace("data-svt-text='t'", "data-svt-text='line&#10;key' data-svt-label='line&#10;label'", StringComparison.Ordinal));
        var text = await w.Run("inspect", "template.svg");
        Assert.IsFalse(text.Output.Contains("line\nkey", StringComparison.Ordinal));
        Assert.IsTrue(text.Output.Contains("line\\nkey", StringComparison.Ordinal));
        var json = await w.Run("inspect", "template.svg", "--format", "json");
        Assert.AreEqual("line\nkey", (string?)JsonNode.Parse(json.Output)?["inputs"]?[0]?["key"]);
        w.Bindings(new { key = "line\nkey", kind = "text", value = "new<&" });
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => w.Bind()));
        Assert.IsTrue(results.All(r => r.ExitCode == 0 && r.Output == results[0].Output));
        Assert.IsTrue((await File.ReadAllTextAsync(w.PathOf("template.svg"))).Contains("original", StringComparison.Ordinal));
    }

    /// <summary>Stdin binding files resolve explicit image paths against the working directory.</summary>
    [TestMethod]
    public async Task StdinImagesAndEscapedKeys()
    {
        using var w = new Workspace();
        await File.WriteAllBytesAsync(w.PathOf("picture.bin"), [1, 2, 3]);
        var input = Encoding.UTF8.GetBytes("{\"format_version\":0,\"bindings\":[{\"key\":\"i\",\"kind\":\"image_content\",\"file\":\"picture.bin\",\"media_type\":\"image/png\"}]}");
        var result = await w.RunWithInput(input, "bind", "template.svg", "--bindings", "-", "--output", "-", "--diagnostics", "json");
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.Output.Contains("AQID", StringComparison.Ordinal));
        w.Bindings(new { key = "t", kind = "text", value = "" });
        Assert.AreEqual("", XDocument.Parse((await w.Bind()).Output).Root?.Elements().First().Value);
        w.Bindings(new { key = "", kind = "text", value = "" });
        Assert.AreEqual("svt.binding.key", (await w.Bind()).Code);
        w.Write("bindings.json", "{\"format_version\":0,\"bindings\":[{\"key\":\"t\",\"kind\":\"text\",\"value\":null}]}");
        var invalid = await w.Bind();
        Assert.AreEqual(0, (int?)invalid.Diagnostics[0]?["assignment_index"]);
        Assert.AreEqual("/bindings/0/value", (string?)invalid.Diagnostics[0]?["json_pointer"]);
    }

    /// <summary>Every exposed configuration option maps to the agreed library setting.</summary>
    [TestMethod]
    public void ConfigurationMapsAllBudgets()
    {
        var values = new Dictionary<string, object?> { ["format_version"] = 0 };
        var groups = new[] { ("inspection", typeof(InspectionOptions)), ("binding", typeof(BindingOptions)), ("image", typeof(EncodedImageOptions)) };
        foreach (var (name, type) in groups)
        {
            var entries = new Dictionary<string, object>();
            foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(int)))
                entries[JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)] = 37;
            values[name] = entries;
        }
        using var input = new JsonInput(JsonSerializer.SerializeToUtf8Bytes(values), "configuration.json", true, CancellationToken.None);
        var config = CliConfiguration.Parse(input);
        foreach (var obj in new object[] { config.Inspection, config.Binding.BindingOptions, config.Binding.ImageOptions })
            foreach (var property in obj.GetType().GetProperties().Where(p => p.PropertyType == typeof(int))) Assert.AreEqual(37, property.GetValue(obj));
    }
}

internal sealed record RunResult(int ExitCode, string Output, string Error)
{
    internal JsonArray Diagnostics
    {
        get
        {
            var envelope = JsonNode.Parse(Error);
            Assert.IsNotNull(envelope);
            Assert.AreEqual(0, (int?)envelope["format_version"]);
            Assert.AreEqual(ExitCode, (int?)envelope["exit_code"]);
            return envelope["diagnostics"]?.AsArray() ?? throw new AssertFailedException("Missing diagnostic envelope: " + Error);
        }
    }
    internal string? Code => (string?)Diagnostics.FirstOrDefault()?["code"];
}

internal sealed class Workspace : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "svt-cli-test-" + Guid.NewGuid().ToString("N"));
    internal Workspace()
    {
        Directory.CreateDirectory(Root);
        Write("template.svg", CliTests.TemplateText);
        Write("bindings.json", CliTests.EmptyBindings);
    }
    internal string PathOf(string name) => Path.Combine(Root, name);
    internal string Write(string name, string content)
    {
        var path = PathOf(name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
    internal void Bindings(params object[] entries) => Write("bindings.json", JsonSerializer.Serialize(new { format_version = 0, bindings = entries }));
    internal Task<RunResult> Bind(params string[] more) => Run(["bind", "template.svg", "--bindings", "bindings.json", "--output", "-", "--diagnostics", "json", .. more]);
    internal Task<RunResult> Run(params string[] args) => RunWithInput([], args);
    internal async Task<RunResult> RunWithInput(byte[] input, params string[] args)
    {
        using var stdin = new MemoryStream(input);
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var code = await CliApplication.RunAsync(args, stdin, stdout, stderr, Root);
        return new(code, Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
    }
    internal Task<RunResult> Process(params string[] args) => ProcessWithInput("", args);
    internal async Task<RunResult> ProcessWithInput(string input, params string[] args)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new AssertFailedException("Process did not start.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
            var error = process.StandardError.ReadToEndAsync(cancellation.Token);
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellation.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellation.Token);
            return new(process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public void Dispose() => Directory.Delete(Root, true);
}
