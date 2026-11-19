using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Bathos.Graphics.SvgTemplates.Tests;

/// <summary>Per-target interpretation through the public inspected-evidence boundary.</summary>
[TestClass]
public sealed class InterpretationTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    /// <summary>Portable grammar, recovery and reference cases retain exact target evidence.</summary>
    [TestMethod]
    public void PortableCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InterpretationFixtures/cases.json")));
        Assert.AreEqual(0, document.RootElement.GetProperty("format_version").GetInt32());
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var raw = item.GetProperty("raw").GetString();
            var image = item.GetProperty("hook").GetString() == "image";
            var target = Target(raw, image ? TargetHook.Image : TargetHook.Fill);
            var options = item.GetProperty("options").Deserialize<InterpretationOptions>(Options);
            var kind = item.GetProperty("kind").GetString();
            var limit = item.GetProperty("exceeded_limit").GetString();
            if (image)
            {
                var result = DefaultInterpreter.InterpretImage(target, options);
                Assert.AreSame(target, result.Target);
                Assert.AreEqual(kind, result.Kind.ToString(), raw);
                Assert.AreEqual(limit, result.ExceededLimit?.ToString(), raw);
                Assert.AreEqual(item.GetProperty("declared_media_type").GetString(), result.DeclaredMediaType, raw);
            }
            else
            {
                var result = DefaultInterpreter.InterpretPaint(target, options);
                Assert.AreSame(target, result.Target);
                Assert.AreEqual(kind, result.Kind.ToString(), raw);
                Assert.AreEqual(limit, result.ExceededLimit?.ToString(), raw);
                var color = item.GetProperty("color");
                if (color.ValueKind == JsonValueKind.Null) Assert.IsNull(result.Color, raw);
                else Color(result.Color, color.EnumerateArray().Select(v => v.GetDouble()).ToArray());
            }
            Assert.AreEqual(raw, target.Default.Raw);
        }
    }

    /// <summary>All fixed normative named-color values, including aliases, are available in any ASCII case.</summary>
    [TestMethod]
    public void NamedColors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InterpretationFixtures/named-colors.json")));
        var count = 0;
        foreach (var entry in document.RootElement.GetProperty("colors").EnumerateObject())
        {
            var hex = entry.Value.GetString() ?? throw new AssertFailedException("Missing color value.");
            var number = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var expected = new[] { (number >> 16) / 255d, ((number >> 8) & 255) / 255d, (number & 255) / 255d, 1d };
            Color(DefaultInterpreter.InterpretPaint(Target(entry.Name)).Color, expected);
            Color(DefaultInterpreter.InterpretPaint(Target(entry.Name.ToUpperInvariant(), TargetHook.Stroke)).Color, expected);
            count++;
        }
        Assert.AreEqual(148, count);
    }

    /// <summary>All positive options are validated, even for absent values and unused budgets.</summary>
    [TestMethod]
    public void ProgrammerArgumentsAndCancellation()
    {
        var paint = Target(null);
        var image = Target(null, TargetHook.Image);
        Assert.ThrowsExactly<ArgumentNullException>(() => DefaultInterpreter.InterpretPaint(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => DefaultInterpreter.InterpretImage(null!));
        Assert.ThrowsExactly<ArgumentException>(() => DefaultInterpreter.InterpretPaint(image));
        Assert.ThrowsExactly<ArgumentException>(() => DefaultInterpreter.InterpretImage(paint));
        var text = Target("text", TargetHook.Text);
        Assert.ThrowsExactly<ArgumentException>(() => DefaultInterpreter.InterpretPaint(text));
        Assert.ThrowsExactly<ArgumentException>(() => DefaultInterpreter.InterpretImage(text));
        foreach (var property in typeof(InterpretationOptions).GetProperties())
            foreach (var invalid in new[] { 0, -1, int.MinValue })
            {
                var options = new InterpretationOptions();
                property.SetValue(options, invalid);
                Assert.AreEqual(property.Name, Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DefaultInterpreter.InterpretPaint(paint, options)).ParamName);
                Assert.AreEqual(property.Name, Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DefaultInterpreter.InterpretImage(image, options)).ParamName);
            }
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.AreEqual(source.Token, Assert.ThrowsExactly<OperationCanceledException>(() => DefaultInterpreter.InterpretPaint(null!, cancellationToken: source.Token)).CancellationToken);
        foreach (var raw in new string?[] { null, "", "red", "unsupported", new('a', 4097) })
        {
            Assert.ThrowsExactly<OperationCanceledException>(() => DefaultInterpreter.InterpretPaint(Target(raw), cancellationToken: source.Token));
            Assert.ThrowsExactly<OperationCanceledException>(() => DefaultInterpreter.InterpretImage(Target(raw, TargetHook.Image), cancellationToken: source.Token));
        }
    }

    /// <summary>Inclusive limits have scoped, typed outcomes and deterministic lexical precedence.</summary>
    [TestMethod]
    public void LimitsAndPrecedence()
    {
        Assert.AreEqual(PaintInterpretationKind.Literal, DefaultInterpreter.InterpretPaint(Target(" red "), new() { MaxPaintCharacters = 5 }).Kind);
        PaintLimit(" red ", new() { MaxPaintCharacters = 4 }, InterpretationLimit.PaintCharacters);
        PaintLimit("\\" + new string('x', 4096), new(), InterpretationLimit.PaintCharacters);
        var valid = DefaultInterpreter.InterpretPaint(Target("rgb(+1.5e+2 0 0)"), new() { MaxNumericTokenCharacters = 7 });
        Assert.AreEqual(PaintInterpretationKind.Literal, valid.Kind);
        PaintLimit("rgb(+1.5e+2 0 0)", new() { MaxNumericTokenCharacters = 6 }, InterpretationLimit.NumericTokenCharacters);
        PaintLimit("var(--x,123)", new() { MaxNumericTokenCharacters = 2 }, InterpretationLimit.NumericTokenCharacters);
        PaintLimit("var(--x,var(--y))", new() { MaxNestingDepth = 1 }, InterpretationLimit.NestingDepth);
        Assert.AreEqual(PaintInterpretationKind.RequiresContext, DefaultInterpreter.InterpretPaint(Target("var(--x,var(--y))"), new() { MaxNestingDepth = 2 }).Kind);
        var one = new InterpretationOptions { MaxNumericTokenCharacters = 1, MaxNestingDepth = 1 };
        Assert.AreEqual(PaintInterpretationKind.Literal, DefaultInterpreter.InterpretPaint(Target("#12345678"), one).Kind);
        Assert.AreEqual(PaintInterpretationKind.RequiresContext, DefaultInterpreter.InterpretPaint(Target("var(--123456, '123456((((')"), one).Kind);
        Assert.AreEqual(PaintInterpretationKind.ResourceReference, DefaultInterpreter.InterpretPaint(Target("url('123456(((((')"), one).Kind);
        Assert.IsNull(DefaultInterpreter.InterpretPaint(Target("\\ 123"), one).ExceededLimit);
        PaintLimit("123 \\", one, InterpretationLimit.NumericTokenCharacters);
        Assert.IsNull(DefaultInterpreter.InterpretPaint(Target("var(123456)"), one).ExceededLimit);
        Assert.IsNull(DefaultInterpreter.InterpretPaint(Target("var(--x, (var(--y)))"), new() { MaxNestingDepth = 2 }).ExceededLimit);
        PaintLimit("var(--x,url(a))", one, InterpretationLimit.NestingDepth);
        var reference = Target("abc", TargetHook.Image);
        Assert.AreEqual(ImageInterpretationKind.RelativeReference, DefaultInterpreter.InterpretImage(reference, new() { MaxReferenceCharacters = 3 }).Kind);
        Assert.AreEqual(InterpretationLimit.ReferenceCharacters, DefaultInterpreter.InterpretImage(reference, new() { MaxReferenceCharacters = 2 }).ExceededLimit);
        var data = Target("data:,payload", TargetHook.Image);
        Assert.AreEqual(ImageInterpretationKind.DataUri, DefaultInterpreter.InterpretImage(data, new() { MaxDataUriHeaderCharacters = 6, MaxReferenceCharacters = 1 }).Kind);
        Assert.AreEqual(InterpretationLimit.DataUriHeaderCharacters, DefaultInterpreter.InterpretImage(data, new() { MaxDataUriHeaderCharacters = 5 }).ExceededLimit);
        Assert.IsNull(DefaultInterpreter.InterpretImage(Target("data:", TargetHook.Image), new() { MaxDataUriHeaderCharacters = 5 }).ExceededLimit);
        Assert.AreEqual(InterpretationLimit.DataUriHeaderCharacters, DefaultInterpreter.InterpretImage(Target("data:x", TargetHook.Image), new() { MaxDataUriHeaderCharacters = 5 }).ExceededLimit);
        Assert.AreEqual(InterpretationLimit.DataUriHeaderCharacters, DefaultInterpreter.InterpretImage(Target("data:bad%;no-comma", TargetHook.Image), new() { MaxDataUriHeaderCharacters = 8 }).ExceededLimit);
    }

    /// <summary>Iteration remains bounded by configured work without recursive descent.</summary>
    [TestMethod]
    public void DeepFunctionsAndOpaquePayload()
    {
        var nested = string.Concat(Enumerable.Repeat("f(", 5000)) + "var(--x)" + new string(')', 5000);
        var target = Target(nested);
        Assert.AreEqual(InterpretationLimit.NestingDepth, DefaultInterpreter.InterpretPaint(target, new() { MaxPaintCharacters = nested.Length }).ExceededLimit);
        Assert.AreEqual(PaintInterpretationKind.RequiresContext, DefaultInterpreter.InterpretPaint(target,
            new() { MaxPaintCharacters = nested.Length, MaxNestingDepth = 5001 }).Kind);
        var raw = "data:," + new string('x', 1024 * 1024) + " not base64 😀 %INVALID";
        var image = Target(raw, TargetHook.Image);
        var result = DefaultInterpreter.InterpretImage(image, new() { MaxReferenceCharacters = 1, MaxDataUriHeaderCharacters = 6 });
        Assert.AreEqual(ImageInterpretationKind.DataUri, result.Kind);
        Assert.IsNull(result.DeclaredMediaType);
        Assert.AreSame(image.Default.Raw, result.Target.Default.Raw);
    }

    /// <summary>Interpretation never mutates raw mixed evidence, inferred defaults, or binding admission.</summary>
    [TestMethod]
    public void EvidenceAndBindingIndependence()
    {
        var template = Inspect("""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:x="http://www.w3.org/1999/xlink" data-svt-version="0" opacity=".2">
              <g data-svt-fill="paint" fill="red" fill-opacity=".1"/>
              <g data-svt-stroke="paint" stroke="#ff0000"/>
              <g data-svt-fill="unsupported" fill="color(display-p3 1 0 0)"/>
              <image data-svt-image="image" x:href="data:image/x%23%5E;base64,invalid"/>
            </svg>
            """);
        var paint = template.Inputs[0];
        Assert.AreEqual(InputDefaultState.Mixed, paint.Default.State);
        foreach (var target in paint.Targets)
        {
            var result = DefaultInterpreter.InterpretPaint(target);
            Assert.AreSame(target, result.Target);
            Assert.AreSame(target.Location, result.Target.Location);
            Color(result.Color, 1, 0, 0, 1);
        }
        var unsupported = template.Inputs[1].Targets[0];
        Assert.AreEqual(PaintInterpretationKind.UnsupportedOrUnrecognized, DefaultInterpreter.InterpretPaint(unsupported).Kind);
        PaintLimit(unsupported.Default.Raw ?? throw new AssertFailedException(), new() { MaxPaintCharacters = 1 }, InterpretationLimit.PaintCharacters);
        var image = DefaultInterpreter.InterpretImage(template.Inputs[2].Targets[0]);
        Assert.AreEqual("image/x#^", image.DeclaredMediaType);
        Assert.AreEqual(DefaultSource.XLinkHref, image.Target.Default.Source);
        var bound = template.Bind([TemplateBinding.Paint("unsupported", "unfamiliar raw value")]);
        Assert.IsNotNull(bound.Document);
        Assert.IsEmpty(bound.Diagnostics);
        Assert.AreEqual(InputDefaultState.Mixed, paint.Default.State);
        Assert.AreEqual("red", paint.Targets[0].Default.Raw);
        Assert.AreEqual("#ff0000", paint.Targets[1].Default.Raw);
        var emptyHref = Inspect("<svg xmlns='http://www.w3.org/2000/svg' xmlns:x='http://www.w3.org/1999/xlink' data-svt-version='0'><image data-svt-image='i' href='' x:href='image.png'/></svg>").Inputs[0].Targets[0];
        Assert.AreEqual(ImageInterpretationKind.Empty, DefaultInterpreter.InterpretImage(emptyHref).Kind);
        Assert.AreEqual(DefaultSource.Href, emptyHref.Default.Source);
        var encoded = EncodedImage.Create(new byte[3], "image/X#^+!");
        Assert.IsNotNull(encoded.Image);
        var replacement = template.Bind([TemplateBinding.ImageContent("image", encoded.Image)]);
        Assert.IsNotNull(replacement.Document);
        var replaced = Inspect(replacement.Document.Xml).Inputs[2].Targets[0];
        Assert.AreEqual("image/x#^+!", DefaultInterpreter.InterpretImage(replaced).DeclaredMediaType);
    }

    /// <summary>Decimal parsing and keyword matching do not depend on the current culture.</summary>
    [TestMethod]
    [DataRow("fr-FR")]
    [DataRow("tr-TR")]
    [DataRow("ar-SA")]
    public void CultureAndPrecision(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Color(DefaultInterpreter.InterpretPaint(Target("rgb(.1 50% 0 / .123456789)")).Color, .1 / 255, .5, 0, .123456789);
            Assert.AreEqual(PaintInterpretationKind.RequiresContext, DefaultInterpreter.InterpretPaint(Target("INITIAL")).Kind);
            var zeros = DefaultInterpreter.InterpretPaint(Target("rgb(-0 -1 -1e-999 / -0)")).Color;
            Assert.IsNotNull(zeros);
            foreach (var zero in new[] { zeros.Red, zeros.Green, zeros.Blue, zeros.Alpha }) Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(zero));
            var hugeHue = DefaultInterpreter.InterpretPaint(Target("hsl(1e308rad 100% 50%)")).Color;
            Assert.IsNotNull(hugeHue);
            Assert.IsTrue(double.IsFinite(hugeHue.Red) && double.IsFinite(hugeHue.Green) && double.IsFinite(hugeHue.Blue));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>Targets and reusable options remain independent under concurrent calls.</summary>
    [TestMethod]
    public void ConcurrentReuse()
    {
        var target = Target("hsl(120 100% 50% / 25%)");
        var image = Target("data:IMAGE/PNG;base64,invalid", TargetHook.Image);
        var options = new InterpretationOptions();
        Parallel.For(0, 128, _ =>
        {
            var result = DefaultInterpreter.InterpretPaint(target, options);
            Assert.AreSame(target, result.Target);
            Color(result.Color, 0, 1, 0, .25);
            Assert.AreEqual("image/png", DefaultInterpreter.InterpretImage(image, options).DeclaredMediaType);
        });
    }

    private static void PaintLimit(string raw, InterpretationOptions options, InterpretationLimit limit)
    {
        var result = DefaultInterpreter.InterpretPaint(Target(raw), options);
        Assert.AreEqual(PaintInterpretationKind.UnsupportedOrUnrecognized, result.Kind);
        Assert.AreEqual(limit, result.ExceededLimit);
        Assert.IsNull(result.Color);
    }

    private static TemplateTarget Target(string? raw, TargetHook hook = TargetHook.Fill)
    {
        var name = hook switch { TargetHook.Image => "image", TargetHook.Text => "text", _ => "g" };
        var field = hook switch { TargetHook.Image => "href", TargetHook.Stroke => "stroke", _ => "fill" };
        var element = new XElement(Svg + name, new XAttribute("data-svt-" + hook.ToString().ToLowerInvariant(), "value"));
        if (raw is not null)
        {
            if (hook == TargetHook.Text) element.Value = raw;
            else element.Add(new XAttribute(field, raw));
        }
        var root = new XElement(Svg + "svg", new XAttribute("data-svt-version", "0"), element);
        return Inspect(root.ToString(SaveOptions.DisableFormatting)).Inputs[0].Targets[0];
    }

    private static InspectedTemplate Inspect(string xml)
    {
        var result = TemplateInspector.Inspect(Encoding.UTF8.GetBytes(xml));
        Assert.IsEmpty(result.Diagnostics);
        Assert.IsNotNull(result.Template);
        return result.Template;
    }

    private static void Color(SrgbColor? color, params double[] expected)
    {
        Assert.IsNotNull(color);
        Assert.AreEqual(expected[0], color.Red, 1e-12);
        Assert.AreEqual(expected[1], color.Green, 1e-12);
        Assert.AreEqual(expected[2], color.Blue, 1e-12);
        Assert.AreEqual(expected[3], color.Alpha, 1e-12);
    }
}
