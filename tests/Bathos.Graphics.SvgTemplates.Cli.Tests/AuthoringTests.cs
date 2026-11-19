using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Bathos.Graphics.SvgTemplates.Cli.Tests;

/// <summary>Independent authoring examples exercised through the API and real CLI.</summary>
[TestClass]
public sealed class AuthoringTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Original and explicitly supplied editor round trips retain their expected inventory and replacements.</summary>
    [TestMethod]
    public async Task AuthoringExamples()
    {
        var root = Environment.GetEnvironmentVariable("SVT_AUTHORING_ROOT") ?? Path.Combine(AppContext.BaseDirectory, "Authoring");
        var suite = ReadJson(Path.Combine(root, "suite.json"));
        Assert.AreEqual(0, suite["format_version"]?.GetValue<int>());
        var cases = Strings(suite, "cases").ToArray();
        Assert.IsNotEmpty(cases, "Empty authoring suite.");
        Assert.AreEqual(cases.Length, cases.Distinct(StringComparer.Ordinal).Count(), "Duplicate cases.");
        foreach (var name in cases)
        {
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9-]*$"), "Invalid case name.");
            var folder = Path.Combine(root, name);
            var sourcePath = Path.Combine(folder, "input.svg");
            var source = await File.ReadAllBytesAsync(sourcePath);
            var original = source.ToArray();
            var result = TemplateInspector.Inspect(source);
            Assert.IsNotNull(result.Template, name);
            Assert.IsEmpty(result.Diagnostics, name);
            var inventory = ReadJson(Path.Combine(folder, "inventory.json"));
            var actual = JsonSerializer.SerializeToNode(result.Template.Inputs, JsonOptions);
            Assert.IsTrue(JsonNode.DeepEquals(inventory["inputs"], WithoutLocations(actual)), name + " API inventory");
            Assert.IsTrue(JsonNode.DeepEquals(inventory["input_groups"], Presentation(result.Template)), name + " API presentation");
            using var workspace = new Workspace();
            var discovery = await workspace.Process("inspect", sourcePath, "--format", "json", "--diagnostics", "json");
            Assert.AreEqual(0, discovery.ExitCode, discovery.Error);
            Assert.IsEmpty(discovery.Diagnostics);
            var discovered = JsonNode.Parse(discovery.Output);
            Assert.IsTrue(discovered?["template_available"]?.GetValue<bool>());
            Assert.IsTrue(JsonNode.DeepEquals(inventory["inputs"], WithoutLocations(discovered?["inputs"])), name + " CLI inventory");
            Assert.IsTrue(JsonNode.DeepEquals(inventory["input_groups"], discovered?["input_groups"]), name + " CLI presentation");
            foreach (var mode in new[] { "empty", "partial", "complete" })
            {
                var requestPath = Path.Combine(folder, mode + ".json");
                var bindings = ReadJson(requestPath)["bindings"]?.AsArray() ?? throw new AssertFailedException("Missing assignments.");
                var assignments = bindings.Select(CreateBinding).ToArray();
                var bound = result.Template.Bind(assignments);
                Assert.IsNotNull(bound.Document, name + " " + mode);
                Assert.IsEmpty(bound.Diagnostics);
                var expected = await File.ReadAllTextAsync(Path.Combine(folder, mode == "empty" ? "input.svg" : mode + ".svg"));
                AssertXml(expected, bound.Document.Xml, name + " API " + mode);
                var cli = await workspace.Process("bind", sourcePath, "--bindings", requestPath, "--output", "-", "--diagnostics", "json");
                Assert.AreEqual(0, cli.ExitCode, cli.Error);
                Assert.IsEmpty(cli.Diagnostics);
                AssertXml(expected, cli.Output, name + " CLI " + mode);
                var reinspected = TemplateInspector.Inspect(bound.Document.ToUtf8Bytes());
                Assert.IsNotNull(reinspected.Template);
                Assert.IsEmpty(reinspected.Diagnostics);
                CollectionAssert.AreEqual(result.Template.Inputs.Select(i => i.Key).ToArray(), reinspected.Template.Inputs.Select(i => i.Key).ToArray());
                Assert.IsTrue(JsonNode.DeepEquals(Presentation(result.Template), Presentation(reinspected.Template)), name + " bound presentation");
            }
            CollectionAssert.AreEqual(original, source, name + " caller buffer");
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(sourcePath), name + " source file");
        }
    }

    /// <summary>A successful inspection with no hooks is not evidence of authoring preservation.</summary>
    [TestMethod]
    public void InventoryDetectsSilentHookLoss()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Authoring", "text-card");
        var document = XDocument.Load(Path.Combine(folder, "input.svg"));
        foreach (var attribute in document.Descendants().Attributes().Where(a => a.Name.LocalName.StartsWith("data-svt-", StringComparison.Ordinal) && a.Name.LocalName != "data-svt-version").ToArray())
            attribute.Remove();
        var result = TemplateInspector.Inspect(System.Text.Encoding.UTF8.GetBytes(document.ToString()));
        Assert.IsNotNull(result.Template);
        Assert.IsEmpty(result.Diagnostics);
        Assert.IsEmpty(result.Template.Inputs);
        Assert.IsFalse(JsonNode.DeepEquals(ReadJson(Path.Combine(folder, "inventory.json"))["inputs"], JsonSerializer.SerializeToNode(result.Template.Inputs, JsonOptions)));
    }

    /// <summary>Hazards have explicit structural outcomes without adding SVG value validation.</summary>
    [TestMethod]
    public async Task AuthoringHazards()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Authoring", "hazards");
        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            var expected = ReadJson(path);
            var sourcePath = Path.ChangeExtension(path, ".svg");
            var result = TemplateInspector.Inspect(await File.ReadAllBytesAsync(sourcePath));
            Assert.AreEqual(expected["template_available"]?.GetValue<bool>(), result.Template is not null, path);
            CollectionAssert.AreEqual(Strings(expected, "codes").ToArray(), result.Diagnostics.Select(x => x.Code).ToArray(), path);
            CollectionAssert.AreEqual(Strings(expected, "keys").ToArray(), result.Template?.Inputs.Select(x => x.Key).ToArray() ?? [], path);
            using var workspace = new Workspace();
            var cli = await workspace.Process("inspect", sourcePath, "--format", "json", "--diagnostics", "json");
            Assert.AreEqual(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? 1 : 0, cli.ExitCode);
            CollectionAssert.AreEqual(result.Diagnostics.Select(x => x.Code).ToArray(), cli.Diagnostics.Select(x => (string?)x?["code"]).ToArray());
            var discovered = JsonNode.Parse(cli.Output) ?? throw new AssertFailedException("Missing CLI inventory.");
            Assert.AreEqual(expected["template_available"]?.GetValue<bool>(), discovered["template_available"]?.GetValue<bool>());
            CollectionAssert.AreEqual(Strings(expected, "keys").ToArray(), Array(discovered, "inputs").Select(x => (string?)x?["key"]).ToArray());
        }
    }

    /// <summary>Relative image references retain authored base context; relocating output is host policy.</summary>
    [TestMethod]
    public async Task RelativeReferenceBaseIsPreserved()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Authoring", "references");
        foreach (var name in new[] { "relative.svg", "xml-base.svg" })
        {
            var path = Path.Combine(folder, name);
            var source = await File.ReadAllBytesAsync(path);
            var result = TemplateInspector.Inspect(source);
            Assert.IsNotNull(result.Template);
            Assert.IsEmpty(result.Diagnostics);
            var raw = name == "relative.svg" ? "../assets/portrait.png" : "portrait.png";
            Assert.AreEqual(raw, result.Template.Inputs.Single().Default.Raw);
            var bound = result.Template.Bind([]);
            Assert.IsNotNull(bound.Document);
            Assert.IsEmpty(bound.Diagnostics);
            AssertXml(System.Text.Encoding.UTF8.GetString(source), bound.Document.Xml, name);
            using var workspace = new Workspace();
            var cli = await workspace.Process("bind", path, "--bindings", Path.Combine(folder, "empty.json"), "--output", "-", "--diagnostics", "json");
            Assert.AreEqual(0, cli.ExitCode);
            Assert.IsEmpty(cli.Diagnostics);
            AssertXml(bound.Document.Xml, cli.Output, name);
            CollectionAssert.AreEqual(source, await File.ReadAllBytesAsync(path), name + " source file");
        }
    }

    private static JsonNode? Presentation(InspectedTemplate template) => JsonSerializer.SerializeToNode(template.InputGroups.Select(group => new
    {
        group.Key,
        group.Label,
        group.Description,
        group.Order,
        InputKeys = group.Inputs.Select(input => input.Key),
    }), JsonOptions);

    private static TemplateBinding CreateBinding(JsonNode? entry)
    {
        Assert.IsNotNull(entry);
        var key = entry["key"]?.GetValue<string>() ?? throw new AssertFailedException("Missing key.");
        var value = entry["value"]?.GetValue<string>() ?? throw new AssertFailedException("Missing value.");
        switch (entry["kind"]?.GetValue<string>())
        {
            case "text": return TemplateBinding.Text(key, value);
            case "paint": return TemplateBinding.Paint(key, value);
            case "image_reference": return TemplateBinding.ImageReference(key, value);
            case "image_content":
                var mediaType = entry["media_type"]?.GetValue<string>() ?? throw new AssertFailedException("Missing media type.");
                var image = EncodedImage.Create(Convert.FromBase64String(value), mediaType);
                Assert.IsNotNull(image.Image);
                Assert.IsEmpty(image.Diagnostics);
                return TemplateBinding.ImageContent(key, image.Image);
            default: throw new AssertFailedException("Unknown fixture kind.");
        }
    }

    private static JsonArray Array(JsonNode node, string name) => node[name]?.AsArray() ?? throw new AssertFailedException("Missing array: " + name);
    private static IEnumerable<string> Strings(JsonNode node, string name) => Array(node, name).Select(x => x?.GetValue<string>() ?? throw new AssertFailedException("Missing string."));
    private static JsonNode ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path)) ?? throw new AssertFailedException("Missing JSON: " + path);
    private static JsonNode? WithoutLocations(JsonNode? value)
    {
        if (value is JsonObject obj)
        {
            obj.Remove("location");
            foreach (var child in obj.ToArray()) WithoutLocations(child.Value);
        }
        else if (value is JsonArray array) foreach (var child in array) WithoutLocations(child);
        return value;
    }
    private static void AssertXml(string expected, string actual, string context) =>
        Assert.IsTrue(XNode.DeepEquals(Canonical(XElement.Parse(expected, LoadOptions.PreserveWhitespace)), Canonical(XElement.Parse(actual, LoadOptions.PreserveWhitespace))), context);
    private static XElement Canonical(XElement element) => new(element.Name,
        element.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => new XAttribute(a)),
        element.Nodes().Where(n => n is not XText t || !string.IsNullOrWhiteSpace(t.Value) || element.Name.LocalName is "text" or "tspan").Select(n => n is XElement child ? Canonical(child) : n));
}
