using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ScalableVectorTemplates.Cli.Tests;

/// <summary>Handwritten JSON defaults preserve strict validation and the ordinary CLI lifecycle.</summary>
[TestClass]
public sealed class JsonDefaultsTests
{
    /// <summary>All omission/explicit combinations preserve authored values through file and stdin acquisition.</summary>
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"format_version\":0}")]
    [DataRow("{\"bindings\":[]}")]
    [DataRow("{\"format_version\":0,\"bindings\":[]}")]
    public async Task NoAssignmentsThroughProcess(string json)
    {
        using var w = new Workspace();
        var source = "<?xml version='1.0'?>" + CliTests.TemplateText;
        w.Write("template.svg", source);
        w.Write("bindings.json", json);
        w.Write("config.json", "{}");
        foreach (var stdin in new[] { false, true })
        {
            var result = await w.ProcessWithInput(stdin ? json : "", "bind", "template.svg",
                "--bindings", stdin ? "-" : "bindings.json", "--output", "-", "--configuration", "config.json", "--diagnostics", "json");
            Assert.AreEqual(0, result.ExitCode);
            Assert.IsEmpty(result.Diagnostics);
            Assert.IsTrue(XNode.DeepEquals(XDocument.Parse(source).Root, XDocument.Parse(result.Output).Root));
            Assert.IsFalse(result.Output.StartsWith("<?xml", StringComparison.Ordinal), "No-assignment requests must still serialize.");
        }
    }

    /// <summary>A versionless useful request replaces content, including an explicit empty string.</summary>
    [TestMethod]
    [DataRow("Hello")]
    [DataRow("")]
    public async Task MinimumUsefulRequest(string value)
    {
        using var w = new Workspace();
        var json = new JsonObject { ["bindings"] = new JsonArray(new JsonObject { ["key"] = "t", ["kind"] = "text", ["value"] = value }) }.ToJsonString();
        var result = await w.ProcessWithInput(json, "bind", "template.svg", "--bindings", "-", "--output", "-", "--diagnostics", "json");
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsEmpty(result.Diagnostics);
        Assert.AreEqual(value, XDocument.Parse(result.Output).Root?.Elements().First().Value);
        Assert.AreEqual(CliTests.TemplateText, File.ReadAllText(w.PathOf("template.svg")));
    }

    /// <summary>Omission does not relax explicit-marker spelling, type, category or source attribution.</summary>
    [TestMethod]
    [DataRow("1")]
    [DataRow("-0")]
    [DataRow("0.0")]
    [DataRow("0e0")]
    [DataRow("\"0\"")]
    [DataRow("null")]
    [DataRow("false")]
    [DataRow("[]")]
    [DataRow("{}")]
    public async Task ExplicitInvalidVersions(string version)
    {
        using var w = new Workspace();
        w.Write("template.svg", "malformed XML");
        var json = "{\"format_version\":" + version + "}";
        w.Write("bindings.json", json);
        var request = await w.Bind();
        Failure(request, 1, "svt.cli.json", "/format_version");
        Assert.AreEqual("bindings.json", (string?)request.Diagnostics[0]?["request_source"]);
        w.Write("config.json", json);
        var configuration = await w.Run("inspect", "template.svg", "--configuration", "config.json", "--diagnostics", "json");
        Failure(configuration, 2, "svt.cli.configuration", "/format_version");
        Assert.AreEqual("config.json", (string?)configuration.Diagnostics[0]?["request_source"]);
    }

    /// <summary>Optional root fields cannot conceal wrong types, misspellings or incomplete assignments.</summary>
    [TestMethod]
    [DataRow("{\"bindings\":null}", "/bindings")]
    [DataRow("{\"bindings\":{}}", "/bindings")]
    [DataRow("{\"bindings\":\"\"}", "/bindings")]
    [DataRow("{\"bindings\":0}", "/bindings")]
    [DataRow("{\"bindings\":false}", "/bindings")]
    [DataRow("{\"binding\":[]}", "/binding")]
    [DataRow("{\"Bindings\":[]}", "/Bindings")]
    [DataRow("{\"format_versoin\":0}", "/format_versoin")]
    [DataRow("{\"bindings\":[null]}", "/bindings/0")]
    [DataRow("{\"bindings\":[{}]}", "/bindings/0/key")]
    [DataRow("{\"bindings\":[{\"key\":\"t\"}]}", "/bindings/0/kind")]
    [DataRow("{\"bindings\":[{\"key\":\"t\",\"kind\":\"text\"}]}", "/bindings/0/value")]
    public async Task InvalidVersionlessBindings(string json, string jsonPointer)
    {
        using var w = new Workspace();
        w.Write("bindings.json", json);
        Failure(await w.Bind(), 1, "svt.cli.json", jsonPointer);
    }

    /// <summary>Root objects, decoded property uniqueness and Unicode remain mandatory without a marker.</summary>
    [TestMethod]
    public async Task StructuralValidationStillApplies()
    {
        using var w = new Workspace();
        foreach (var json in new[] { "null", "[]", "true", "{\"format_version\":0,\"format_versi\\u006fn\":0}", "{\"\\ud800\":0}" })
        {
            w.Write("bindings.json", json);
            Failure(await w.Bind(), 1, "svt.cli.json");
            w.Write("config.json", json);
            Failure(await w.Run("inspect", "template.svg", "--configuration", "config.json", "--diagnostics", "json"), 2, "svt.cli.configuration");
        }
        w.Write("bindings.json", "{\"bindings\":[],\"bindin\\u0067s\":[]}");
        Failure(await w.Bind(), 1, "svt.cli.json");
        foreach (var json in new[] { "{\"binding\":null}", "{\"binding\":{\"unknown_key_behavior\":null}}", "{\"binding\":{\"typo\":1}}", "{\"cli\":{},\"cli\":{}}" })
        {
            w.Write("config.json", json);
            Failure(await w.Bind("--configuration", "config.json", "--unknown-keys", "error"), 2, "svt.cli.configuration");
        }
    }

    /// <summary>Empty configurations use defaults; versionless groups and flag precedence still apply.</summary>
    [TestMethod]
    public async Task ConfigurationDefaultsAndOverrides()
    {
        using var w = new Workspace();
        var original = await w.Run("inspect", "template.svg", "--format", "json", "--diagnostics", "json");
        foreach (var json in new[] { "{}", "{\"format_version\":0}", "{\"inspection\":{},\"binding\":{},\"image\":{},\"cli\":{}}" })
        {
            w.Write("config.json", json);
            var configured = await w.Process("inspect", "template.svg", "--configuration", "config.json", "--format", "json", "--diagnostics", "json");
            Assert.AreEqual(0, configured.ExitCode);
            Assert.AreEqual(original.Output, configured.Output);
            Assert.IsEmpty(configured.Diagnostics);
            Assert.AreEqual(0, (int?)JsonNode.Parse(configured.Output)?["format_version"]);
        }
        w.Write("config.json", "{\"binding\":{\"unknown_key_behavior\":\"ignore_with_warning\"}}");
        w.Write("bindings.json", "{\"bindings\":[{\"key\":\"missing\",\"kind\":\"text\",\"value\":\"unused\"}]}");
        Assert.AreEqual(0, (await w.Bind("--configuration", "config.json")).ExitCode);
        Failure(await w.Bind("--configuration", "config.json", "--unknown-keys", "error"), 1, "svt.binding.unknown");
    }

    /// <summary>Omitted CLI options preserve the published failure policy and request budget.</summary>
    [TestMethod]
    public void PublishedCliDefaults()
    {
        AssertDefaults(CliConfiguration.Default);
        foreach (var json in new[] { "{}", "{\"format_version\":0}", "{\"cli\":{}}" })
        {
            using var input = new JsonInput(Encoding.UTF8.GetBytes(json), "config.json", true, CancellationToken.None);
            AssertDefaults(CliConfiguration.Parse(input));
        }

        static void AssertDefaults(CliConfiguration configuration)
        {
            Assert.IsFalse(configuration.AllowScoped);
            Assert.AreEqual(67_108_864, configuration.MaxBindingsFileBytes);
            Assert.AreEqual(UnknownKeyBehavior.Error, configuration.Binding.BindingOptions.UnknownKeyBehavior);
        }
    }

    /// <summary>No-assignment requests still enforce inspection policy and acquisition/output budgets.</summary>
    [TestMethod]
    public async Task EmptyRequestPoliciesAndBudgets()
    {
        using var w = new Workspace();
        w.Write("bindings.json", "{}");
        w.Write("template.svg", CliTests.TemplateText.Replace("</svg>", "<text data-svt-text='bad'/><image data-svt-image='bad'/></svg>", StringComparison.Ordinal));
        Assert.AreEqual(1, (await w.Bind()).ExitCode);
        w.Write("config.json", "{\"cli\":{\"inspection_errors\":\"allow_scoped\"}}");
        var recovered = await w.Bind("--configuration", "config.json");
        Assert.AreEqual(0, recovered.ExitCode);
        Assert.IsNotEmpty(recovered.Diagnostics);
        Assert.AreEqual(1, (await w.Bind("--configuration", "config.json", "--inspection-errors", "fail")).ExitCode);
        w.Write("template.svg", "<bad/>");
        Assert.AreEqual(1, (await w.Bind("--configuration", "config.json")).ExitCode);
        w.Write("template.svg", CliTests.TemplateText);
        foreach (var (json, code, limit, phase) in new[]
        {
            ("{\"inspection\":{\"max_source_bytes\":1}}", "svt.cli.limit", "MaxSourceBytes", "input"),
            ("{\"binding\":{\"max_output_bytes\":1}}", "svt.limit.exceeded", "MaxOutputBytes", "binding"),
            ("{\"cli\":{\"max_bindings_file_bytes\":1}}", "svt.cli.limit", "cli.max_bindings_file_bytes", "input")
        })
        {
            w.Write("config.json", json);
            var limited = await w.Bind("--configuration", "config.json");
            Failure(limited, 1, code);
            Assert.AreEqual(limit, (string?)limited.Diagnostics[0]?["limit"]);
            Assert.AreEqual(phase, (string?)limited.Diagnostics[0]?["phase"]);
            w.Write("result.svg", "retain existing output");
            var file = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--configuration", "config.json",
                "--output", "result.svg", "--overwrite", "--diagnostics", "json");
            Failure(file, 1, code);
            Assert.AreEqual("retain existing output", File.ReadAllText(w.PathOf("result.svg")));
        }
        w.Write("config.json", "{\"cli\":{\"max_bindings_file_bytes\":2}}");
        Assert.AreEqual(0, (await w.Bind("--configuration", "config.json")).ExitCode);
    }

    /// <summary>An omitted bindings array never bypasses publication refusal or explicit operand requirements.</summary>
    [TestMethod]
    public async Task EmptyRequestPublication()
    {
        using var w = new Workspace();
        w.Write("bindings.json", "{}");
        w.Write("result.svg", "existing destination");
        Failure(await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "result.svg", "--diagnostics", "json"), 3, "svt.cli.output-exists");
        Assert.AreEqual("existing destination", File.ReadAllText(w.PathOf("result.svg")));
        w.Write("config.json", "{}");
        foreach (var consumed in new[] { "template.svg", "bindings.json", "config.json" })
        {
            var original = File.ReadAllBytes(w.PathOf(consumed));
            Failure(await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--configuration", "config.json",
                "--output", consumed, "--overwrite", "--diagnostics", "json"), 3, "svt.cli.output-conflict");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(w.PathOf(consumed)));
        }
        var published = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "result.svg", "--overwrite", "--diagnostics", "json");
        Assert.AreEqual(0, published.ExitCode);
        Assert.AreEqual("", published.Output);
        Assert.IsEmpty(published.Diagnostics);
        Assert.IsTrue(XNode.DeepEquals(XDocument.Parse(CliTests.TemplateText).Root, XDocument.Load(w.PathOf("result.svg")).Root));
        foreach (var args in new[] { new[] { "bind", "template.svg", "--output", "-" }, new[] { "bind", "template.svg", "--bindings", "bindings.json" } })
            Assert.AreEqual(2, (await w.Run([.. args, "--diagnostics", "json"])).ExitCode);
    }

    /// <summary>Only a supplied object enables defaults; missing content and trailing tokens still fail.</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow(" \r\n\t")]
    [DataRow("{}{}")]
    [DataRow("{} trailing")]
    [DataRow("{}/* comment */")]
    public async Task MalformedInputIsNotAnEmptyRequest(string json)
    {
        using var w = new Workspace();
        w.Write("bindings.json", json);
        Failure(await w.Bind(), 1, "svt.cli.json");
        Failure(await w.RunWithInput(Encoding.UTF8.GetBytes(json), "bind", "template.svg", "--bindings", "-",
            "--output", "-", "--diagnostics", "json"), 1, "svt.cli.json");
        w.Write("config.json", json);
        Failure(await w.Run("inspect", "template.svg", "--configuration", "config.json", "--diagnostics", "json"), 2, "svt.cli.configuration");
    }

    /// <summary>BOM removal for empty objects does not bypass file or stdin acquisition budgets.</summary>
    [TestMethod]
    public async Task EmptyObjectsWithBomRemainBounded()
    {
        using var w = new Workspace();
        byte[] bytes = [0xef, 0xbb, 0xbf, (byte)'{', (byte)'}'];
        await File.WriteAllBytesAsync(w.PathOf("bindings.json"), bytes);
        await File.WriteAllBytesAsync(w.PathOf("config.json"), bytes);
        Assert.AreEqual(0, (await w.Bind("--configuration", "config.json")).ExitCode);
        foreach (var maximum in new[] { bytes.Length, bytes.Length - 1 })
        {
            w.Write("config.json", new JsonObject { ["cli"] = new JsonObject { ["max_bindings_file_bytes"] = maximum } }.ToJsonString());
            foreach (var stdin in new[] { false, true })
            {
                var source = stdin ? "-" : "bindings.json";
                var result = await w.RunWithInput(stdin ? bytes : [], "bind", "template.svg", "--bindings", source,
                    "--configuration", "config.json", "--output", "-", "--diagnostics", "json");
                if (maximum == bytes.Length)
                {
                    Assert.AreEqual(0, result.ExitCode);
                    Assert.IsEmpty(result.Diagnostics);
                }
                else
                {
                    Failure(result, 1, "svt.cli.limit");
                    Assert.AreEqual("cli.max_bindings_file_bytes", (string?)result.Diagnostics[0]?["limit"]);
                    Assert.AreEqual(source, (string?)result.Diagnostics[0]?["request_source"]);
                }
            }
        }
    }

    private static void Failure(RunResult result, int exit, string code, string? jsonPointer = null)
    {
        Assert.AreEqual(exit, result.ExitCode);
        Assert.AreEqual(code, result.Code);
        Assert.AreEqual("", result.Output);
        if (jsonPointer is not null) Assert.AreEqual(jsonPointer, (string?)result.Diagnostics[0]?["json_pointer"]);
    }
}
