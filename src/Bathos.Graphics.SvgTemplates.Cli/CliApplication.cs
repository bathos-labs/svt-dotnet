namespace Bathos.Graphics.SvgTemplates.Cli;

internal static class CliApplication
{
    internal static async Task<int> RunAsync(string[] args, Stream standardInput, Stream standardOutput,
        Stream standardError, string workingDirectory, CancellationToken token = default)
    {
        var diagnostics = new List<CliDiagnostic>();
        string? command = args.FirstOrDefault() is "inspect" or "bind" ? args[0] : null;
        var jsonDiagnostics = Invocation.WantsJsonDiagnostics(args);
        var phase = "invocation";
        string? source = null, requestSource = null, pointer = null;
        int? index = null;
        var exitCode = 0;
        CliDiagnostic Context(string code, string message) => new(phase, code, "error", message,
            RelatedLocations: [], Source: source, RequestSource: requestSource, JsonPointer: pointer, AssignmentIndex: index);
        async Task<T> IoAsync<T>(Func<Task<T>> action)
        {
            try { return await action().ConfigureAwait(false); }
            catch (Exception e) when (FileAccess.IsIoFailure(e))
            {
                token.ThrowIfCancellationRequested();
                throw new CliFailure(3, Context("svt.cli.io", IoMessage(e, phase == "publication")));
            }
        }
        T Io<T>(Func<T> action)
        {
            try { return action(); }
            catch (Exception e) when (FileAccess.IsIoFailure(e))
            {
                token.ThrowIfCancellationRequested();
                throw new CliFailure(3, Context("svt.cli.io", IoMessage(e, phase == "publication")));
            }
        }
        try
        {
            var invocation = Invocation.Parse(args);
            command = invocation.Command;
            jsonDiagnostics = invocation.JsonDiagnostics;
            token.ThrowIfCancellationRequested();
            if (invocation.Help || invocation.Version)
            {
                phase = "publication"; source = "-";
                var text = invocation.Help ? CliHelp.ForCommand(command) + "\n" :
                    CliVersion.Display();
                await IoAsync(async () =>
                {
                    await Reports.WriteTextAsync(standardOutput, text, token).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
            }
            else
            {
                var files = new FileAccess(workingDirectory, standardInput);
                var configuration = CliConfiguration.Default;
                if (invocation.Configuration is { } configPath)
                {
                    phase = "configuration"; source = configPath; requestSource = configPath;
                    var bytes = await IoAsync(() => files.ReadAsync(configPath, 65536, "cli.max_configuration_bytes", phase, token, 2)).ConfigureAwait(false);
                    using var input = new JsonInput(bytes, configPath, true, token);
                    configuration = CliConfiguration.Parse(input);
                }
                configuration = configuration.Apply(invocation);
                phase = "input"; source = invocation.Template; requestSource = null;
                var templatePath = invocation.Template ?? throw new InvalidOperationException("Validated command requires a template.");
                var templateBytes = await IoAsync(() => files.ReadAsync(templatePath, configuration.Inspection.MaxSourceBytes,
                    "MaxSourceBytes", phase, token)).ConfigureAwait(false);
                BindingRequest[] requests = [];
                if (invocation.Bindings is { } bindingsPath)
                {
                    source = bindingsPath; requestSource = bindingsPath;
                    var bytes = await IoAsync(() => files.ReadAsync(bindingsPath, configuration.MaxBindingsFileBytes,
                        "cli.max_bindings_file_bytes", phase, token)).ConfigureAwait(false);
                    using var input = new JsonInput(bytes, bindingsPath, false, token);
                    requests = BindingRequest.Parse(input, configuration.Binding.BindingOptions.MaxBindings, token);
                }
                phase = "inspection"; source = templatePath; requestSource = null;
                var inspection = TemplateInspector.Inspect(templateBytes, configuration.Inspection, token);
                diagnostics.AddRange(inspection.Diagnostics.Select(d => CliDiagnostic.FromLibrary(d, phase, templatePath)));
                var inspectionErrors = inspection.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
                if (command == "inspect")
                {
                    exitCode = inspectionErrors || inspection.Template is null ? 1 : 0;
                    phase = "publication"; source = "-";
                    await IoAsync(async () =>
                    {
                        await Reports.InspectAsync(standardOutput, inspection, invocation.JsonReport, token).ConfigureAwait(false);
                        return true;
                    }).ConfigureAwait(false);
                }
                else if (inspection.Template is not { } template || (inspectionErrors && !configuration.AllowScoped)) exitCode = 1;
                else
                {
                    var bindings = new TemplateBinding[requests.Length];
                    var firstIndices = new Dictionary<string, int>(StringComparer.Ordinal);
                    long totalImages = 0;
                    phase = "preparation"; requestSource = invocation.Bindings;
                    var baseDirectory = invocation.Bindings == "-" ? files.WorkingDirectory :
                        Io(() => Path.GetDirectoryName(files.FullPath(invocation.Bindings ?? throw new InvalidOperationException("Missing bindings path."))))
                            ?? throw new InvalidOperationException("Bindings path requires a directory.");
                    for (var n = 0; n < requests.Length; n++)
                    {
                        token.ThrowIfCancellationRequested();
                        var request = requests[n];
                        firstIndices.TryAdd(request.Key, n);
                        index = n; pointer = $"/bindings/{n}"; source = invocation.Bindings;
                        if (request.Kind == "image_content")
                        {
                            var perImage = configuration.Binding.ImageOptions.MaxEncodedImageBytes;
                            var remaining = configuration.Binding.BindingOptions.MaxTotalEncodedImageBytes - totalImages;
                            var maximum = (int)Math.Min(perImage, remaining);
                            var limit = perImage <= remaining ? "MaxEncodedImageBytes" : "MaxTotalEncodedImageBytes";
                            byte[] bytes;
                            if (request.File is { } file)
                            {
                                pointer += "/file";
                                // An image path is always a file, including a literal filename "-".
                                source = file == "-" ? "./-" : file;
                                var imagePath = Io(() =>
                                {
                                    if (Path.EndsInDirectorySeparator(file) || Path.GetFileName(file) is "." or "..")
                                        throw new IOException("Expected an image file path.");
                                    return Path.GetFullPath(file, baseDirectory);
                                });
                                bytes = await IoAsync(() => files.ReadAsync(imagePath, maximum, limit, phase, token)).ConfigureAwait(false);
                            }
                            else
                            {
                                pointer += "/value";
                                bytes = DecodeImage(request.Value ?? throw new InvalidOperationException("Missing image content."), maximum,
                                    limit, source ?? "-", token);
                            }
                            totalImages += bytes.Length;
                            var prepared = EncodedImage.Create(bytes,
                                request.MediaType ?? throw new InvalidOperationException("Missing media type."), configuration.Binding, token);
                            if (prepared.Image is not { } image)
                            {
                                diagnostics.AddRange(prepared.Diagnostics.Select(d => CliDiagnostic.FromLibrary(d, phase, source ?? "-", requestSource, n)));
                                exitCode = 1;
                                break;
                            }
                            bindings[n] = TemplateBinding.ImageContent(request.Key, image);
                        }
                        else
                        {
                            var value = request.Value ?? throw new InvalidOperationException("Missing binding value.");
                            bindings[n] = request.Kind switch
                            {
                                "text" => TemplateBinding.Text(request.Key, value),
                                "paint" => TemplateBinding.Paint(request.Key, value),
                                "image_reference" => TemplateBinding.ImageReference(request.Key, value),
                                _ => throw new InvalidOperationException("Unvalidated binding kind."),
                            };
                        }
                    }
                    if (exitCode == 0)
                    {
                        phase = "binding"; source = templatePath; index = null; pointer = null;
                        var bound = template.Bind(bindings, configuration.Binding, token);
                        foreach (var diagnostic in bound.Diagnostics)
                        {
                            int? assignmentIndex = diagnostic.Key is { } key && firstIndices.TryGetValue(key, out var found) ? found : null;
                            diagnostics.Add(CliDiagnostic.FromLibrary(diagnostic, phase,
                                diagnostic.Location is not null ? templatePath : invocation.Bindings ?? "-", invocation.Bindings, assignmentIndex));
                        }
                        if (bound.Document is not { } document) exitCode = 1;
                        else
                        {
                            phase = "publication"; source = invocation.Output; requestSource = null;
                            var output = document.ToUtf8Bytes();
                            if (invocation.Output == "-")
                            {
                                token.ThrowIfCancellationRequested();
                                await IoAsync(async () =>
                                {
                                    await standardOutput.WriteAsync(output, token).ConfigureAwait(false);
                                    await standardOutput.FlushAsync(token).ConfigureAwait(false);
                                    return true;
                                }).ConfigureAwait(false);
                            }
                            else
                            {
                                var path = Io(() => files.FullPath(invocation.Output ?? throw new InvalidOperationException("Missing output path.")));
                                using var staged = await IoAsync(() => StagedOutput.CreateAsync(path, output, invocation.Overwrite, files.Consumed, token)).ConfigureAwait(false);
                                Io(() => { staged.Commit(invocation.Overwrite, files.Consumed, token); return true; });
                            }
                        }
                    }
                }
            }
        }
        catch (CliFailure failure)
        {
            exitCode = failure.ExitCode;
            var diagnostic = failure.Diagnostic;
            var aggregate = diagnostic.Limit == "MaxTotalEncodedImageBytes";
            diagnostic = diagnostic with
            {
                // File operations may report resolved paths; keep the caller-facing source for diagnostics.
                Source = aggregate ? requestSource : source ?? diagnostic.Source,
                RequestSource = diagnostic.RequestSource ?? requestSource,
                AssignmentIndex = aggregate ? null : diagnostic.AssignmentIndex ?? index,
                JsonPointer = aggregate ? null : diagnostic.JsonPointer ?? pointer,
            };
            diagnostics.Add(diagnostic);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { exitCode = 130; diagnostics.Add(Context("svt.cli.cancelled", "Operation cancelled.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { exitCode = 3; diagnostics.Add(Context("svt.cli.io", IoMessage(e, phase == "publication"))); }
        catch (Exception e) when (e is not OutOfMemoryException)
        { exitCode = 4; diagnostics.Add(Context("svt.cli.internal", "Unexpected internal failure.")); }

        try { await Reports.DiagnosticsAsync(standardError, command, exitCode, diagnostics, jsonDiagnostics, args.Length == 0 && exitCode == 2).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException or NotSupportedException)
        { return 3; } // Never append a second envelope or roll back a published result.
        return exitCode;
    }

    internal static string IoMessage(Exception error, bool writing) => error switch
    {
        FileNotFoundException => "File not found. Check the path.",
        DirectoryNotFoundException => "Folder not found. Check the path.",
        UnauthorizedAccessException or System.Security.SecurityException => "Access denied. Check file permissions.",
        _ => writing ? "Cannot write the file or stream." : "Cannot read the file or stream."
    };

    private static byte[] DecodeImage(string text, int maximum, string limit, string source, CancellationToken token)
    {
        if (text.Length % 4 != 0) throw BadBase64();
        var padding = text.EndsWith("==", StringComparison.Ordinal) ? 2 : text.EndsWith('=') ? 1 : 0;
        var size = (long)text.Length / 4 * 3 - padding;
        if (size > maximum) throw CliFailure.Limit("preparation", source, limit);
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            var c = text[i];
            if (i >= text.Length - padding ? c != '=' : !(char.IsAsciiLetterOrDigit(c) || c is '+' or '/')) throw BadBase64();
        }
        var bytes = new byte[checked((int)size)];
        if (!Convert.TryFromBase64String(text, bytes, out var written) || written != bytes.Length) throw BadBase64();
        token.ThrowIfCancellationRequested();
        return bytes;
    }
    private static CliFailure BadBase64() => new(1, new("preparation", "svt.cli.base64", "error", "Expected standard padded base64 without whitespace."));
}
