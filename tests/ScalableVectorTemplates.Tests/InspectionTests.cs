using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ScalableVectorTemplates.Tests;

/// <summary>Portable inspection contract checks.</summary>
[TestClass]
public sealed class InspectionTests
{
    private const string Start = "<svg xmlns=\"http://www.w3.org/2000/svg\" data-svt-version=\"1.0\">";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static readonly JsonSerializerOptions FixtureOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Runs every portable fixture against the public API.</summary>
    [TestMethod]
    public void ConformanceFixtures()
    {
        var directories = Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        Assert.IsGreaterThanOrEqualTo(4, directories.Length);
        foreach (var directory in directories)
        {
            var optionsPath = Path.Combine(directory, "options.json");
            InspectionOptions? options = null;
            if (File.Exists(optionsPath))
            {
                var document = JsonNode.Parse(File.ReadAllText(optionsPath));
                Assert.IsNotNull(document);
                Assert.AreEqual(0, document["format_version"]?.GetValue<int>());
                var properties = document.AsObject();
                properties.Remove("format_version");
                options = properties.Deserialize<InspectionOptions>(FixtureOptions);
                Assert.IsNotNull(options);
            }
            var input = Directory.GetFiles(directory, "input.*");
            Assert.HasCount(1, input);
            var result = TemplateInspector.Inspect(File.ReadAllBytes(input[0]), options);
            var actual = JsonSerializer.SerializeToNode(new
            {
                FormatVersion = 0,
                TemplateAvailable = result.Template is not null,
                Inputs = result.Template?.Inputs ?? [],
                InputGroups = result.Template?.InputGroups.Select(group => new
                {
                    group.Key,
                    group.Label,
                    group.Description,
                    group.Order,
                    InputKeys = group.Inputs.Select(input => input.Key),
                }).ToArray() ?? [],
                Diagnostics = result.Diagnostics.Select(d => new { d.Code, d.Severity, d.Key, d.Location, d.RelatedLocations, d.Limit }),
            }, JsonOptions);
            var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "expected.json")));
            Assert.IsTrue(JsonNode.DeepEquals(expected, actual), $"{Path.GetFileName(directory)}\nExpected: {expected}\nActual: {actual}");
        }
    }

    /// <summary>Checks byte/character equality and all structural budgets.</summary>
    [TestMethod]
    public void BudgetBoundaries()
    {
        var xml = Start + "<g data-svt-fill=\"a\"/><g data-svt-fill=\"b\"/></svg>";
        var bytes = Encoding.UTF8.GetBytes(xml);
        Accepted(bytes, new() { MaxSourceBytes = bytes.Length, MaxXmlCharacters = xml.Length, MaxElementDepth = 2, MaxElements = 3, MaxAttributesPerElement = 2, MaxHooks = 2 });
        Limited(bytes, new() { MaxSourceBytes = bytes.Length - 1 }, "MaxSourceBytes");
        Limited(bytes, new() { MaxXmlCharacters = xml.Length - 1 }, "MaxXmlCharacters");
        Limited(bytes, new() { MaxElementDepth = 1 }, "MaxElementDepth");
        Limited(bytes, new() { MaxElements = 2 }, "MaxElements");
        Limited(bytes, new() { MaxAttributesPerElement = 1 }, "MaxAttributesPerElement");
        Limited(bytes, new() { MaxHooks = 1 }, "MaxHooks");
        bytes = Encoding.UTF8.GetBytes(Start + "<g data-svt-a=\"\" data-svt-b=\"\"/></svg>");
        Accepted(bytes, new() { MaxDiagnostics = 2 });
        Limited(bytes, new() { MaxDiagnostics = 1 }, "MaxDiagnostics");
    }

    /// <summary>Counts characters before entity and newline normalization and excludes the BOM.</summary>
    [TestMethod]
    public void DecodedCharacterBudget()
    {
        var xml = Start + "\r\n<text data-svt-text=\"é\">😀 &amp; X</text></svg>";
        foreach (var encoding in new Encoding[] { new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
            Accepted(bytes, new() { MaxSourceBytes = bytes.Length, MaxXmlCharacters = xml.Length });
            Limited(bytes, new() { MaxXmlCharacters = xml.Length - 1 }, "MaxXmlCharacters");
        }
    }

    /// <summary>Rejects invalid roots, declarations and DTDs.</summary>
    [TestMethod]
    [DataRow("", "svt.xml.invalid")]
    [DataRow("<svg/>", "svt.root.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='1'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='0'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='1.0.0'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='1.1'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='2.0'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='01.0'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='1.00'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version=' 1.0 '/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version='1.0-rc.1'/>", "svt.version.invalid")]
    [DataRow("<svg xmlns='http://www.w3.org/2000/svg' data-svt-version=''/>", "svt.version.invalid")]
    [DataRow("<!DOCTYPE svg SYSTEM 'file:///unavailable'><svg/>", "svt.xml.dtd")]
    [DataRow("<!DOCTYPE svg [<!ENTITY x 'value'>]><svg>&x;</svg>", "svt.xml.dtd")]
    [DataRow("<?xml version='1.0' encoding='iso-8859-1'?><svg/>", "svt.xml.encoding")]
    [DataRow("<?xml version='1.1'?><svg/>", "svt.xml.invalid")]
    [DataRow("<svg a='1' a='2'/>", "svt.xml.invalid")]
    [DataRow("<svg>&unknown;</svg>", "svt.xml.invalid")]
    public void TerminalFailures(string xml, string code) => Terminal(TemplateInspector.Inspect(Encoding.UTF8.GetBytes(xml)), code);

    /// <summary>Preserves ownership and supports valid encodings while rejecting invalid bytes.</summary>
    [TestMethod]
    public void EncodingsAndOwnership()
    {
        var xml = Start + "<text data-svt-text='caption'>é😀</text></svg>";
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
            var result = TemplateInspector.Inspect(bytes);
            Array.Fill(bytes, (byte)0);
            Assert.IsNotNull(result.Template);
            Assert.AreEqual("é😀", result.Template.Inputs[0].Default.Raw);
        }
        foreach (var encoding in new Encoding[] { Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            var result = TemplateInspector.Inspect(encoding.GetBytes("<?xml version='1.0' encoding='utf-16'?>" + xml));
            Assert.IsNotNull(result.Template);
            Assert.IsNotNull(TemplateInspector.Inspect(encoding.GetBytes(xml)).Template);
        }
        Terminal(TemplateInspector.Inspect(new byte[] { 0xff }), "svt.xml.invalid");
        Terminal(TemplateInspector.Inspect(new byte[] { 0xff, 0xfe, 0x3c }), "svt.xml.invalid");
        Terminal(TemplateInspector.Inspect(Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes(xml)).ToArray()), "svt.xml.encoding");
    }

    /// <summary>Checks cancellation and invalid options without converting programming errors.</summary>
    [TestMethod]
    public void ProgrammingBoundaries()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => TemplateInspector.Inspect(ReadOnlyMemory<byte>.Empty, cancellationToken: cancellation.Token));
        foreach (var property in typeof(InspectionOptions).GetProperties())
            foreach (var invalidValue in new[] { 0, -1 })
            {
                var options = new InspectionOptions();
                property.SetValue(options, invalidValue);
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TemplateInspector.Inspect(ReadOnlyMemory<byte>.Empty, options));
            }
    }

    /// <summary>Checks order, namespace admission, label warnings and raw value tolerance.</summary>
    [TestMethod]
    public void DiscoveryEdges()
    {
        var result = Inspect("<custom data-svt-stroke='色' data-svt-fill='色' stroke='???' fill='var(--x)' data-svt-label=' First '/>" +
            "<g data-svt-fill='色' data-svt-label='Second'/>" +
            "<foreign xmlns='urn:foreign' data-svt-fill='色'/>" +
            "<text data-svt-text='t'><tspan/></text>" +
            "<text data-svt-text='t'/>" +
            "<g data-svt-fill='' data-svt-unknown='' data-svt-version='1.0'/>" +
            "<image data-svt-image='i' href='not a uri %'/>");
        Assert.IsNotNull(result.Template);
        Assert.HasCount(3, result.Template.Inputs);
        var paint = result.Template.Inputs[0];
        Assert.AreEqual("色", paint.Key);
        Assert.AreEqual("First", paint.Label);
        Assert.AreEqual(TargetHook.Fill, paint.Targets[0].Hook);
        Assert.AreEqual(TargetHook.Stroke, paint.Targets[1].Hook);
        Assert.AreEqual(InputDefaultState.Mixed, paint.Default.State);
        Assert.AreEqual("", result.Template.Inputs[1].Default.Raw);
        Assert.AreEqual("not a uri %", result.Template.Inputs[2].Default.Raw);
        CollectionAssert.AreEquivalent(new List<string> { "svt.input.labels", "svt.hook.target", "svt.hook.target", "svt.hook.key", "svt.annotation.unknown", "svt.version.nested" }, result.Diagnostics.Select(d => d.Code).ToArray());
    }

    /// <summary>Checks UTF-16 columns and deterministic diagnostic positions.</summary>
    [TestMethod]
    public void LocationsAndNamespaces()
    {
        var result = TemplateInspector.Inspect(Encoding.UTF8.GetBytes("<s:svg xmlns:s='http://www.w3.org/2000/svg' data-svt-version='1.0'>\r\n\t<s:g title='😀é' data-svt-fill='x'/>\r\n</s:svg>"));
        Assert.IsNotNull(result.Template);
        var point = result.Template.Inputs[0].Targets[0].Location;
        Assert.AreEqual(2, point.Line);
        Assert.AreEqual(19, point.Column);
        result = Inspect("<g xmlns:f='urn:f' f:data-svt-fill='ignored' data-svt-label='inert'/>");
        Assert.IsNotNull(result.Template);
        Assert.IsEmpty(result.Template.Inputs);
        Assert.IsEmpty(result.Diagnostics);
    }

    /// <summary>DTD-like text stays inert, while real declarations never reach entity parsing.</summary>
    [TestMethod]
    public void DtdAndFailurePrecedence()
    {
        var result = Inspect("<!-- <!DOCTYPE svg> --><?svt-rejected-dtd?><text data-svt-text='t'><![CDATA[<!DOCTYPE svg>]]></text>");
        Assert.IsNotNull(result.Template);
        Assert.AreEqual("<!DOCTYPE svg>", result.Template.Inputs[0].Default.Raw);
        Terminal(TemplateInspector.Inspect(Encoding.UTF8.GetBytes("<?svt-rejected-dtd?><!DOCTYPE svg [broken subset]<svg/>")), "svt.xml.dtd");
        Terminal(TemplateInspector.Inspect(Encoding.UTF8.GetBytes("<bad></wrong><!DOCTYPE svg>")), "svt.xml.invalid");
        result = TemplateInspector.Inspect(Encoding.UTF8.GetBytes(Start + "<g data-svt-fill=''/><g/></svg>"), new() { MaxElements = 2 });
        Terminal(result, "svt.limit.exceeded");
        result = TemplateInspector.Inspect(Encoding.UTF8.GetBytes("<wrong data-svt-version='1' data-svt-fill=''/ >"));
        Terminal(result, "svt.xml.invalid");
    }

    /// <summary>Rejects whole ambiguous groups without emitting their label warning.</summary>
    [TestMethod]
    public void AmbiguityAndRawSummaries()
    {
        var result = Inspect("<g data-svt-image='x' data-svt-label='One'/><g data-svt-fill='x' data-svt-label='Two'/>" +
            "<g data-svt-image='y'/><g data-svt-image='y'/>" +
            "<g data-svt-fill='absent' data-svt-stroke='absent'/>" +
            "<g data-svt-fill='empty' data-svt-stroke='empty' fill='' stroke=''/>" +
            "<g data-svt-fill=' spaced '/><g data-svt-fill='spaced'/>" +
            "<image data-svt-image='missing'/><image xmlns:x='http://www.w3.org/1999/xlink' data-svt-image='fallback' x:href='preview.svg'/>");
        Assert.IsNotNull(result.Template);
        Assert.HasCount(2, result.Diagnostics);
        Assert.IsTrue(result.Diagnostics.All(d => d.Code == "svt.input.ambiguous"));
        Assert.AreEqual(InputDefaultState.Absent, result.Template.Inputs[0].Default.State);
        Assert.AreEqual(InputDefaultState.Common, result.Template.Inputs[1].Default.State);
        Assert.AreEqual("", result.Template.Inputs[1].Default.Raw);
        Assert.AreEqual(" spaced ", result.Template.Inputs[2].Key);
        Assert.AreEqual("spaced", result.Template.Inputs[3].Key);
        Assert.IsNull(result.Template.Inputs[4].Targets[0].Default.Source);
        Assert.AreEqual(DefaultSource.XLinkHref, result.Template.Inputs[5].Targets[0].Default.Source);
    }

    /// <summary>Diagnostic source ordering is independent of hook discovery order.</summary>
    [TestMethod]
    public void DiagnosticOrderingAndInvalidHookBudget()
    {
        var content = "<g data-svt-stroke='' data-svt-text='' data-svt-fill=''/>";
        var result = Inspect(content);
        Assert.HasCount(3, result.Diagnostics);
        var columns = result.Diagnostics.Select(d => d.Location?.Column).ToArray();
        CollectionAssert.AreEqual(columns.Order().ToArray(), columns);
        Limited(Encoding.UTF8.GetBytes(Start + content + "</svg>"), new() { MaxHooks = 2 }, "MaxHooks");
        result = Inspect("<f:g xmlns:f='urn:foreign' data-svt-text=''/>");
        Assert.AreEqual("svt.hook.target", result.Diagnostics[0].Code);
    }

    /// <summary>Cancellation after entry is observed at the parsing boundary without timers.</summary>
    [TestMethod]
    public void CancellationDuringSourceAccess()
    {
        using var cancellation = new CancellationTokenSource();
        using var memory = new CancelingMemory(Encoding.UTF8.GetBytes(Start + "</svg>"), cancellation);
        var source = memory.Memory;
        memory.Armed = true;
        Assert.ThrowsExactly<OperationCanceledException>(() => TemplateInspector.Inspect(source, cancellationToken: cancellation.Token));
    }

    /// <summary>Cancellation is not converted to malformed input when decoding or parsing fails.</summary>
    [TestMethod]
    public void CancellationWithInvalidSource()
    {
        foreach (var bytes in new[] { new byte[] { 0xff }, Encoding.UTF8.GetBytes("<broken"), Array.Empty<byte>() })
        {
            using var cancellation = new CancellationTokenSource();
            using var memory = new CancelingMemory(bytes, cancellation);
            var source = memory.Memory;
            memory.Armed = true;
            Assert.ThrowsExactly<OperationCanceledException>(() => TemplateInspector.Inspect(source, cancellationToken: cancellation.Token));
        }
    }

    /// <summary>Unsupported declarations are diagnosed before interpreting their payload bytes.</summary>
    [TestMethod]
    public void UnsupportedEncodingWithNonAsciiPayload()
    {
        var bytes = Encoding.Latin1.GetBytes("<?xml version='1.0' encoding='iso-8859-1'?>" + Start + "é</svg>");
        Terminal(TemplateInspector.Inspect(bytes), "svt.xml.encoding");
    }

    /// <summary>Labels trim only XML whitespace and related locations exclude matching labels.</summary>
    [TestMethod]
    public void LabelEvidenceAndXmlNormalization()
    {
        var result = Inspect("\n<g data-svt-fill='p' data-svt-label=' A '/>" +
            "\n<g data-svt-fill='p' data-svt-label='A'/>" +
            "\n<g data-svt-stroke='p' data-svt-label='B'/>" +
            "\n<text data-svt-text='t' data-svt-label='&#160;'>a\r\nb<?pi ignored?>c<![CDATA[d]]><!-- ignored -->e</text>" +
            "\n<g data-svt-fill='w' fill='a\tb&#x9;c'/>");
        Assert.IsNotNull(result.Template);
        var warning = result.Diagnostics.Single();
        Assert.AreEqual("svt.input.labels", warning.Code);
        Assert.AreEqual(2, warning.Location?.Line);
        Assert.HasCount(1, warning.RelatedLocations);
        Assert.AreEqual(4, warning.RelatedLocations[0].Line);
        Assert.AreEqual("\u00a0", result.Template.Inputs[1].Label);
        Assert.AreEqual("a\nbcde", result.Template.Inputs[1].Default.Raw);
        Assert.AreEqual("a b\tc", result.Template.Inputs[2].Default.Raw);
    }

    /// <summary>Declaration matching covers both byte orders, mismatches and strict UTF-8.</summary>
    [TestMethod]
    public void DeclarationEdges()
    {
        foreach (var (encoding, name) in new (Encoding, string)[]
        {
            (new UTF8Encoding(false, true), "UTF-8"),
            (new UnicodeEncoding(false, false, true), "UTF-16LE"),
            (new UnicodeEncoding(true, false, true), "UTF-16BE"),
        })
        {
            var xml = "<?xml version='1.0' encoding='" + name + "'?>" + Start + "</svg>";
            Assert.IsNotNull(TemplateInspector.Inspect(encoding.GetBytes(xml)).Template);
            var unsupported = encoding.GetBytes("<?xml version='1.0' encoding='unsupported'?>").Concat(new byte[] { 0xff }).ToArray();
            Terminal(TemplateInspector.Inspect(unsupported), "svt.xml.encoding");
        }
        Terminal(TemplateInspector.Inspect(Encoding.UTF8.GetBytes("<?xml version='1.0' encoding='UTF-16'?>" + Start + "</svg>")), "svt.xml.invalid");
        Terminal(TemplateInspector.Inspect(new byte[] { 0xc0, 0xaf }), "svt.xml.invalid");
        Terminal(TemplateInspector.Inspect(new byte[] { 0xed, 0xa0, 0x80 }), "svt.xml.invalid");
    }

    /// <summary>Explicitly raised limits are honored without recursion in discovery.</summary>
    [TestMethod]
    public void RaisedLimitsAndGroupedDiagnosticBudget()
    {
        var xml = Start + string.Concat(Enumerable.Repeat("<g>", 64)) +
            "<g data-svt-fill='a'/>" + string.Concat(Enumerable.Repeat("</g>", 64)) + "</svg>";
        Limited(Encoding.UTF8.GetBytes(xml), new(), "MaxElementDepth");
        Accepted(Encoding.UTF8.GetBytes(xml), new() { MaxElementDepth = 66 });
        xml = Start + "<g data-svt-fill='p' data-svt-label='A'/><g data-svt-fill='p' data-svt-label='B'/>" +
            "<g data-svt-image='i'/><g data-svt-image='i'/></svg>";
        Accepted(Encoding.UTF8.GetBytes(xml), new() { MaxDiagnostics = 2 });
        Limited(Encoding.UTF8.GetBytes(xml), new() { MaxDiagnostics = 1 }, "MaxDiagnostics");
    }

    private sealed class CancelingMemory(byte[] bytes, CancellationTokenSource cancellation) : MemoryManager<byte>
    {
        internal bool Armed { get; set; }
        public override Span<byte> GetSpan()
        {
            if (Armed) cancellation.Cancel();
            return bytes;
        }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    private static InspectionResult Inspect(string content) => TemplateInspector.Inspect(Encoding.UTF8.GetBytes(Start + content + "</svg>"));
    private static void Accepted(byte[] bytes, InspectionOptions options) => Assert.IsNotNull(TemplateInspector.Inspect(bytes, options).Template);
    private static void Limited(byte[] bytes, InspectionOptions options, string limit)
    {
        var result = TemplateInspector.Inspect(bytes, options);
        Terminal(result, "svt.limit.exceeded");
        Assert.AreEqual(limit, result.Diagnostics[0].Limit);
    }
    private static void Terminal(InspectionResult result, string code)
    {
        Assert.IsNull(result.Template);
        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual(code, result.Diagnostics[0].Code);
    }
}
