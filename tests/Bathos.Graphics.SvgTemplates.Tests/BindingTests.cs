using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Bathos.Graphics.SvgTemplates.Tests;

/// <summary>Observable substitution contracts and resource boundaries.</summary>
[TestClass]
public sealed class BindingTests
{
    private const string Start = "<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='0'>";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
    private static InspectedTemplate Template(string content)
    {
        var result = TemplateInspector.Inspect(Encoding.UTF8.GetBytes(Start + content + "</svg>"));
        Assert.IsNotNull(result.Template);
        return result.Template;
    }
    private static BoundSvg Success(BindingResult result)
    {
        Assert.IsNotNull(result.Document, string.Join(", ", result.Diagnostics.Select(d => d.Code + ":" + d.Limit)));
        return result.Document;
    }
    private static void Failure(BindingResult result, string code, string? limit = null)
    {
        Assert.IsNull(result.Document);
        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual(code, result.Diagnostics[0].Code);
        Assert.AreEqual(limit, result.Diagnostics[0].Limit);
    }
    private static EncodedImage Image(byte[] bytes, string media = "image/png")
    {
        var result = EncodedImage.Create(bytes, media);
        Assert.IsNotNull(result.Image);
        return result.Image;
    }

    /// <summary>Preserves text placement, comments, PIs, attributes and omission.</summary>
    [TestMethod]
    public void TextPlacementAndRawValues()
    {
        var template = Template("<text data-svt-text='t'><!--before-->old<?note keep?><![CDATA[tail]]><!--after--></text>" +
            "<text data-svt-text='none'><!--only--></text><text data-svt-text='empty'/>" +
            "<g data-svt-fill='p' fill='red' data-svt-stroke='p' stroke='#ff0000'/>");
        var output = Success(template.Bind([TemplateBinding.Text("t", "new<&\r\n"), TemplateBinding.Text("none", "append"), TemplateBinding.Text("empty", "")]));
        var root = XElement.Parse(output.Xml, LoadOptions.PreserveWhitespace);
        var text = root.Elements().First();
        Assert.AreEqual("new<&\r\n", text.Value);
        CollectionAssert.AreEqual(new[] { typeof(XComment), typeof(XText), typeof(XProcessingInstruction), typeof(XComment) }, text.Nodes().Select(n => n.GetType()).ToArray());
        Assert.AreEqual("append", root.Elements().ElementAt(1).Value);
        Assert.IsInstanceOfType<XComment>(root.Elements().ElementAt(1).FirstNode);
        Assert.IsNull(root.Elements().ElementAt(2).FirstNode);
        var paint = root.Elements().Last();
        Assert.AreEqual("red", (string?)paint.Attribute("fill"));
        Assert.AreEqual("#ff0000", (string?)paint.Attribute("stroke"));
        var replaced = XElement.Parse(Success(template.Bind([TemplateBinding.Paint("p", "a\tb\rc\n<&")])).Xml);
        Assert.AreEqual("a\tb\rc\n<&", (string?)replaced.Elements().Last().Attribute("fill"));
        Assert.AreEqual("a\tb\rc\n<&", (string?)replaced.Elements().Last().Attribute("stroke"));
        var cleared = XElement.Parse(Success(template.Bind([TemplateBinding.Text("t", "")])).Xml);
        Assert.IsFalse(cleared.Elements().First().Nodes().Any(n => n is XText));
        Assert.HasCount(3, cleared.Elements().First().Nodes().ToArray());
    }

    /// <summary>Only admitted targets change, while rejected keys remain distinguishable.</summary>
    [TestMethod]
    public void ScopedAdmissionAndDiagnostics()
    {
        var template = Template("<text data-svt-text='shared'><g/></text><text data-svt-text='shared'>old</text>" +
            "<text data-svt-text='bad'/><text data-svt-text='bad'/><g data-svt-fill='p'/>");
        var root = XElement.Parse(Success(template.Bind([TemplateBinding.Text("shared", "new")])).Xml);
        Assert.IsTrue(root.Elements().First().HasElements);
        Assert.AreEqual("new", root.Elements().ElementAt(1).Value);
        var ignore = new BindingConfiguration(new() { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning });
        Failure(template.Bind([TemplateBinding.Text("bad", "new")], ignore), "svt.binding.rejected");
        Failure(template.Bind([TemplateBinding.Text("missing", "new")]), "svt.binding.unknown");
        var result = template.Bind([TemplateBinding.Text("missing", "\0"), TemplateBinding.Paint("p", "none")], ignore);
        Success(result);
        Assert.AreEqual(DiagnosticSeverity.Warning, result.Diagnostics[0].Severity);
        Assert.IsNull(result.Diagnostics[0].Location);
        Failure(template.Bind([TemplateBinding.Text("p", "\0")]), "svt.binding.kind");
        Failure(template.Bind([TemplateBinding.Paint("p", "\0")]), "svt.binding.value");
        Failure(template.Bind([TemplateBinding.Text("", "\0")]), "svt.binding.key");
        Failure(template.Bind([TemplateBinding.Text("missing", "\0"), TemplateBinding.Text("missing", "other")], ignore), "svt.binding.duplicate");
        Failure(template.Bind([TemplateBinding.Text("", ""), TemplateBinding.Text("", "")]), "svt.binding.duplicate");
        result = template.Bind([TemplateBinding.Paint("p", "\0"), TemplateBinding.Text("bad", "a"), TemplateBinding.Text("bad", "b")]);
        Assert.IsNull(result.Document);
        CollectionAssert.AreEqual(new List<string> { "svt.binding.value", "svt.binding.duplicate" }, result.Diagnostics.Select(d => d.Code).ToArray());
        Assert.IsNotNull(result.Diagnostics[1].Location);
        Assert.HasCount(1, result.Diagnostics[1].RelatedLocations);
    }

    /// <summary>Image capture owns bytes and correctly escapes and encodes data URLs.</summary>
    [TestMethod]
    public void ImageOwnershipAndReferences()
    {
        var bytes = Enumerable.Range(0, 20000).Select(i => (byte)i).ToArray();
        var original = Convert.ToBase64String(bytes);
        var image = Image(bytes, "IMAGE/X+TEST");
        Array.Fill(bytes, (byte)0);
        Assert.AreEqual("image/x+test", image.MediaType);
        var template = Template("<image xmlns:x='http://www.w3.org/1999/xlink' data-svt-image='i' href='' x:href='original'/>" +
            "<image data-svt-image='j'/>");
        var root = XElement.Parse(Success(template.Bind([TemplateBinding.ImageContent("i", image), TemplateBinding.ImageReference("j", "relative ?%\t")])).Xml);
        var element = root.Elements().First();
        var expected = "data:image/x+test;base64," + original;
        Assert.AreEqual(expected, (string?)element.Attribute("href"));
        Assert.AreEqual(expected, (string?)element.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink")));
        Assert.AreEqual("relative ?%\t", (string?)root.Elements().Last().Attribute("href"));
        Assert.IsNull(root.Elements().Last().Attribute(XName.Get("href", "http://www.w3.org/1999/xlink")));
        var empty = XElement.Parse(Success(template.Bind([TemplateBinding.ImageContent("i", Image([]))])).Xml);
        Assert.AreEqual("data:image/png;base64,", (string?)empty.Elements().First().Attribute("href"));
    }

    /// <summary>All eight limits allow equality and reject excess, including ignored assignments.</summary>
    [TestMethod]
    public void BudgetBoundaries()
    {
        var template = Template("<text data-svt-text='a'/><text data-svt-text='b'/><image data-svt-image='i'/><image data-svt-image='j'/>");
        TemplateBinding[] text = [TemplateBinding.Text("a", "xx"), TemplateBinding.Text("b", "xx")];
        Success(template.Bind(text, new(new() { MaxBindings = 2, MaxValueCharacters = 2, MaxTotalValueCharacters = 6 })));
        Failure(template.Bind(text, new(new() { MaxBindings = 1 })), "svt.limit.exceeded", "MaxBindings");
        Failure(template.Bind(text, new(new() { MaxValueCharacters = 1 })), "svt.limit.exceeded", "MaxValueCharacters");
        Failure(template.Bind(text, new(new() { MaxTotalValueCharacters = 5 })), "svt.limit.exceeded", "MaxTotalValueCharacters");
        var image = Image([1, 2]);
        TemplateBinding[] images = [TemplateBinding.ImageContent("i", image), TemplateBinding.ImageContent("j", image)];
        Success(template.Bind(images, new(new() { MaxTotalEncodedImageBytes = 4 }, new() { MaxEncodedImageBytes = 2, MaxMediaTypeCharacters = 9 })));
        Failure(template.Bind(images, new(new() { MaxTotalEncodedImageBytes = 3 })), "svt.limit.exceeded", "MaxTotalEncodedImageBytes");
        Failure(template.Bind(images, new(imageOptions: new() { MaxEncodedImageBytes = 1 })), "svt.limit.exceeded", "MaxEncodedImageBytes");
        Failure(template.Bind(images, new(imageOptions: new() { MaxMediaTypeCharacters = 8 })), "svt.limit.exceeded", "MaxMediaTypeCharacters");
        var length = Success(template.Bind(text)).ToUtf8Bytes().Length;
        Success(template.Bind(text, new(new() { MaxOutputBytes = length })));
        Failure(template.Bind(text, new(new() { MaxOutputBytes = length - 1 })), "svt.limit.exceeded", "MaxOutputBytes");
        TemplateBinding[] unknown = [TemplateBinding.Text("x", ""), TemplateBinding.Text("y", "")];
        var warnings = new BindingOptions { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning, MaxDiagnostics = 2 };
        Assert.HasCount(2, template.Bind(unknown, new(warnings)).Diagnostics);
        Failure(template.Bind(unknown, new(new() { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning, MaxDiagnostics = 1 })), "svt.limit.exceeded", "MaxDiagnostics");
        Failure(template.Bind([TemplateBinding.Text("x", "long")], new(new() { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning, MaxValueCharacters = 3 })), "svt.limit.exceeded", "MaxValueCharacters");
        Failure(template.Bind([TemplateBinding.Text("a", "xx"), TemplateBinding.Text("a", "xx")], new(new() { MaxTotalValueCharacters = 5 })), "svt.limit.exceeded", "MaxTotalValueCharacters");
    }

    /// <summary>Preparation validates limits before grammar and never decodes bytes.</summary>
    [TestMethod]
    public void ImagePreparationFailures()
    {
        Assert.IsNotNull(EncodedImage.Create(new byte[] { 1, 2 }, "image/png", new(imageOptions: new() { MaxEncodedImageBytes = 2, MaxMediaTypeCharacters = 9 })).Image);
        var result = EncodedImage.Create(new byte[] { 1, 2 }, "bad", new(imageOptions: new() { MaxEncodedImageBytes = 1 }));
        Assert.IsNull(result.Image);
        Assert.AreEqual("MaxEncodedImageBytes", result.Diagnostics[0].Limit);
        result = EncodedImage.Create(new byte[] { 1 }, "bad", new(imageOptions: new() { MaxMediaTypeCharacters = 2 }));
        Assert.AreEqual("MaxMediaTypeCharacters", result.Diagnostics[0].Limit);
        foreach (var media in new[] { "", "image/", "image/*", " image/png", "image/png;charset=x", "text/plain", "image/é", "data:image/png", "image/x#test", "image/svg%2Bxml", "image/x?query", "image/x,body" })
        {
            result = EncodedImage.Create(ReadOnlyMemory<byte>.Empty, media);
            Assert.IsNull(result.Image);
            Assert.AreEqual("svt.image.media-type", result.Diagnostics[0].Code);
        }
    }

    /// <summary>Configuration and documents are reusable and independently owned.</summary>
    [TestMethod]
    public async Task RepeatedAndConcurrentBinding()
    {
        var options = new BindingOptions { MaxValueCharacters = 10 };
        var configuration = new BindingConfiguration(options);
        Assert.AreNotSame(options, configuration.BindingOptions);
        var template = Template("<text data-svt-text='a'>original</text>");
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() => Success(template.Bind([TemplateBinding.Text("a", i.ToString(System.Globalization.CultureInfo.InvariantCulture))], configuration)))));
        for (var i = 0; i < results.Length; i++) Assert.AreEqual(i.ToString(System.Globalization.CultureInfo.InvariantCulture), XElement.Parse(results[i].Xml).Value);
        var original = Success(template.Bind([]));
        Assert.AreEqual("original", XElement.Parse(original.Xml).Value);
        var bytes = original.ToUtf8Bytes();
        Array.Fill(bytes, (byte)0);
        Assert.AreEqual("original", XElement.Parse(original.Xml).Value);
        Assert.IsFalse(original.Xml.StartsWith("<?xml", StringComparison.Ordinal));
        Assert.IsFalse(original.ToUtf8Bytes().AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
    }

    /// <summary>Programming errors and deterministic cancellation remain exceptions.</summary>
    [TestMethod]
    public void ProgrammingErrorsAndCancellation()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TemplateBinding.Text("a", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TemplateBinding.ImageContent("a", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => EncodedImage.Create(ReadOnlyMemory<byte>.Empty, null!));
        var template = Template("<text data-svt-text='a'/>");
        Assert.ThrowsExactly<ArgumentNullException>(() => template.Bind(null!));
        Assert.ThrowsExactly<ArgumentException>(() => template.Bind([null!]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BindingConfiguration(new() { UnknownKeyBehavior = (UnknownKeyBehavior)50 }));
        foreach (var type in new[] { typeof(BindingOptions), typeof(EncodedImageOptions) })
            foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(int)))
                foreach (var value in new[] { 0, -1 })
                {
                    var options = Activator.CreateInstance(type);
                    property.SetValue(options, value);
                    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => type == typeof(BindingOptions)
                        ? new BindingConfiguration((BindingOptions?)options) : new BindingConfiguration(imageOptions: (EncodedImageOptions?)options));
                }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => template.Bind([], cancellationToken: cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => EncodedImage.Create(ReadOnlyMemory<byte>.Empty, "image/png", cancellationToken: cancellation.Token));
        using var duringAccess = new CancellationTokenSource();
        using var memory = new CancelingMemory(duringAccess);
        var source = memory.Memory;
        memory.Armed = true;
        Assert.ThrowsExactly<OperationCanceledException>(() => EncodedImage.Create(source, "image/png", cancellationToken: duringAccess.Token));
    }

    /// <summary>Counts emitted expansion, including shared paint and duplicated image attributes.</summary>
    [TestMethod]
    public void ExpandedOutputLimitsAndRejectedLocations()
    {
        var template = Template("<g data-svt-fill='p' data-svt-stroke='p'/><g data-svt-fill='p'/>" +
            "<image xmlns:x='http://www.w3.org/1999/xlink' data-svt-image='i' x:href='old'/>");
        TemplateBinding[] bindings = [TemplateBinding.Paint("p", "<&😀"), TemplateBinding.ImageContent("i", Image(new byte[20000]))];
        var length = Success(template.Bind(bindings)).ToUtf8Bytes().Length;
        Success(template.Bind(bindings, new(new() { MaxOutputBytes = length })));
        Failure(template.Bind(bindings, new(new() { MaxOutputBytes = length - 1 })), "svt.limit.exceeded", "MaxOutputBytes");
        Failure(template.Bind(bindings, new(new() { MaxOutputBytes = 10 })), "svt.limit.exceeded", "MaxOutputBytes");
        template = Template("<g data-svt-stroke='bad' data-svt-text='bad'/><g xmlns='urn:foreign' data-svt-fill='bad'/>");
        var result = template.Bind([TemplateBinding.Paint("bad", "none")]);
        Failure(result, "svt.binding.rejected");
        var diagnostic = result.Diagnostics[0];
        Assert.HasCount(2, diagnostic.RelatedLocations);
        Assert.IsNotNull(diagnostic.Location);
        // Discovery orders text before stroke even though stroke occurs first in source.
        Assert.IsGreaterThan(diagnostic.RelatedLocations[0].Column, diagnostic.Location.Column);
    }

    /// <summary>Binding observes cancellation caused by an external collection without polling.</summary>
    [TestMethod]
    public void CancellationDuringAssignmentCapture()
    {
        using var cancellation = new CancellationTokenSource();
        var template = Template("<text data-svt-text='a'/>");
        Assert.ThrowsExactly<OperationCanceledException>(() => template.Bind(new CancelingBindings(cancellation), cancellationToken: cancellation.Token));
    }

    /// <summary>Preserves document-level nodes, namespaces, foreign content and all unassigned values.</summary>
    [TestMethod]
    public void DocumentPreservationAcrossEncodings()
    {
        const string body = "<?before keep?>\n<s:svg xmlns:s='http://www.w3.org/2000/svg' xmlns:x='http://www.w3.org/1999/xlink' data-svt-version='0' xml:base='assets/'>" +
            "<s:g xml:space='preserve'> before <s:text data-svt-text='t'><!--leading--><![CDATA[old]]><?note keep?>tail</s:text> after " +
            "<foreign xmlns='urn:other' x:href='untouched' data-svt-fill='bad'><![CDATA[<raw>]]></foreign>" +
            "<s:image data-svt-image='i' href='' x:href='different'/></s:g></s:svg>\n<!--after-->";
        foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            var name = encoding == Encoding.UTF8 ? "utf-8" : "utf-16";
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("<?xml version='1.0' encoding='" + name + "'?>" + body)).ToArray();
            var inspection = TemplateInspector.Inspect(bytes);
            Assert.IsNotNull(inspection.Template);
            Array.Fill(bytes, (byte)0);
            var output = Success(inspection.Template.Bind([]));
            BoundSvgTests.AssertExports(output);
            var expected = XDocument.Parse(body, LoadOptions.PreserveWhitespace);
            // XML serialization can replace CDATA with equivalent character data.
            foreach (var cdata in expected.DescendantNodes().OfType<XCData>().ToArray()) cdata.ReplaceWith(new XText(cdata.Value));
            Assert.IsTrue(XNode.DeepEquals(expected, XDocument.Parse(output.Xml, LoadOptions.PreserveWhitespace)));
            var replaced = XDocument.Parse(Success(inspection.Template.Bind([TemplateBinding.Text("t", "new")])).Xml, LoadOptions.PreserveWhitespace);
            var text = replaced.Descendants(XName.Get("text", "http://www.w3.org/2000/svg")).Single();
            Assert.AreEqual("new", text.Value);
            CollectionAssert.AreEqual(new[] { typeof(XComment), typeof(XText), typeof(XProcessingInstruction) }, text.Nodes().Select(n => n.GetType()).ToArray());
        }
    }

    /// <summary>XML validity checks distinguish malformed UTF-16 from valid supplementary characters.</summary>
    [TestMethod]
    public void SurrogatesAndUtf16Budgets()
    {
        var template = Template("<text data-svt-text='t'/><g data-svt-fill='p'/><image data-svt-image='i'/>");
        foreach (var invalid in new[] { "\ud800", "\udc00", "\ud800X", "\uffff", "\u0001" })
        {
            Failure(template.Bind([TemplateBinding.Text("t", invalid)]), "svt.binding.value");
            Failure(template.Bind([TemplateBinding.Paint("p", invalid)]), "svt.binding.value");
            Failure(template.Bind([TemplateBinding.ImageReference("i", invalid)]), "svt.binding.value");
        }
        var value = char.ConvertFromUtf32(0x1f600);
        var output = Success(template.Bind([TemplateBinding.Text("t", value)], new(new() { MaxValueCharacters = 2, MaxTotalValueCharacters = 3 })));
        Assert.AreEqual(value, XElement.Parse(output.Xml).Elements().First().Value);
        Failure(template.Bind([TemplateBinding.Text("t", value)], new(new() { MaxValueCharacters = 1 })), "svt.limit.exceeded", "MaxValueCharacters");
        Failure(template.Bind([TemplateBinding.Text("t", value)], new(new() { MaxTotalValueCharacters = 2 })), "svt.limit.exceeded", "MaxTotalValueCharacters");
    }

    /// <summary>Base64 tails and every allowed subtype punctuation preserve encoded bytes.</summary>
    [TestMethod]
    public void ImageEncodingEdges()
    {
        var template = Template("<image data-svt-image='i'/>");
        foreach (var length in new[] { 0, 1, 2, 3, 12287, 12288, 12289, 12290 })
        {
            var bytes = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
            var image = Image(bytes, "IMAGE/X!$&^_.+-");
            var output = XElement.Parse(Success(template.Bind([TemplateBinding.ImageContent("i", image)])).Xml);
            var url = (string?)output.Elements().Single().Attribute("href");
            Assert.IsNotNull(url);
            Assert.AreEqual("data:image/x!$&^_.+-;base64," + Convert.ToBase64String(bytes), url);
        }
    }

    /// <summary>Ignored and duplicate assignments cannot bypass image budgets; failures erase warnings.</summary>
    [TestMethod]
    public void FailurePrecedenceAndAtomicRecovery()
    {
        var template = Template("<text data-svt-text='a'>old</text>");
        var image = Image([1, 2]);
        var strict = new BindingConfiguration(new() { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning }, new() { MaxEncodedImageBytes = 1 });
        Failure(template.Bind([TemplateBinding.ImageContent("unknown", image)], strict), "svt.limit.exceeded", "MaxEncodedImageBytes");
        Failure(template.Bind([TemplateBinding.ImageContent("unknown", image), TemplateBinding.ImageContent("unknown", image)], strict), "svt.limit.exceeded", "MaxEncodedImageBytes");
        Failure(template.Bind([TemplateBinding.Text("unknown", "")], new(new() { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning, MaxOutputBytes = 1 })), "svt.limit.exceeded", "MaxOutputBytes");
        Failure(template.Bind([TemplateBinding.Text("a", "\0")], new(new() { MaxOutputBytes = 1 })), "svt.binding.value");
        Failure(template.Bind([TemplateBinding.Text("a", "new"), TemplateBinding.Text("unknown", "")]), "svt.binding.unknown");
        Assert.AreEqual("old", XElement.Parse(Success(template.Bind([])).Xml).Value);
        Assert.AreEqual("next", XElement.Parse(Success(template.Bind([TemplateBinding.Text("a", "next")])).Xml).Value);
        var diagnostics = template.Bind([TemplateBinding.Text("", ""), TemplateBinding.Text("x", "a"), TemplateBinding.Text("x", "b")]);
        CollectionAssert.AreEqual(new List<string> { "svt.binding.key", "svt.binding.duplicate" }, diagnostics.Diagnostics.Select(d => d.Code).ToArray());
        Failure(template.Bind([TemplateBinding.Text("", ""), TemplateBinding.Text("x", "a"), TemplateBinding.Text("x", "b")], new(new() { MaxDiagnostics = 1 })), "svt.limit.exceeded", "MaxDiagnostics");
    }

    private sealed class CancelingBindings(CancellationTokenSource cancellation) : IReadOnlyList<TemplateBinding>
    {
        public int Count => 1;
        public TemplateBinding this[int index]
        {
            get { cancellation.Cancel(); return TemplateBinding.Text("a", "new"); }
        }
        public IEnumerator<TemplateBinding> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CancelingMemory(CancellationTokenSource cancellation) : MemoryManager<byte>
    {
        private readonly byte[] _bytes = [1];
        internal bool Armed { get; set; }
        public override Span<byte> GetSpan() { if (Armed) cancellation.Cancel(); return _bytes; }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    /// <summary>Runs original language-neutral substitution fixtures with semantic XML comparison.</summary>
    [TestMethod]
    public void ConformanceFixtures()
    {
        var directories = Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "BindingFixtures"));
        Assert.IsGreaterThanOrEqualTo(3, directories.Length);
        foreach (var directory in directories)
        {
            var inspection = TemplateInspector.Inspect(File.ReadAllBytes(Path.Combine(directory, "input.svg")));
            Assert.IsNotNull(inspection.Template);
            var request = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "bindings.json")));
            Assert.IsNotNull(request);
            Assert.AreEqual(0, request["format_version"]?.GetValue<int>());
            var entries = request["bindings"]?.AsArray();
            Assert.IsNotNull(entries);
            var bindings = entries.Select(entry =>
            {
                Assert.IsNotNull(entry);
                var key = entry["key"]?.GetValue<string>();
                var value = entry["value"]?.GetValue<string>();
                Assert.IsNotNull(key);
                Assert.IsNotNull(value);
                return entry["kind"]?.GetValue<string>() switch
                {
                    "text" => TemplateBinding.Text(key, value),
                    "paint" => TemplateBinding.Paint(key, value),
                    "image_reference" => TemplateBinding.ImageReference(key, value),
                    "image_content" => TemplateBinding.ImageContent(key, Image(Convert.FromBase64String(value), entry["media_type"]?.GetValue<string>() ?? "")),
                    _ => throw new InvalidDataException("Unknown fixture binding kind."),
                };
            }).ToArray();
            var result = inspection.Template.Bind(bindings);
            var actual = JsonSerializer.SerializeToNode(new
            {
                FormatVersion = 0,
                DocumentAvailable = result.Document is not null,
                Diagnostics = result.Diagnostics.Select(d => new { d.Code, d.Severity, d.Key, d.Location, d.RelatedLocations, d.Limit }),
            }, JsonOptions);
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "expected.json"))), actual), directory);
            if (result.Document is { } output)
            {
                Assert.IsTrue(XNode.DeepEquals(XDocument.Parse(File.ReadAllText(Path.Combine(directory, "output.svg")), LoadOptions.PreserveWhitespace), XDocument.Parse(output.Xml, LoadOptions.PreserveWhitespace)), directory);
                BoundSvgTests.AssertExports(output);
            }
        }
    }
}
