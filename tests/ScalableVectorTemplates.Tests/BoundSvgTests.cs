using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace ScalableVectorTemplates.Tests;

/// <summary>Caller-owned UTF-8 output, exact sizing and immutable reuse.</summary>
[TestClass]
public sealed class BoundSvgTests
{
    private const byte Sentinel = 0xcc;
    private const string Source = """
        <?before keep?>
        <?xml-stylesheet href="style.css" type="text/css"?>
        <s:svg xmlns:s="http://www.w3.org/2000/svg" xmlns:x="http://www.w3.org/1999/xlink" data-svt-version="1.0">
          <s:text data-svt-text="t"><!--before-->old<?note keep?><!--after--></s:text>
          <s:text data-svt-text="empty">clear</s:text>
          <s:image data-svt-image="i" href="old" x:href="other"/>
        </s:svg>
        <!--after-->
        """;

    private static InspectedTemplate Inspect()
    {
        var result = TemplateInspector.Inspect(Encoding.UTF8.GetBytes(Source));
        Assert.IsEmpty(result.Diagnostics);
        Assert.IsNotNull(result.Template);
        return result.Template;
    }

    private static BoundSvg Success(BindingResult result)
    {
        Assert.IsEmpty(result.Diagnostics);
        Assert.IsNotNull(result.Document);
        return result.Document;
    }

    internal static void AssertExports(BoundSvg document)
    {
        var expected = Encoding.UTF8.GetBytes(document.Xml);
        CollectionAssert.AreEqual(expected, document.ToUtf8Bytes());
        Assert.AreEqual(expected.Length, document.Utf8ByteCount);
        Assert.IsGreaterThan(0, document.Utf8ByteCount);
        Assert.IsFalse(expected.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.IsNull(XDocument.Parse(document.Xml).Declaration);
        var actual = new byte[document.Utf8ByteCount];
        Assert.IsTrue(document.TryWriteUtf8Bytes(actual, out var written));
        Assert.AreEqual(expected.Length, written);
        CollectionAssert.AreEqual(expected, actual);
    }

    /// <summary>Counts serialized UTF-8, including escaping, empty values and supplementary characters.</summary>
    [TestMethod]
    [DataRow("ASCII")]
    [DataRow("")]
    [DataRow("Café 日本語 e\u0301 😀")]
    [DataRow("<&>\"'\r\n\t")]
    public void TextAndPreservedStructure(string value)
    {
        var template = Inspect();
        AssertExports(Success(template.Bind([])));
        AssertExports(Success(template.Bind([TemplateBinding.Text("t", value), TemplateBinding.Text("empty", "")])));
    }

    /// <summary>Both href attributes, base64 tails and multichunk image encoding count toward output.</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(20000)]
    public void EncodedImageOutput(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        var prepared = EncodedImage.Create(bytes, "image/x&test");
        Assert.IsNotNull(prepared.Image);
        AssertExports(Success(Inspect().Bind([TemplateBinding.ImageContent("i", prepared.Image)])));
    }

    /// <summary>Every short capacity leaves the entire backing buffer untouched, including slices.</summary>
    [TestMethod]
    public void InsufficientCapacityNeverWrites()
    {
        var document = Success(Inspect().Bind([TemplateBinding.Text("t", "Café 😀 <&")]));
        Assert.IsFalse(document.TryWriteUtf8Bytes(default, out var written));
        Assert.AreEqual(0, written);
        Assert.IsFalse(document.TryWriteUtf8Bytes(Span<byte>.Empty, out written));
        Assert.AreEqual(0, written);
        var storage = new byte[document.Utf8ByteCount + 10];
        Array.Fill(storage, Sentinel);
        var original = storage.ToArray();
        for (var length = 0; length < document.Utf8ByteCount; length++)
        {
            written = -1;
            Assert.IsFalse(document.TryWriteUtf8Bytes(storage.AsSpan(3, length), out written));
            Assert.AreEqual(0, written);
            CollectionAssert.AreEqual(original, storage);
        }
        AssertExports(document);
    }

    /// <summary>Warnings and later token cancellation do not invalidate a completed document.</summary>
    [TestMethod]
    public void CompletedDocumentOutlivesBindingCancellationAndWarnings()
    {
        var template = Inspect();
        BoundSvg document;
        using (var cancellation = new CancellationTokenSource())
        {
            var configuration = new BindingConfiguration(new() { UnknownKeyBehavior = UnknownKeyBehavior.IgnoreWithWarning });
            var result = template.Bind([TemplateBinding.Text("t", "Café 😀"), TemplateBinding.Text("unknown", "ignored")],
                configuration, cancellation.Token);
            Assert.IsNotNull(result.Document);
            Assert.HasCount(1, result.Diagnostics);
            Assert.AreEqual("svt.binding.unknown", result.Diagnostics[0].Code);
            Assert.AreEqual(DiagnosticSeverity.Warning, result.Diagnostics[0].Severity);
            document = result.Document;
            cancellation.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => template.Bind([], cancellationToken: cancellation.Token));
            AssertExports(document);
        }
        AssertExports(document);
    }

    /// <summary>Different documents can reuse a pooled-style buffer with the count stored in a separate header.</summary>
    [TestMethod]
    public void SharedStorageRespectsSlicesAndSeparateCountOutput()
    {
        var template = Inspect();
        var large = Success(template.Bind([TemplateBinding.Text("t", new string('x', 2000) + "😀")]));
        var small = Success(template.Bind([TemplateBinding.Text("t", "")]));
        var storage = new byte[sizeof(int) + large.Utf8ByteCount + 19];
        Array.Fill(storage, Sentinel);
        // A count header in the same allocation is valid when it does not overlap the supplied span.
        ref var count = ref MemoryMarshal.Cast<byte, int>(storage.AsSpan(0, sizeof(int)))[0];
        var destination = storage.AsSpan(sizeof(int));
        Assert.IsTrue(large.TryWriteUtf8Bytes(destination, out count));
        Assert.AreEqual(large.Utf8ByteCount, count);
        Assert.IsTrue(destination[..count].SequenceEqual(large.ToUtf8Bytes()));
        var afterLarge = destination.ToArray();
        Assert.IsTrue(small.TryWriteUtf8Bytes(destination, out count));
        Assert.AreEqual(small.Utf8ByteCount, count);
        Assert.IsTrue(destination[..count].SequenceEqual(small.ToUtf8Bytes()));
        Assert.IsTrue(destination[count..].SequenceEqual(afterLarge.AsSpan(count)));
        var afterSmall = destination.ToArray();
        Assert.IsFalse(large.TryWriteUtf8Bytes(destination[..(large.Utf8ByteCount - 1)], out count));
        Assert.AreEqual(0, count);
        Assert.IsTrue(destination.SequenceEqual(afterSmall));
        AssertExports(large);
        AssertExports(small);
    }

    /// <summary>Exact counts survive UTF-8 scalar boundaries and multichunk Unicode serialization.</summary>
    [TestMethod]
    public void UnicodeBoundariesAcrossSerializationChunks()
    {
        var scalars = "\u007f\u0080\u07ff\u0800\ud7ff\ue000\ufffd\U00010000\U0010ffff\ufeff<&\r\n";
        var template = Inspect();
        foreach (var length in new[] { 2047, 2048, 2049, 4095, 4096, 4097 })
        {
            var text = new string('x', length) + scalars + new string('é', length) + "😀";
            var document = Success(template.Bind([TemplateBinding.Text("t", text)]));
            AssertExports(document);
            var xml = XDocument.Parse(document.Xml);
            Assert.AreEqual(text, xml.Descendants().Single(e => (string?)e.Attribute("data-svt-text") == "t").Value);
        }
    }

    /// <summary>Exact and oversized slices change only the output prefix and support stack memory.</summary>
    [TestMethod]
    public void SuccessfulWritesPreserveSurroundingBytes()
    {
        var document = Success(Inspect().Bind([TemplateBinding.Text("t", "é😀")]));
        var expected = document.ToUtf8Bytes();
        foreach (var extra in new[] { 0, 1, 17 })
        {
            var storage = new byte[document.Utf8ByteCount + extra + 10];
            Array.Fill(storage, Sentinel);
            Assert.IsTrue(document.TryWriteUtf8Bytes(storage.AsSpan(3, document.Utf8ByteCount + extra), out var written));
            Assert.AreEqual(document.Utf8ByteCount, written);
            Assert.IsTrue(storage.AsSpan(3, written).SequenceEqual(expected));
            Assert.IsTrue(storage.AsSpan(0, 3).IndexOfAnyExcept(Sentinel) < 0);
            Assert.IsTrue(storage.AsSpan(3 + written).IndexOfAnyExcept(Sentinel) < 0);
        }
        Span<byte> stack = stackalloc byte[document.Utf8ByteCount];
        Assert.IsTrue(document.TryWriteUtf8Bytes(stack, out var stackWritten));
        Assert.AreEqual(expected.Length, stackWritten);
        Assert.IsTrue(stack.SequenceEqual(expected));
    }

    /// <summary>Mixed concurrent exports use disjoint slices; no output mutation affects retained state.</summary>
    [TestMethod]
    public async Task RepeatedConcurrentExportsAreIndependent()
    {
        var document = Success(Inspect().Bind([TemplateBinding.Text("t", "Café 😀")]));
        var xml = document.Xml;
        var expected = document.ToUtf8Bytes();
        var storage = new byte[expected.Length * 16];
        await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
        {
            for (var repeat = 0; repeat < 8; repeat++)
            {
                var destination = storage.AsSpan(index * expected.Length, expected.Length);
                Assert.IsTrue(document.TryWriteUtf8Bytes(destination, out var written));
                Assert.AreEqual(expected.Length, written);
                Assert.IsTrue(destination.SequenceEqual(expected));
                var allocated = document.ToUtf8Bytes();
                CollectionAssert.AreEqual(expected, allocated);
                destination.Clear();
                Array.Fill(allocated, Sentinel);
                Assert.AreEqual(xml, document.Xml);
                Assert.AreEqual(expected.Length, document.Utf8ByteCount);
            }
        })));
        AssertExports(document);
        var first = document.ToUtf8Bytes();
        var second = document.ToUtf8Bytes();
        Assert.AreNotSame(first, second);
        Array.Fill(first, Sentinel);
        CollectionAssert.AreEqual(expected, second);
    }

    /// <summary>Binding admits the exact serialized size; export capacity is independent of admission.</summary>
    [TestMethod]
    public void OutputBudgetRemainsABindingBoundary()
    {
        var template = Inspect();
        var prepared = EncodedImage.Create(new byte[20000], "image/png");
        Assert.IsNotNull(prepared.Image);
        TemplateBinding[] bindings = [TemplateBinding.Text("t", "<&é😀"), TemplateBinding.ImageContent("i", prepared.Image)];
        var expected = Success(template.Bind(bindings)).ToUtf8Bytes();
        var document = Success(template.Bind(bindings, new(new() { MaxOutputBytes = expected.Length })));
        Assert.AreEqual(expected.Length, document.Utf8ByteCount);
        var destination = new byte[expected.Length + 100];
        Assert.IsTrue(document.TryWriteUtf8Bytes(destination, out var written));
        Assert.AreEqual(expected.Length, written);
        Assert.IsTrue(destination.AsSpan(0, written).SequenceEqual(expected));
        var rejected = template.Bind(bindings, new(new() { MaxOutputBytes = expected.Length - 1 }));
        Assert.IsNull(rejected.Document);
        Assert.HasCount(1, rejected.Diagnostics);
        Assert.AreEqual("svt.limit.exceeded", rejected.Diagnostics[0].Code);
        Assert.AreEqual("MaxOutputBytes", rejected.Diagnostics[0].Limit);
        AssertExports(document);
    }
}
