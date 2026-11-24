using System.Text.Json.Nodes;
using CliFiles = ScalableVectorTemplates.Cli.FileAccess;

namespace ScalableVectorTemplates.Cli.Tests;

/// <summary>Regression coverage for CLI contract review findings.</summary>
[TestClass]
public sealed class ReviewTests
{
    /// <summary>Directory-qualified file operands must never silently become files.</summary>
    [TestMethod]
    public async Task DirectoryQualifiedFilePathsFailWithoutPublication()
    {
        using var w = new Workspace();
        foreach (var suffix in new[] { "/", "/.", "/missing/.." })
        {
            var result = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", "new.svg" + suffix, "--diagnostics", "json");
            Assert.AreEqual(3, result.ExitCode, suffix);
            Assert.AreEqual("svt.cli.io", result.Code);
            Assert.IsFalse(File.Exists(w.PathOf("new.svg")));
            Assert.IsFalse(Directory.Exists(w.PathOf("new.svg")));
        }
        var input = await w.Run("inspect", "template.svg/", "--diagnostics", "json");
        Assert.AreEqual(3, input.ExitCode);
        Assert.AreEqual("", input.Output);
        var bindings = await w.Run("bind", "template.svg", "--bindings", "bindings.json/", "--output", "-", "--diagnostics", "json");
        Assert.AreEqual(3, bindings.ExitCode);
        w.Write("image.bin", "bytes");
        foreach (var path in new[] { "image.bin/", "image.bin/." })
        {
            w.Bindings(new { key = "i", kind = "image_content", file = path, media_type = "image/png" });
            Assert.AreEqual(3, (await w.Bind()).ExitCode);
        }
    }

    /// <summary>Aggregate budgets identify the request rather than inventing one responsible assignment.</summary>
    [TestMethod]
    public async Task AggregateImageBudgetHasNoAssignmentLocation()
    {
        using var w = new Workspace();
        w.Write("config.json", "{\"format_version\":0,\"binding\":{\"max_total_encoded_image_bytes\":3}}");
        foreach (var fromFile in new[] { false, true })
        {
            await File.WriteAllBytesAsync(w.PathOf("image.bin"), [0, 1]);
            if (fromFile)
                w.Bindings(new { key = "i", kind = "image_content", file = "image.bin", media_type = "image/png" },
                    new { key = "missing", kind = "image_content", file = "image.bin", media_type = "image/png" });
            else
                w.Bindings(new { key = "i", kind = "image_content", value = "AAE=", media_type = "image/png" },
                    new { key = "missing", kind = "image_content", value = "AAE=", media_type = "image/png" });
            var result = await w.Bind("--configuration", "config.json");
            Assert.AreEqual(1, result.ExitCode);
            Assert.AreEqual("MaxTotalEncodedImageBytes", (string?)result.Diagnostics[0]?["limit"]);
            Assert.IsNull(result.Diagnostics[0]?["assignment_index"]);
            Assert.IsNull(result.Diagnostics[0]?["json_pointer"]);
            Assert.AreEqual("bindings.json", (string?)result.Diagnostics[0]?["source"]);
        }
    }

    /// <summary>No-diagnostic text success never accesses stderr; JSON reporting failures remain I/O failures.</summary>
    [TestMethod]
    public async Task SilentSuccessDoesNotAccessStderr()
    {
        using var w = new Workspace();
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var closed = new MemoryStream();
        closed.Dispose();
        Assert.AreEqual(0, await CliApplication.RunAsync(["inspect", "template.svg"], input, output, closed, w.Root));
        Assert.AreEqual(0, await CliApplication.RunAsync(["bind", "template.svg", "--bindings", "bindings.json", "--output", "silent.svg"], input, output, closed, w.Root));
        Assert.IsTrue(File.Exists(w.PathOf("silent.svg")));
        using var readOnly = new MemoryStream([], writable: false);
        Assert.AreEqual(3, await CliApplication.RunAsync(["bind", "template.svg", "--bindings", "bindings.json", "--output", "reported.svg", "--diagnostics", "json"], input, output, readOnly, w.Root));
        Assert.IsTrue(File.Exists(w.PathOf("reported.svg")));
    }

    /// <summary>Closed external streams are I/O failures rather than internal defects.</summary>
    [TestMethod]
    public async Task ClosedStreamsReportIoFailures()
    {
        using var w = new Workspace();
        using var closed = new MemoryStream();
        closed.Dispose();
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new MemoryStream();
        Assert.AreEqual(3, await CliApplication.RunAsync(["inspect", "-", "--diagnostics", "json"], closed, output, errors, w.Root));
        Assert.AreEqual("svt.cli.io", (string?)JsonNode.Parse(errors.ToArray())?["diagnostics"]?[0]?["code"]);
        foreach (var args in new string[][]
        {
            ["inspect", "--help", "--diagnostics", "json"],
            ["inspect", "template.svg", "--format", "json", "--diagnostics", "json"],
            ["bind", "template.svg", "--bindings", "bindings.json", "--output", "-", "--diagnostics", "json"],
        })
        {
            errors.SetLength(0);
            Assert.AreEqual(3, await CliApplication.RunAsync(args, input, closed, errors, w.Root));
            Assert.AreEqual("svt.cli.io", (string?)JsonNode.Parse(errors.ToArray())?["diagnostics"]?[0]?["code"]);
            Assert.AreEqual("-", (string?)JsonNode.Parse(errors.ToArray())?["diagnostics"]?[0]?["source"]);
        }
    }

    /// <summary>Normalized directory-entry aliases cannot overwrite a consumed input.</summary>
    [TestMethod]
    public async Task NormalizedInputAliasesRemainProtected()
    {
        using var w = new Workspace();
        var input = w.PathOf("template.svg");
        var aliases = new List<string> { input, Path.Combine(w.Root, ".", "template.svg") };
        if (OperatingSystem.IsWindows())
        {
            // Runtime Windows assertions are exercised by the platform CI job.
            aliases.Add(@"\\?\" + input);
            aliases.Add(@"\\.\" + input);
            Assert.AreEqual(@"\\SERVER\SHARE\file.svg", CliFiles.EntryIdentity(@"\\?\UNC\SERVER\SHARE\file.svg"));
        }
        foreach (var alias in aliases)
        {
            var result = await w.Run("bind", "template.svg", "--bindings", "bindings.json", "--output", alias, "--overwrite", "--diagnostics", "json");
            Assert.AreEqual(3, result.ExitCode, alias);
            Assert.AreEqual("svt.cli.output-conflict", result.Code);
            var reverse = await w.Run("bind", alias, "--bindings", "bindings.json", "--output", "template.svg", "--overwrite", "--diagnostics", "json");
            Assert.AreEqual("svt.cli.output-conflict", reverse.Code);
            Assert.AreEqual(CliTests.TemplateText, await File.ReadAllTextAsync(input));
        }
    }

    /// <summary>A later preparation failure retains the inspection evidence that recovery explicitly allowed.</summary>
    [TestMethod]
    public async Task PreparationFailureRetainsEarlierFindings()
    {
        using var w = new Workspace();
        w.Write("template.svg", CliTests.TemplateText.Replace("</svg>", "<text data-svt-text='bad'/><text data-svt-text='bad'/></svg>", StringComparison.Ordinal));
        w.Bindings(new { key = "i", kind = "image_content", file = "missing.png", media_type = "image/png" });
        var result = await w.Bind("--inspection-errors", "allow-scoped");
        Assert.AreEqual(3, result.ExitCode);
        Assert.HasCount(2, result.Diagnostics);
        Assert.AreEqual("inspection", (string?)result.Diagnostics[0]?["phase"]);
        Assert.AreEqual("error", (string?)result.Diagnostics[0]?["severity"]);
        Assert.AreEqual("preparation", (string?)result.Diagnostics[1]?["phase"]);
        Assert.AreEqual("svt.cli.io", (string?)result.Diagnostics[1]?["code"]);
        Assert.AreEqual("", result.Output);
    }

    /// <summary>Cancellation observed while a read fails remains cancellation, not an I/O diagnostic.</summary>
    [TestMethod]
    public async Task CancelledReadFailureRemainsCancellation()
    {
        using var w = new Workspace();
        using var cancellation = new CancellationTokenSource();
        using var input = new CancelAndFailReadStream(cancellation);
        using var output = new MemoryStream();
        using var errors = new MemoryStream();
        var exit = await CliApplication.RunAsync(["inspect", "-", "--diagnostics", "json"], input, output, errors, w.Root, cancellation.Token);
        Assert.AreEqual(130, exit);
        Assert.AreEqual("svt.cli.cancelled", (string?)JsonNode.Parse(errors.ToArray())?["diagnostics"]?[0]?["code"]);
        Assert.AreEqual(0, output.Length);
    }

    private sealed class CancelAndFailReadStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromException<int>(new IOException("Read interrupted."));
        }
    }
}
