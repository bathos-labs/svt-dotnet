using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Bathos.Graphics.SvgTemplates;

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

var palette = Inspect(File.ReadAllBytes(Path.Combine(sampleRoot, "shared-palette/input.svg")));
Check(palette.Inputs.Single(i => i.Key == "accent").Default.State == InputDefaultState.Common, "Common default.");
Check(palette.Inputs.Single(i => i.Key == "varied").Default.State == InputDefaultState.Mixed, "Raw mixed default.");
Check(palette.Inputs.Single(i => i.Key == "inherited").Default.State == InputDefaultState.Absent, "Absent default.");
var painted = XElement.Parse(Success(palette.Bind([TemplateBinding.Paint("accent", "rgb(1 2 3 / 50%)")], configuration)).Xml);
var targets = painted.Descendants().Where(e => (string?)e.Attribute("data-svt-fill") == "accent" || (string?)e.Attribute("data-svt-stroke") == "accent").ToArray();
Check(targets.Length == 2 && targets.All(e => (string?)(e.Attribute("data-svt-fill") is null ? e.Attribute("stroke") : e.Attribute("fill")) == "rgb(1 2 3 / 50%)"), "Shared raw paint.");
checks.Add("raw-paint-shared-and-default-evidence");

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
checks.Add("cancellation");
Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", checks }));

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
