namespace Bathos.Graphics.SvgTemplates.Cli;

internal sealed record Invocation(string? Command, string? Template, string? Bindings, string? Output,
    string? Configuration, bool Overwrite, bool JsonReport, bool JsonDiagnostics,
    string? UnknownKeys, string? InspectionErrors, bool Help, bool Version)
{
    internal const string HelpText = """
        Usage:
          svt inspect TEMPLATE [--format text|json] [--configuration PATH] [--diagnostics text|json]
          svt bind TEMPLATE --bindings PATH --output PATH [--overwrite]
                   [--configuration PATH] [--diagnostics text|json]
                   [--unknown-keys error|ignore-with-warning] [--inspection-errors fail|allow-scoped]
          svt --help
          svt inspect --help
          svt bind --help
          svt --version
        Use - for one template/bindings stdin input or for SVG stdout. Use -- to end options.
        Binding JSON: {"bindings":[{"key":"heading","kind":"text","value":"Hello"}]}
        Omitted format_version means 0; omitted bindings means no assignments ({}).
        Configuration is explicit; omitted settings use library defaults. Existing output requires --overwrite.
        """;

    // Recognize presentation independently so usage errors can still be machine-readable.
    internal static bool WantsJsonDiagnostics(string[] args)
    {
        for (var i = 0; i < args.Length && args[i] != "--"; i++)
            if (args[i] == "--diagnostics" && i + 1 < args.Length) return args[i + 1] == "json";
        return false;
    }

    internal static Invocation Parse(string[] args)
    {
        if (args.Length == 0) throw Usage("A command is required.\n" + HelpText);
        if (args is ["--help"]) return new(null, null, null, null, null, false, false, false, null, null, true, false);
        if (args is ["--version"]) return new(null, null, null, null, null, false, false, false, null, null, false, true);
        var command = args[0];
        if (command is not ("inspect" or "bind")) throw Usage("Expected inspect or bind.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string? template = null;
        var ended = false;
        for (var i = 1; i < args.Length; i++)
        {
            var argument = args[i];
            if (!ended && argument == "--") { ended = true; continue; }
            if (!ended && argument.StartsWith('-') && argument != "-")
            {
                var allowed = argument is "--configuration" or "--diagnostics" or "--help" ||
                    (command == "inspect" && argument == "--format") ||
                    (command == "bind" && argument is "--bindings" or "--output" or "--overwrite" or "--unknown-keys" or "--inspection-errors");
                if (!allowed) throw Usage("Unknown option: " + argument);
                if (options.ContainsKey(argument)) throw Usage("Repeated option: " + argument);
                var value = "true";
                if (argument is not ("--help" or "--overwrite"))
                {
                    if (++i == args.Length || (args[i].StartsWith('-') && args[i] != "-"))
                        throw Usage("Missing value for " + argument);
                    value = args[i];
                    if (value.Length == 0) throw Usage("An option value must not be empty.");
                }
                options.Add(argument, value);
            }
            else
            {
                if (template is not null || argument.Length == 0) throw Usage("Exactly one nonempty template operand is required.");
                template = argument;
            }
        }
        string? Get(string name) => options.GetValueOrDefault(name);
        static void Choice(string? value, string a, string b)
        {
            if (value is not null && value != a && value != b) throw Usage("Invalid option value: " + value);
        }
        Choice(Get("--format"), "text", "json");
        Choice(Get("--diagnostics"), "text", "json");
        Choice(Get("--unknown-keys"), "error", "ignore-with-warning");
        Choice(Get("--inspection-errors"), "fail", "allow-scoped");
        var help = options.ContainsKey("--help");
        if (!help && (template is null || (command == "bind" && (Get("--bindings") is null || Get("--output") is null))))
            throw Usage("A template, and for bind both --bindings and --output, are required.");
        if (template == "-" && Get("--bindings") == "-") throw Usage("Only one input may consume stdin.");
        if (Get("--configuration") == "-") throw Usage("Configuration must name a file.");
        if (Get("--output") == "-" && options.ContainsKey("--overwrite")) throw Usage("Overwrite cannot be used with stdout.");
        return new(command, template, Get("--bindings"), Get("--output"), Get("--configuration"),
            options.ContainsKey("--overwrite"), Get("--format") == "json", Get("--diagnostics") == "json",
            Get("--unknown-keys"), Get("--inspection-errors"), help, false);
    }

    private static CliFailure Usage(string message) => new(2, new("invocation", "svt.cli.usage", "error", message));
}
