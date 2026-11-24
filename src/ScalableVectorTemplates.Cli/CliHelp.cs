namespace ScalableVectorTemplates.Cli;

internal static class CliHelp
{
    internal const string Overview = """
        Inspect and update SVG templates.

        svt inspect template.svg
        svt bind template.svg --bindings bindings.json --output result.svg

        inspect   Show the template's editable inputs.
        bind      Apply values and save an SVG.

        Use svt <command> --help for options and examples.
        Use --help or -h for help, and --version for the version.
        """;

    internal static string ForCommand(string? command) => command switch
    {
        "inspect" => """
            Show the template's editable inputs and existing values.

            svt inspect TEMPLATE [options]

            --format text|json       Report format (default: text).
            --configuration PATH     Read settings from this file (otherwise use defaults).
            --diagnostics text|json  Error and warning format (default: text, on stderr).
            --help, -h               Show this help without reading files.

            svt inspect template.svg
            svt inspect template.svg --format json

            Use - as TEMPLATE to read stdin. Use -- before a filename beginning with -.
            Long values are shortened in text reports; JSON keeps complete values.
            """,
        "bind" => """
            Apply values to a template and save a new SVG.

            svt bind TEMPLATE --bindings PATH --output PATH [options]

            --bindings PATH          Required JSON file containing replacement values.
            --output PATH            Required destination; use - for SVG stdout.
            --overwrite              Replace an existing output (default: refuse).
            --configuration PATH     Read settings from this file (otherwise use defaults).
            --diagnostics text|json  Error and warning format (default: text, on stderr).
            --unknown-keys error|ignore-with-warning
                                     Unknown binding keys (default: error).
            --inspection-errors fail|allow-scoped
                                     Template errors (default: fail).
            --help, -h               Show this help without reading files.

            svt bind template.svg --bindings bindings.json --output result.svg
            svt bind template.svg --bindings bindings.json --output -

            A useful binding file:
            {"bindings":[{"key":"heading","kind":"text","value":"Hello"}]}
            Use {} to keep the template's existing values. Omitted format_version means 0.
            Settings in a configuration file override defaults; command options override settings.
            Use - for either TEMPLATE or --bindings to read stdin, but not both.
            Use -- before a template filename beginning with -.
            The output must be separate from the files being read. Success without warnings is silent.
            """,
        _ => Overview
    };
}
