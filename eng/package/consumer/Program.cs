using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Bathos.Graphics.SvgTemplates;
using SvgTemplateExamples;

if (args.Length != 4) throw new ArgumentException("Expected samples, package version, assembly hash and commit.");
var sampleRoot = args[0];
var assembly = typeof(TemplateInspector).Assembly;
Check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))) == args[2], "Loaded assembly differs from inspected package.");
Check(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion == args[1] + "+" + args[3], "Loaded assembly provenance mismatch.");
var checks = new List<string> { "loaded-package-identity" };
var configuration = new BindingConfiguration();
var textPath = Path.Combine(sampleRoot, "text-card/input.svg");
var source = File.ReadAllBytes(textPath);
var original = source.ToArray();
var text = Inspect(source);
Array.Fill(source, (byte)0);
var empty = Success(text.Bind([], configuration));
Check(XNode.DeepEquals(XElement.Parse(Encoding.UTF8.GetString(original)), XElement.Parse(empty.Xml)), "Omission changed source XML.");
var changed = Success(text.Bind([TemplateBinding.Text("heading", "Café & <Hello>"), TemplateBinding.Text("caption", "")], configuration));
var changedXml = XElement.Parse(changed.Xml);
Check(Find(changedXml, "heading").Value == "Café & <Hello>", "Text escaping/Unicode.");
Check(Find(changedXml, "caption").Value == "", "Explicit empty text.");
Check(Find(changedXml, "note").Value == "  space  ", "Omission/whitespace.");
Check(Success(text.Bind([], configuration)).Xml == empty.Xml, "Template mutated by binding.");
var outputBytes = changed.ToUtf8Bytes();
Array.Fill(outputBytes, (byte)0);
Check(changed.ToUtf8Bytes()[0] == (byte)'<', "Output bytes are not independent.");
Check(File.ReadAllBytes(textPath).SequenceEqual(original), "Source file mutated.");
checks.Add("inspection-text-omission-empty-ownership");
checks.Add("caller-owned-utf8-output");
VerifyPooledConsumption(changed);
checks.Add("pooled-output-consumption-lifetime");

var palette = Inspect(File.ReadAllBytes(Path.Combine(sampleRoot, "shared-palette/input.svg")));
Check(palette.Inputs.Single(i => i.Key == "accent").Default.State == InputDefaultState.Common, "Common default.");
Check(palette.Inputs.Single(i => i.Key == "varied").Default.State == InputDefaultState.Mixed, "Raw mixed default.");
Check(palette.Inputs.Single(i => i.Key == "inherited").Default.State == InputDefaultState.Absent, "Absent default.");
var painted = XElement.Parse(Success(palette.Bind([TemplateBinding.Paint("accent", "rgb(1 2 3 / 50%)")], configuration)).Xml);
var targets = painted.Descendants().Where(e => (string?)e.Attribute("data-svt-fill") == "accent" || (string?)e.Attribute("data-svt-stroke") == "accent").ToArray();
Check(targets.Length == 2 && targets.All(e => (string?)(e.Attribute("data-svt-fill") is null ? e.Attribute("stroke") : e.Attribute("fill")) == "rgb(1 2 3 / 50%)"), "Shared raw paint.");
checks.Add("raw-paint-shared-and-default-evidence");

var accentInput = palette.Inputs.Single(i => i.Key == "accent");
Check(accentInput.Label == "Primary accent" && accentInput.LabelSource == InputLabelSource.Metadata &&
    accentInput.Description == "One color shared by the panel and line." && accentInput.Order == 20, "Resolved metadata fields.");
Check(palette.InputGroups.Length == 2 && palette.InputGroups[0].Key == "advanced" &&
    palette.InputGroups[1].Key == "appearance" && palette.InputGroups[1].Inputs[0].Key == "varied" &&
    ReferenceEquals(palette.InputGroups[1].Inputs[1], accentInput), "Presentation order and input identity.");
var metadataName = XName.Get("metadata", "http://www.w3.org/2000/svg");
var originalMetadata = XElement.Load(Path.Combine(sampleRoot, "shared-palette/input.svg")).Element(metadataName);
Check(originalMetadata is not null && XNode.DeepEquals(originalMetadata, painted.Element(metadataName)), "Metadata preservation through binding.");
checks.Add("metadata-presentation-and-preservation");

var imageBytes = File.ReadAllBytes(Path.Combine(sampleRoot, "assets/portrait.png"));
var encoded = Convert.ToBase64String(imageBytes);
var prepared = EncodedImage.Create(imageBytes, "image/png", configuration);
Check(prepared.Diagnostics.IsEmpty, "Image preparation diagnostics.");
var image = prepared.Image ?? throw new InvalidOperationException("Image preparation failed.");
Array.Fill(imageBytes, (byte)0);
var images = Inspect(Encoding.UTF8.GetBytes("""
    <svg xmlns="http://www.w3.org/2000/svg" xmlns:x="http://www.w3.org/1999/xlink" data-svt-version="0">
      <image data-svt-image="content" href="old" x:href="old"/>
      <image data-svt-image="reference" href="original"/>
    </svg>
    """));
var assignments = new[] { TemplateBinding.ImageContent("content", image), TemplateBinding.ImageReference("reference", "../assets/portrait.png?x=1&y=2") };
var originalImageOutput = Success(images.Bind(assignments, configuration)).Xml;
var imageOutput = XElement.Parse(originalImageOutput);
var imageElement = imageOutput.Elements().First();
Check((string?)imageElement.Attribute("href") == "data:image/png;base64," + encoded, "Image content/ownership.");
Check((string?)imageElement.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink")) == "data:image/png;base64," + encoded, "Existing XLink synchronization.");
Check((string?)imageOutput.Elements().Last().Attribute("href") == "../assets/portrait.png?x=1&y=2", "Reference preservation.");
var strictImage = new BindingConfiguration(imageOptions: new() { MaxEncodedImageBytes = imageBytes.Length - 1 });
Reject(images.Bind(assignments, strictImage), "svt.limit.exceeded", "MaxEncodedImageBytes");
Check(Success(images.Bind(assignments, configuration)).Xml == originalImageOutput, "Image recovery/reuse.");
checks.Add("encoded-images-references-and-policy-recheck");

var interpretedSource = Inspect(Encoding.UTF8.GetBytes("""
    <svg xmlns="http://www.w3.org/2000/svg" data-svt-version="0">
      <g data-svt-fill="color" fill="hsl(120 100% 50% / 25%)"/>
      <g data-svt-stroke="color" stroke="rgb(0 255 0 / .25)"/>
      <g data-svt-fill="context" fill="var(--accent)"/>
      <g data-svt-fill="server" fill="url(#gradient) red"/>
      <image data-svt-image="portrait" href="data:image/x%23;base64,invalid-payload"/>
    </svg>
    """));
var interpretationOptions = new InterpretationOptions();
var colorInput = interpretedSource.Inputs[0];
Check(colorInput.Default.State == InputDefaultState.Mixed, "Raw mixed evidence before interpretation.");
foreach (var target in colorInput.Targets)
{
    var result = DefaultInterpreter.InterpretPaint(target, interpretationOptions);
    Check(ReferenceEquals(result.Target, target) && result.Kind == PaintInterpretationKind.Literal &&
        result.Color is { Red: 0, Green: 1, Blue: 0, Alpha: .25 } && result.ExceededLimit is null,
        "Per-target literal interpretation and source identity.");
}
Check(DefaultInterpreter.InterpretPaint(interpretedSource.Inputs[1].Targets[0]).Kind == PaintInterpretationKind.RequiresContext,
    "Context interpretation.");
Check(DefaultInterpreter.InterpretPaint(interpretedSource.Inputs[2].Targets[0]).Kind == PaintInterpretationKind.ResourceReference,
    "Paint-server fallback must not become a literal.");
var imageTarget = interpretedSource.Inputs[3].Targets[0];
var imageInterpretation = DefaultInterpreter.InterpretImage(imageTarget, new() { MaxReferenceCharacters = 1 });
Check(ReferenceEquals(imageTarget, imageInterpretation.Target) &&
    imageInterpretation.Kind == ImageInterpretationKind.DataUri && imageInterpretation.DeclaredMediaType == "image/x%23",
    "Bounded data header and unexamined payload.");
var limitedPaint = DefaultInterpreter.InterpretPaint(colorInput.Targets[0], new() { MaxPaintCharacters = 1 });
Check(limitedPaint.Kind == PaintInterpretationKind.UnsupportedOrUnrecognized && limitedPaint.Color is null &&
    limitedPaint.ExceededLimit == InterpretationLimit.PaintCharacters, "Typed interpretation limit.");
Check(colorInput.Default.State == InputDefaultState.Mixed, "Interpretation changed the raw summary.");
var retained = Success(interpretedSource.Bind([]));
Check(Inspect(retained.ToUtf8Bytes()).Inputs[0].Targets[0].Default.Raw == colorInput.Targets[0].Default.Raw,
    "Omission after interpretation changed authored paint.");
Success(interpretedSource.Bind([TemplateBinding.Paint("context", "unfamiliar raw syntax")]));
checks.Add("per-target-interpretation-and-preservation");

// Compile the same host-owned example against the isolated package, without a UI framework.
foreach (var raw in new[] { "red", "#abc", "rgb(.1 50% 0)" })
{
    var editorTemplate = Inspect(Encoding.UTF8.GetBytes(
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" data-svt-version=\"0\"><g data-svt-fill=\"paint\" fill=\"{raw}\"/></svg>"));
    var adapter = new PaintEditorAdapter(editorTemplate.Inputs[0]);
    var seed = adapter.InitialChooserValue ?? throw new InvalidOperationException("Missing chooser seed.");
    var initialized = adapter.OpenChooser();
    initialized.ObserveWidgetValue(seed);
    initialized.ObserveWidgetValue(new(12, 34, 56));
    Check(adapter.GetBindings().IsEmpty, "Initialization created a preview replacement.");
    initialized.Accept();
    Check(adapter.GetBindings().IsEmpty, "No edit created a binding.");
    var cancelled = adapter.OpenChooser();
    cancelled.SelectFromUser(new(1, 2, 3));
    cancelled.Cancel();
    Check(adapter.GetBindings().IsEmpty, "Cancelled edit created a binding.");
    var reverted = adapter.OpenChooser();
    reverted.SelectFromUser(new(1, 2, 3));
    reverted.SelectFromUser(seed);
    reverted.Accept();
    Check(adapter.GetBindings().IsEmpty, "Return to initial chooser value created a binding.");
    var accepted = adapter.OpenChooser();
    accepted.SelectFromUser(new(1, 2, 3));
    Check(adapter.GetBindings().IsEmpty, "Unaccepted edit created a preview replacement.");
    accepted.Accept();
    Check(adapter.GetBindings().Length == 1 &&
        Inspect(Success(editorTemplate.Bind(adapter.GetBindings())).ToUtf8Bytes()).Inputs[0].Default.Raw == "#010203",
        "Accepted edit did not create one explicit replacement.");
    adapter.Reset();
    Check(adapter.GetBindings().IsEmpty &&
        Inspect(Success(editorTemplate.Bind(adapter.GetBindings())).ToUtf8Bytes()).Inputs[0].Default.Raw == raw,
        "Reset did not restore omission and authored spelling.");
}
var mixedEditor = new PaintEditorAdapter(colorInput);
Check(mixedEditor.InitialChooserValue is null && mixedEditor.Presentation.Length == 2 &&
    mixedEditor.AuthoredEvidence.Default.State == InputDefaultState.Mixed && mixedEditor.GetBindings().IsEmpty,
    "Shared chooser policy changed raw Mixed evidence.");
checks.Add("editor-default-intent");

Reject(text.Bind([TemplateBinding.Text("unknown", "x")], configuration), "svt.binding.unknown");
Reject(text.Bind([TemplateBinding.Paint("heading", "red")], configuration), "svt.binding.kind");
Reject(text.Bind([TemplateBinding.Text("heading", "\0")], configuration), "svt.binding.value");
Reject(text.Bind([TemplateBinding.Text("heading", "12345")], new(new() { MaxValueCharacters = 4 })), "svt.limit.exceeded", "MaxValueCharacters");
Reject(text.Bind([], new(new() { MaxOutputBytes = 8 })), "svt.limit.exceeded", "MaxOutputBytes");
var ignored = text.Bind([TemplateBinding.Text("unknown", "x")], new(new() { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning }));
Check(ignored.Document is not null && ignored.Diagnostics.Length == 1 && ignored.Diagnostics[0].Code == "svt.binding.unknown" && ignored.Diagnostics[0].Severity == DiagnosticSeverity.Warning, "Configurable unknown key.");
var rejected = TemplateInspector.Inspect(Encoding.UTF8.GetBytes("<svg>"));
Check(rejected.Template is null && rejected.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Malformed source diagnostics.");
checks.Add("structured-rejection-budgets-and-policy");

using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
Cancelled(() => TemplateInspector.Inspect(original, cancellationToken: cancellation.Token));
Cancelled(() => EncodedImage.Create(imageBytes, "image/png", cancellationToken: cancellation.Token));
Cancelled(() => text.Bind([], configuration, cancellation.Token));
Cancelled(() => DefaultInterpreter.InterpretPaint(colorInput.Targets[0], cancellationToken: cancellation.Token));
Cancelled(() => DefaultInterpreter.InterpretImage(imageTarget, cancellationToken: cancellation.Token));
checks.Add("cancellation");
Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", checks }));

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ConsumePooled(BoundSvg document, ArrayPool<byte> pool,
    Action<ReadOnlySpan<byte>> consume)
{
    byte[] rented = pool.Rent(document.Utf8ByteCount);
    try
    {
        if (!document.TryWriteUtf8Bytes(rented, out int bytesWritten))
            throw new InvalidOperationException("The rented buffer is too small.");

        // The consumer must finish reading before it returns, including when it throws.
        consume(rented.AsSpan(0, bytesWritten));
    }
    finally
    {
        pool.Return(rented);
    }
}
static void VerifyPooledConsumption(BoundSvg document)
{
    var expected = document.ToUtf8Bytes();
    var pool = new TrackingBytePool(document.Utf8ByteCount + 13);
    void Consume(ReadOnlySpan<byte> utf8)
    {
        Check(pool.IsRented, "The loan was returned before consumption finished.");
        Check(utf8.Length == document.Utf8ByteCount && utf8.SequenceEqual(expected),
            "The consumer did not receive exactly the written prefix.");
        pool.Events.Add("consume");
    }

    ConsumePooled(document, pool, Consume);
    Check(!pool.IsRented && pool.ReturnCount == 1, "Successful consumption did not return the loan.");
    Check(pool.Events.SequenceEqual(["rent", "consume", "return"]), "Successful consumption lifetime order.");

    pool.Events.Clear();
    var failure = new InvalidOperationException("Controlled consumer failure.");
    try
    {
        ConsumePooled(document, pool, utf8 =>
        {
            Consume(utf8);
            throw failure;
        });
        throw new InvalidOperationException("Consumer failure did not propagate.");
    }
    catch (InvalidOperationException error) when (ReferenceEquals(error, failure))
    {
        Check(!pool.IsRented && pool.ReturnCount == 2, "Failed consumption did not return the loan.");
        Check(pool.Events.SequenceEqual(["rent", "consume", "return"]), "Failed consumption lifetime order.");
    }

    // All readers have finished, so this same loan may now be reused after either outcome.
    ConsumePooled(document, pool, Consume);
    Check(!pool.IsRented && pool.ReturnCount == 3, "Reuse after consumption failure.");
    ConsumePooled(document, ArrayPool<byte>.Shared, utf8 =>
    {
        var parsed = XElement.Parse(Encoding.UTF8.GetString(utf8));
        Check(XNode.DeepEquals(parsed, XElement.Parse(document.Xml)), "Synchronous parsing of pooled output.");
    });
}
static InspectedTemplate Inspect(byte[] bytes)
{
    var result = TemplateInspector.Inspect(bytes);
    Check(result.Diagnostics.IsEmpty, "Unexpected inspection diagnostics.");
    return result.Template ?? throw new InvalidOperationException("Inspection failed.");
}
static BoundSvg Success(BindingResult result)
{
    Check(result.Diagnostics.IsEmpty, "Unexpected binding diagnostics.");
    var document = result.Document ?? throw new InvalidOperationException("Binding failed.");
    VerifyExport(document);
    return document;
}
static void VerifyExport(BoundSvg document)
{
    var expected = document.ToUtf8Bytes();
    Check(document.Utf8ByteCount == expected.Length && expected.Length == Encoding.UTF8.GetByteCount(document.Xml), "UTF-8 byte count.");
    var storage = new byte[expected.Length + 13];
    Array.Fill(storage, (byte)0xcc);
    Check(!document.TryWriteUtf8Bytes(default, out var written) && written == 0, "Default span.");
    Check(!document.TryWriteUtf8Bytes(storage.AsSpan(3, expected.Length - 1), out written) && written == 0, "Insufficient span.");
    Check(storage.AsSpan().IndexOfAnyExcept((byte)0xcc) < 0, "Short span changed storage.");
    Check(document.TryWriteUtf8Bytes(storage.AsSpan(3, expected.Length + 5), out written) && written == expected.Length, "Oversized span.");
    Check(storage.AsSpan(3, written).SequenceEqual(expected), "Export bytes differ.");
    Check(storage.AsSpan(0, 3).IndexOfAnyExcept((byte)0xcc) < 0 && storage.AsSpan(3 + written).IndexOfAnyExcept((byte)0xcc) < 0, "Export changed surrounding storage.");
    Array.Clear(storage);
    Check(document.TryWriteUtf8Bytes(storage.AsSpan(0, expected.Length), out written) && written == expected.Length, "Repeated exact span.");
    Check(storage.AsSpan(0, written).SequenceEqual(expected) && document.ToUtf8Bytes().AsSpan().SequenceEqual(expected), "Document changed after export mutation.");
}
static XElement Find(XElement root, string key) => root.Descendants().Single(e => (string?)e.Attribute("data-svt-text") == key);
static void Reject(BindingResult result, string code, string? limit = null) => Check(result.Document is null && result.Diagnostics.Length == 1 && result.Diagnostics[0].Code == code && result.Diagnostics[0].Limit == limit, "Structured rejection: " + code);
static void Cancelled(Action action)
{
    try { action(); }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Cancellation did not throw.");
}

// A deterministic host pool proves lifetime ordering and oversized-buffer handling without timing.
internal sealed class TrackingBytePool(int capacity) : ArrayPool<byte>
{
    private readonly byte[] _buffer = new byte[capacity];
    private int _requested;
    internal bool IsRented { get; private set; }
    internal int ReturnCount { get; private set; }
    internal List<string> Events { get; } = [];

    public override byte[] Rent(int minimumLength)
    {
        if (IsRented || minimumLength > _buffer.Length)
            throw new InvalidOperationException("Invalid test pool rental.");
        Array.Fill(_buffer, (byte)0xcc);
        _requested = minimumLength;
        IsRented = true;
        Events.Add("rent");
        return _buffer;
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        if (!IsRented || !ReferenceEquals(array, _buffer))
            throw new InvalidOperationException("The loan must be returned exactly once to its owner.");
        if (array.AsSpan(_requested).IndexOfAnyExcept((byte)0xcc) >= 0)
            throw new InvalidOperationException("Unused pool capacity was modified.");
        if (clearArray) Array.Clear(array);
        IsRented = false;
        ReturnCount++;
        Events.Add("return");
    }
}
