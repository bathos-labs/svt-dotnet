using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CliFiles = ScalableVectorTemplates.Cli.FileAccess;

namespace ScalableVectorTemplates.Cli.Tests;

/// <summary>Resource and publication boundaries tested without timing-based synchronization.</summary>
[TestClass]
public sealed class BoundaryTests
{
    /// <summary>Input transport caps admit equality and reject the next byte, including BOM bytes.</summary>
    [TestMethod]
    public async Task TransportLimits()
    {
        using var w = new Workspace();
        var sourceSize = Encoding.UTF8.GetByteCount(CliTests.TemplateText);
        foreach (var delta in new[] { 0, -1 })
        {
            w.Write("config.json", JsonSerializer.Serialize(new { format_version = 0, inspection = new { max_source_bytes = sourceSize + delta } }));
            var result = await w.Run("inspect", "template.svg", "--format", "json", "--configuration", "config.json", "--diagnostics", "json");
            Assert.AreEqual(delta == 0 ? 0 : 1, result.ExitCode);
            if (delta < 0) Assert.AreEqual("MaxSourceBytes", (string?)result.Diagnostics[0]?["limit"]);
        }
        var bindingBytes = Encoding.UTF8.GetBytes(CliTests.EmptyBindings);
        await File.WriteAllBytesAsync(w.PathOf("bindings.json"), [0xef, 0xbb, 0xbf, .. bindingBytes]);
        foreach (var delta in new[] { 0, -1 })
        {
            w.Write("config.json", JsonSerializer.Serialize(new { format_version = 0, cli = new { max_bindings_file_bytes = bindingBytes.Length + 3 + delta } }));
            var result = await w.Bind("--configuration", "config.json");
            Assert.AreEqual(delta == 0 ? 0 : 1, result.ExitCode);
            if (delta < 0) Assert.AreEqual("cli.max_bindings_file_bytes", (string?)result.Diagnostics[0]?["limit"]);
        }
        var config = "{\"format_version\":0}";
        w.Write("config.json", config.PadRight(65536));
        Assert.AreEqual(0, (await w.Bind("--configuration", "config.json")).ExitCode);
        w.Write("config.json", config.PadRight(65537));
        var excess = await w.Bind("--configuration", "config.json");
        Assert.AreEqual(2, excess.ExitCode);
        Assert.AreEqual("cli.max_configuration_bytes", (string?)excess.Diagnostics[0]?["limit"]);
        for (var depth = 16; depth <= 17; depth++)
        {
            var json = new string('[', depth) + "0" + new string(']', depth);
            if (depth == 16)
            {
                using var input = new JsonInput(Encoding.UTF8.GetBytes(json), "bindings.json", false, CancellationToken.None);
                Assert.AreEqual(JsonValueKind.Array, input.Root.ValueKind);
            }
            else
            {
                var error = Assert.ThrowsExactly<CliFailure>(() => new JsonInput(Encoding.UTF8.GetBytes(json), "bindings.json", false, CancellationToken.None));
                Assert.AreEqual("cli.max_json_depth", error.Diagnostic.Limit);
            }
        }
    }

    /// <summary>Acquisition stops after the first excess byte, without trusting stream length metadata.</summary>
    [TestMethod]
    public async Task BoundedStreams()
    {
        using var stream = new CountingStream(new byte[100]);
        var error = await Assert.ThrowsExactlyAsync<CliFailure>(() => CliFiles.ReadBoundedAsync(stream, 7, "MaxSourceBytes", "input", "-", CancellationToken.None));
        Assert.AreEqual(8L, stream.Position);
        Assert.AreEqual("MaxSourceBytes", error.Diagnostic.Limit);
        using var equal = new CountingStream(new byte[7]);
        Assert.HasCount(7, await CliFiles.ReadBoundedAsync(equal, 7, "MaxSourceBytes", "input", "-", CancellationToken.None));
    }

    /// <summary>Image bytes and binding counts are bounded before acquisition/capture and include ignored entries.</summary>
    [TestMethod]
    public async Task ImageAndCountPreflight()
    {
        using var w = new Workspace();
        foreach (var limit in new[] { 2, 1 })
        {
            w.Write("config.json", JsonSerializer.Serialize(new { format_version = 0, image = new { max_encoded_image_bytes = limit } }));
            w.Bindings(new { key = "i", kind = "image_content", value = "AAE=", media_type = "image/png" });
            var result = await w.Bind("--configuration", "config.json");
            Assert.AreEqual(limit == 2 ? 0 : 1, result.ExitCode);
            if (limit == 1) Assert.AreEqual("MaxEncodedImageBytes", (string?)result.Diagnostics[0]?["limit"]);
            await File.WriteAllBytesAsync(w.PathOf("image.bin"), [0, 1]);
            w.Bindings(new { key = "i", kind = "image_content", file = "image.bin", media_type = "image/png" });
            Assert.AreEqual(limit == 2 ? 0 : 1, (await w.Bind("--configuration", "config.json")).ExitCode);
        }
        foreach (var limit in new[] { 4, 3 })
        {
            w.Write("config.json", JsonSerializer.Serialize(new { format_version = 0, binding = new { max_total_encoded_image_bytes = limit } }));
            w.Bindings(new { key = "i", kind = "image_content", file = "image.bin", media_type = "image/png" },
                new { key = "missing", kind = "image_content", file = "image.bin", media_type = "image/png" });
            var result = await w.Bind("--configuration", "config.json", "--unknown-keys", "ignore-with-warning");
            Assert.AreEqual(limit == 4 ? 0 : 1, result.ExitCode);
            if (limit == 3) Assert.AreEqual("MaxTotalEncodedImageBytes", (string?)result.Diagnostics[0]?["limit"]);
        }
        w.Write("config.json", "{\"format_version\":0,\"binding\":{\"max_bindings\":1}}");
        w.Bindings(new { key = "i", kind = "image_content", file = "does-not-exist", media_type = "image/png" });
        Assert.AreEqual(3, (await w.Bind("--configuration", "config.json")).ExitCode);
        w.Bindings(new { key = "i", kind = "image_content", file = "does-not-exist", media_type = "image/png" }, new { key = "t", kind = "text", value = "" });
        var count = await w.Bind("--configuration", "config.json");
        Assert.AreEqual("MaxBindings", (string?)count.Diagnostics[0]?["limit"]);
    }

    /// <summary>Output budgets remain library semantics and never truncate the preexisting destination.</summary>
    [TestMethod]
    public async Task OutputAndDiagnosticLimits()
    {
        using var w = new Workspace();
        var success = await w.Bind();
        var size = Encoding.UTF8.GetByteCount(success.Output);
        w.Write("result.svg", "keep");
        foreach (var limit in new[] { size - 1, size })
        {
            w.Write("config.json", JsonSerializer.Serialize(new { format_version = 0, binding = new { max_output_bytes = limit } }));
            var result = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--configuration", "config.json", "--output", "result.svg", "--overwrite", "--diagnostics", "json");
            Assert.AreEqual(limit == size ? 0 : 1, result.ExitCode);
            if (limit < size)
            {
                Assert.AreEqual("keep", await File.ReadAllTextAsync(w.PathOf("result.svg")));
                Assert.AreEqual("svt.limit.exceeded", result.Code);
                Assert.AreEqual("MaxOutputBytes", (string?)result.Diagnostics[0]?["limit"]);
            }
        }
        w.Write("config.json", "{\"format_version\":0,\"binding\":{\"max_diagnostics\":1}}");
        w.Bindings(new { key = "missing1", kind = "text", value = "a" });
        Assert.AreEqual("svt.binding.unknown", (await w.Bind("--configuration", "config.json")).Code);
        w.Bindings(new { key = "missing1", kind = "text", value = "a" }, new { key = "missing2", kind = "text", value = "a" });
        var diagnosticLimit = await w.Bind("--configuration", "config.json");
        Assert.HasCount(1, diagnosticLimit.Diagnostics);
        Assert.AreEqual("MaxDiagnostics", (string?)diagnosticLimit.Diagnostics[0]?["limit"]);
    }

    /// <summary>Publication distinguishes output refusal, input aliases and ordinary I/O errors.</summary>
    [TestMethod]
    public async Task PublicationProtection()
    {
        using var w = new Workspace();
        w.Write("result.svg", "keep");
        var args = new[] { "bind", "template.svg", "--bindings", "bindings.json", "--diagnostics", "json", "--output" };
        var refused = await w.Run([.. args, "result.svg"]);
        Assert.AreEqual(3, refused.ExitCode);
        Assert.AreEqual("svt.cli.output-exists", refused.Code);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(w.PathOf("result.svg")));
        foreach (var input in new[] { "template.svg", "./template.svg", "bindings.json" })
            Assert.AreEqual("svt.cli.output-conflict", (await w.Run([.. args, input, "--overwrite"])).Code);
        w.Write("config.json", "{\"format_version\":0}");
        Assert.AreEqual("svt.cli.output-conflict", (await w.Run([.. args, "config.json", "--overwrite", "--configuration", "config.json"])).Code);
        w.Write("image.bin", "image");
        w.Bindings(new { key = "i", kind = "image_content", file = "image.bin", media_type = "image/png" });
        Assert.AreEqual("svt.cli.output-conflict", (await w.Run([.. args, "image.bin", "--overwrite"])).Code);
        Assert.AreEqual(3, (await w.Run([.. args, "absent/result.svg"])).ExitCode);
        Assert.IsFalse(Directory.Exists(w.PathOf("absent")));
        Assert.AreEqual(0, (await w.Run([.. args, "result.svg", "--overwrite"])).ExitCode);
        Assert.IsTrue((await File.ReadAllTextAsync(w.PathOf("result.svg"))).StartsWith("<svg", StringComparison.Ordinal));
        Assert.IsEmpty(Directory.GetFiles(w.Root, ".svt-*.tmp"));
    }

    /// <summary>Staging permits deterministic race and cancellation tests around the actual commit point.</summary>
    [TestMethod]
    public async Task PublicationRacesAndCancellation()
    {
        using var w = new Workspace();
        var path = w.PathOf("result.svg");
        var inputs = new HashSet<string>(StringComparer.Ordinal);
        using (var stage = await StagedOutput.CreateAsync(path, [1, 2, 3], false, inputs, CancellationToken.None))
        {
            w.Write("result.svg", "concurrent");
            var error = Assert.ThrowsExactly<CliFailure>(() => stage.Commit(false, inputs, CancellationToken.None));
            Assert.AreEqual("svt.cli.output-exists", error.Diagnostic.Code);
            Assert.AreEqual("concurrent", await File.ReadAllTextAsync(path));
        }
        using (var cancellation = new CancellationTokenSource())
        using (var stage = await StagedOutput.CreateAsync(path, [1, 2, 3], true, inputs, cancellation.Token))
        {
            cancellation.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => stage.Commit(true, inputs, cancellation.Token));
            Assert.AreEqual("concurrent", await File.ReadAllTextAsync(path));
        }
        Assert.IsEmpty(Directory.GetFiles(w.Root, ".svt-*.tmp"));
    }

    /// <summary>Resolved paths protect inputs, while hard-link replacement leaves the original file untouched.</summary>
    [TestMethod]
    public async Task LinksAndFilesystemCase()
    {
        using var w = new Workspace();
        // Fail explicitly on unsupported test environments instead of silently claiming link coverage.
        try
        {
            File.CreateSymbolicLink(w.PathOf("link.svg"), "template.svg");
            File.CreateSymbolicLink(w.PathOf("dangling.svg"), "absent.svg");
            File.CreateSymbolicLink(w.PathOf("directory-qualified.svg"), "template.svg/.");
            Directory.CreateSymbolicLink(w.PathOf("alias"), w.Root);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or PlatformNotSupportedException)
        { Assert.Inconclusive("Symbolic link creation is unavailable on this test host."); }
        foreach (var path in new[] { "link.svg", "dangling.svg", "alias/template.svg" })
        {
            var result = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", path, "--overwrite", "--diagnostics", "json");
            Assert.AreEqual("svt.cli.output-conflict", result.Code, path);
        }
        var directoryQualified = await w.Run("inspect", "directory-qualified.svg", "--diagnostics", "json");
        Assert.AreEqual(3, directoryQualified.ExitCode);
        Assert.AreEqual("svt.cli.io", directoryQualified.Code);
        Assert.AreEqual("", directoryQualified.Output);
        var aliases = await w.Run("bind", "alias/template.svg", "--bindings", "bindings.json", "--output", "template.svg", "--overwrite", "--diagnostics", "json");
        Assert.AreEqual("svt.cli.output-conflict", aliases.Code);
        var caseAliasExists = File.Exists(w.PathOf("TEMPLATE.SVG"));
        var caseResult = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "TEMPLATE.SVG", "--overwrite", "--diagnostics", "json");
        Assert.AreEqual(caseAliasExists ? 3 : 0, caseResult.ExitCode);
        if (caseAliasExists) Assert.AreEqual("svt.cli.output-conflict", caseResult.Code);
        else
        {
            w.Write("Template.svg", CliTests.TemplateText);
            // Several fold-equivalent names must not hide an exact entry on a case-sensitive filesystem.
            var exact = await w.Run("inspect", "Template.svg", "--diagnostics", "json");
            Assert.AreEqual(0, exact.ExitCode);
        }
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "ln") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in OperatingSystem.IsWindows()
            ? new[] { "/c", "mklink", "/H", w.PathOf("hard.svg"), w.PathOf("template.svg") }
            : new[] { w.PathOf("template.svg"), w.PathOf("hard.svg") }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start) ?? throw new AssertFailedException("Link utility did not start."))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
        }
        w.Bindings(new { key = "t", kind = "text", value = "changed" });
        var hard = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "hard.svg", "--overwrite", "--diagnostics", "json");
        Assert.AreEqual(0, hard.ExitCode);
        Assert.AreEqual(CliTests.TemplateText, await File.ReadAllTextAsync(w.PathOf("template.svg")));
        Assert.IsTrue((await File.ReadAllTextAsync(w.PathOf("hard.svg"))).Contains("changed", StringComparison.Ordinal));
    }

    /// <summary>Cancellation and broken streams preserve lifecycle distinctions and never roll back committed files.</summary>
    [TestMethod]
    public async Task CancellationAndReportingFailures()
    {
        using var w = new Workspace();
        using var cancellation = new CancellationTokenSource();
        using var input = new CancelReadStream(Encoding.UTF8.GetBytes(CliTests.TemplateText), cancellation);
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var code = await CliApplication.RunAsync(["inspect", "-", "--format", "json", "--diagnostics", "json"], input, output, error, w.Root, cancellation.Token);
        Assert.AreEqual(130, code);
        Assert.AreEqual(0, output.Length);
        var diagnostics = JsonNode.Parse(error.ToArray());
        Assert.AreEqual("cancelled", (string?)diagnostics?["outcome"]);
        using var empty = new MemoryStream();
        using var failedError = new FailWriteStream();
        var published = await CliApplication.RunAsync(["bind", "template.svg", "--bindings", "bindings.json", "--output", "result.svg", "--diagnostics", "json"],
            empty, output, failedError, w.Root);
        Assert.AreEqual(3, published);
        Assert.IsTrue(File.Exists(w.PathOf("result.svg")));
        using var failedOutput = new FailWriteStream();
        using var reporting = new MemoryStream();
        var streamFailure = await CliApplication.RunAsync(["bind", "template.svg", "--bindings", "bindings.json", "--output", "-", "--diagnostics", "json"],
            empty, failedOutput, reporting, w.Root);
        Assert.AreEqual(3, streamFailure);
        Assert.AreEqual("svt.cli.io", (string?)JsonNode.Parse(reporting.ToArray())?["diagnostics"]?[0]?["code"]);
        w.Bindings(new { key = "absent", kind = "text", value = "ignored" });
        using var validationReport = new MemoryStream();
        Assert.AreEqual(1, await CliApplication.RunAsync(["bind", "template.svg", "--bindings", "bindings.json", "--output", "-", "--diagnostics", "json"],
            empty, failedOutput, validationReport, w.Root));
        w.Write("bindings.json", CliTests.EmptyBindings);
        using var afterCommitCancellation = new CancellationTokenSource();
        using var cancellingReport = new CancelWriteStream(afterCommitCancellation);
        Assert.AreEqual(0, await CliApplication.RunAsync(["bind", "template.svg", "--bindings", "bindings.json", "--output", "committed.svg", "--diagnostics", "json"],
            empty, output, cancellingReport, w.Root, afterCommitCancellation.Token));
        Assert.IsTrue(afterCommitCancellation.IsCancellationRequested);
        Assert.IsTrue(File.Exists(w.PathOf("committed.svg")));
        using var brokenInput = new DefectReadStream();
        using var defectReport = new MemoryStream();
        var internalFailure = await CliApplication.RunAsync(["inspect", "-", "--diagnostics", "json"], brokenInput, output, defectReport, w.Root);
        Assert.AreEqual(4, internalFailure);
        Assert.AreEqual("svt.cli.internal", (string?)JsonNode.Parse(defectReport.ToArray())?["diagnostics"]?[0]?["code"]);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override long Length => throw new AssertFailedException("Acquisition must not depend on declared stream length.");
    }
    private sealed class CancelReadStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            cancellation.Cancel();
            return read;
        }
    }
    private sealed class CancelWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
    private sealed class FailWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Deliberate stream failure."));
    }
    private sealed class DefectReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new InvalidOperationException("Deliberate programming defect."));
    }
}
