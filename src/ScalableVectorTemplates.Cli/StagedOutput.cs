namespace ScalableVectorTemplates.Cli;

internal sealed class StagedOutput : IDisposable
{
    private readonly string _destination;
    private readonly string _temporary;
    private bool _published;
    private bool _ownsTemporary;

    private StagedOutput(string destination, string temporary)
    {
        _destination = destination;
        _temporary = temporary;
    }

    internal static async Task<StagedOutput> CreateAsync(string destination, byte[] content,
        bool overwrite, IReadOnlySet<string> inputs, CancellationToken token)
    {
        destination = Validate(destination, overwrite, inputs, token);
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("Output requires a parent directory.");
        var staged = new StagedOutput(destination, Path.Combine(parent, ".svt-" + Guid.NewGuid().ToString("N") + ".tmp"));
        try
        {
            await using (var stream = new FileStream(staged._temporary, FileMode.CreateNew, System.IO.FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous))
            {
                staged._ownsTemporary = true;
                await stream.WriteAsync(content, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            return staged;
        }
        catch { staged.Dispose(); throw; }
    }

    internal void Commit(bool overwrite, IReadOnlySet<string> inputs, CancellationToken token)
    {
        var resolved = Validate(_destination, overwrite, inputs, token);
        if (resolved != _destination) throw Conflict(_destination);
        token.ThrowIfCancellationRequested();
        try { File.Move(_temporary, _destination, overwrite); }
        catch (IOException) when (!overwrite && (File.Exists(_destination) || Directory.Exists(_destination)))
        { throw Exists(_destination); }
        _published = true;
    }

    private static string Validate(string destination, bool overwrite, IReadOnlySet<string> inputs, CancellationToken token)
    {
        var resolved = FileAccess.Resolve(destination, token, followFinal: false);
        if (new FileInfo(resolved).LinkTarget is not null || inputs.Contains(FileAccess.EntryIdentity(resolved))) throw Conflict(destination);
        if (!overwrite && (File.Exists(resolved) || Directory.Exists(resolved))) throw Exists(destination);
        if (Directory.Exists(resolved)) throw new IOException("The output destination is a directory.");
        return resolved;
    }
    private static CliFailure Conflict(string source) => new(3, new("publication", "svt.cli.output-conflict", "error",
        "Choose an output that is not an input file or a symbolic link.", Source: source));
    private static CliFailure Exists(string source) => new(3, new("publication", "svt.cli.output-exists", "error",
        Directory.Exists(source) ? "The output path is a folder. Choose a filename."
            : "The output file already exists. Choose another name or use --overwrite.", Source: source));

    public void Dispose()
    {
        if (_published || !_ownsTemporary) return;
        try { File.Delete(_temporary); }
        catch (Exception e) when (FileAccess.IsIoFailure(e)) { /* Best-effort cleanup, never mask the primary result. */ }
    }
}
