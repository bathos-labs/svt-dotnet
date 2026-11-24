using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ScalableVectorTemplates;

RequireRelease();
if (args.Length != 0) throw new ArgumentException("This runner accepts no arguments.");
const int Samples = 5;
const byte Sentinel = 0xcc;
Check(AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out var tieredCompilation) && !tieredCompilation,
    "Measurements require disabled tiered compilation.");
var cases = new List<object>();
foreach (var large in new[] { false, true })
{
    // All acquisition, preparation, binding and destination allocation precede measurement.
    var document = CreateDocument(large);
    var expected = document.ToUtf8Bytes();
    var destination = new byte[document.Utf8ByteCount];
    Check(expected.Length == document.Utf8ByteCount, "Exact UTF-8 size.");
    Check(expected.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(document.Xml)), "XML byte equivalence.");
    Check(!large || (document.Utf8ByteCount > BindingConfiguration.Default.BindingOptions.MaxOutputBytes * 0.99 &&
        document.Utf8ByteCount <= BindingConfiguration.Default.BindingOptions.MaxOutputBytes), "Near-budget fixture size.");
    Array.Fill(destination, Sentinel);
    Check(!document.TryWriteUtf8Bytes(destination.AsSpan(0, destination.Length - 1), out var shortWritten) && shortWritten == 0,
        "Short destination result.");
    Check(destination.AsSpan().IndexOfAnyExcept(Sentinel) < 0, "Short destination changed.");

    var exportIterations = large ? 8 : 10000;
    var operations = new[]
    {
        new Operation("array-plus-copy", () =>
        {
            var bytes = document.ToUtf8Bytes();
            bytes.AsSpan().CopyTo(destination);
            return bytes.Length;
        }, exportIterations, false, true, document.Utf8ByteCount),
        new Operation("span", () =>
        {
            Check(document.TryWriteUtf8Bytes(destination, out var written), "Span export failed.");
            return written;
        }, exportIterations, true, true, document.Utf8ByteCount),
        new Operation("byte-count", () => document.Utf8ByteCount, 100000, true, false, document.Utf8ByteCount),
        new Operation("insufficient-span", () =>
        {
            Check(!document.TryWriteUtf8Bytes(destination.AsSpan(0, destination.Length - 1), out var written), "Short export succeeded.");
            return written;
        }, 100000, true, false, 0),
    };
    // Warm the same delegates and call sites used below before reading allocation counters.
    foreach (var operation in operations)
    {
        Array.Fill(destination, Sentinel);
        for (var i = 0; i < operation.Iterations; i++)
            Check(operation.Invoke() == operation.ExpectedCount, "Warmup byte count.");
        VerifyDestination(operation);
    }
    var results = operations.ToDictionary(operation => operation.Name, _ => new List<Sample>());
    for (var sample = 0; sample < Samples; sample++)
    {
        // Alternate order so one export path does not always follow the other.
        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[sample % 2 == 0 ? index : operations.Length - 1 - index];
            // Reusing valid output could hide a short call rewriting a prefix, or a no-op export.
            // Reset outside the timed region so neither path measures buffer initialization.
            Array.Fill(destination, Sentinel);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var checksum = 0L;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < operation.Iterations; i++) checksum += operation.Invoke();
            var ticks = Stopwatch.GetTimestamp() - start;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(checksum == (long)operation.ExpectedCount * operation.Iterations, "Measured byte count.");
            Check(!operation.AllocationFree || allocated == 0, "Unexpected managed allocation: " + operation.Name);
            Check(operation.AllocationFree || allocated >= (long)document.Utf8ByteCount * operation.Iterations,
                "Baseline did not observe the expected allocation.");
            VerifyDestination(operation);
            var seconds = (double)ticks / Stopwatch.Frequency;
            results[operation.Name].Add(new(allocated, seconds * 1000 / operation.Iterations,
                operation.ExportsBytes ? (double)document.Utf8ByteCount * operation.Iterations / (1024 * 1024) / seconds : null));
        }
    }
    void VerifyDestination(Operation operation)
    {
        Check(operation.ExportsBytes ? destination.AsSpan().SequenceEqual(expected) :
            destination.AsSpan().IndexOfAnyExcept(Sentinel) < 0, "Destination changed unexpectedly: " + operation.Name);
    }
    cases.Add(new
    {
        name = large ? "near-budget-images" : "small-unicode",
        utf8_bytes = document.Utf8ByteCount,
        max_output_bytes = BindingConfiguration.Default.BindingOptions.MaxOutputBytes,
        measurements = operations.Select(operation => new
        {
            operation = operation.Name,
            warmup_iterations = operation.Iterations,
            iterations_per_sample = operation.Iterations,
            bytes_allocated_per_operation = results[operation.Name].Select(sample => (double)sample.AllocatedBytes / operation.Iterations).ToArray(),
            median_milliseconds_per_operation = results[operation.Name].Select(sample => sample.MillisecondsPerOperation).Order().ElementAt(Samples / 2),
            median_mebibytes_per_second = operation.ExportsBytes
                ? results[operation.Name].Select(sample => sample.MebibytesPerSecond).Order().ElementAt(Samples / 2) : null,
            samples = results[operation.Name],
        }).ToArray(),
    });
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    status = "passed",
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    server_gc = GCSettings.IsServerGC,
    tiered_compilation = tieredCompilation,
    stopwatch_frequency = Stopwatch.Frequency,
    samples_per_operation = Samples,
    cases,
}, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

static BoundSvg CreateDocument(bool large)
{
    const string source = """
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:x="http://www.w3.org/1999/xlink" data-svt-version="0">
          <text data-svt-text="heading"/>
          <image data-svt-image="first" href="old" x:href="old"/>
          <image data-svt-image="second" href="old" x:href="old"/>
        </svg>
        """;
    var inspection = TemplateInspector.Inspect(Encoding.UTF8.GetBytes(source));
    Check(inspection.Diagnostics.IsEmpty, "Inspection diagnostics.");
    var template = inspection.Template ?? throw new InvalidOperationException("Inspection failed.");
    var bindings = new List<TemplateBinding> { TemplateBinding.Text("heading", string.Concat(Enumerable.Repeat("Café 日本語 😀 <&> ", 16))) };
    if (large)
    {
        // Two image assignments, each expanded into href and x:href, approach the default 64 MiB
        // output budget while staying within both image input budgets. No image decoding occurs.
        var bytes = new byte[12 * 1024 * 1024 - 1024];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);
        var prepared = EncodedImage.Create(bytes, "image/png");
        Check(prepared.Diagnostics.IsEmpty, "Image diagnostics.");
        var image = prepared.Image ?? throw new InvalidOperationException("Image preparation failed.");
        bindings.Add(TemplateBinding.ImageContent("first", image));
        bindings.Add(TemplateBinding.ImageContent("second", image));
    }
    var result = template.Bind(bindings);
    Check(result.Diagnostics.IsEmpty, "Binding diagnostics.");
    return result.Document ?? throw new InvalidOperationException("Binding failed.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RequireRelease()
{
#if DEBUG
    throw new InvalidOperationException("Run export measurements in Release configuration.");
#endif
}

internal sealed record Operation(string Name, Func<int> Invoke, int Iterations, bool AllocationFree, bool ExportsBytes, int ExpectedCount);
internal sealed record Sample(long AllocatedBytes, double MillisecondsPerOperation, double? MebibytesPerSecond);
