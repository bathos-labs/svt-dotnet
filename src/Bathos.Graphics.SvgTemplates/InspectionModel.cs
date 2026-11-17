using System.Collections.Immutable;

namespace Bathos.Graphics.SvgTemplates;

/// <summary>Identifies InputKind values.</summary>
public enum InputKind
{
    /// <summary>Text.</summary>
    Text,
    /// <summary>Image.</summary>
    Image,
    /// <summary>Paint.</summary>
    Paint,
}

/// <summary>Identifies TargetHook values.</summary>
public enum TargetHook
{
    /// <summary>Text.</summary>
    Text,
    /// <summary>Image.</summary>
    Image,
    /// <summary>Fill.</summary>
    Fill,
    /// <summary>Stroke.</summary>
    Stroke,
}

/// <summary>Identifies TargetDefaultState values.</summary>
public enum TargetDefaultState
{
    /// <summary>Present.</summary>
    Present,
    /// <summary>Absent.</summary>
    Absent,
}

/// <summary>Identifies InputDefaultState values.</summary>
public enum InputDefaultState
{
    /// <summary>Common.</summary>
    Common,
    /// <summary>Mixed.</summary>
    Mixed,
    /// <summary>Absent.</summary>
    Absent,
}

/// <summary>Identifies DefaultSource values.</summary>
public enum DefaultSource
{
    /// <summary>TextContent.</summary>
    TextContent,
    /// <summary>Fill.</summary>
    Fill,
    /// <summary>Stroke.</summary>
    Stroke,
    /// <summary>Href.</summary>
    Href,
    /// <summary>XLinkHref.</summary>
    XLinkHref,
}

/// <summary>Identifies DiagnosticSeverity values.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Error.</summary>
    Error,
    /// <summary>Warning.</summary>
    Warning,
}

/// <summary>Immutable SourceLocation evidence.</summary>
public sealed class SourceLocation
{
    internal SourceLocation(int line, int column)
    {
        Line = line;
        Column = column;
    }
    /// <summary>Gets the Line.</summary>
    public int Line { get; }
    /// <summary>Gets the Column.</summary>
    public int Column { get; }
}

/// <summary>Immutable TargetDefault evidence.</summary>
public sealed class TargetDefault
{
    internal TargetDefault(TargetDefaultState state, string? raw, DefaultSource? source)
    {
        State = state;
        Raw = raw;
        Source = source;
    }
    /// <summary>Gets the State.</summary>
    public TargetDefaultState State { get; }
    /// <summary>Gets the Raw.</summary>
    public string? Raw { get; }
    /// <summary>Gets the Source.</summary>
    public DefaultSource? Source { get; }
}

/// <summary>Immutable InputDefault evidence.</summary>
public sealed class InputDefault
{
    internal InputDefault(InputDefaultState state, string? raw)
    {
        State = state;
        Raw = raw;
    }
    /// <summary>Gets the State.</summary>
    public InputDefaultState State { get; }
    /// <summary>Gets the Raw.</summary>
    public string? Raw { get; }
}

/// <summary>Immutable TemplateTarget evidence.</summary>
public sealed class TemplateTarget
{
    internal TemplateTarget(TargetHook hook, string elementName, SourceLocation location, TargetDefault @default)
    {
        Hook = hook;
        ElementName = elementName;
        Location = location;
        Default = @default;
    }
    /// <summary>Gets the Hook.</summary>
    public TargetHook Hook { get; }
    /// <summary>Gets the ElementName.</summary>
    public string ElementName { get; }
    /// <summary>Gets the Location.</summary>
    public SourceLocation Location { get; }
    /// <summary>Gets the Default.</summary>
    public TargetDefault Default { get; }
}

/// <summary>Immutable TemplateInput evidence.</summary>
public sealed class TemplateInput
{
    internal TemplateInput(string key, string label, InputKind kind, ImmutableArray<TemplateTarget> targets, InputDefault @default)
    {
        Key = key;
        Label = label;
        Kind = kind;
        Targets = targets;
        Default = @default;
    }
    /// <summary>Gets the Key.</summary>
    public string Key { get; }
    /// <summary>Gets the Label.</summary>
    public string Label { get; }
    /// <summary>Gets the Kind.</summary>
    public InputKind Kind { get; }
    /// <summary>Gets the Targets.</summary>
    public ImmutableArray<TemplateTarget> Targets { get; }
    /// <summary>Gets the Default.</summary>
    public InputDefault Default { get; }
}

/// <summary>Immutable Diagnostic evidence.</summary>
public sealed class Diagnostic
{
    internal Diagnostic(string code, DiagnosticSeverity severity, string message, string? key, SourceLocation? location, ImmutableArray<SourceLocation> relatedLocations, string? limit)
    {
        Code = code;
        Severity = severity;
        Message = message;
        Key = key;
        Location = location;
        RelatedLocations = relatedLocations;
        Limit = limit;
    }
    /// <summary>Gets the Code.</summary>
    public string Code { get; }
    /// <summary>Gets the Severity.</summary>
    public DiagnosticSeverity Severity { get; }
    /// <summary>Gets the Message.</summary>
    public string Message { get; }
    /// <summary>Gets the Key.</summary>
    public string? Key { get; }
    /// <summary>Gets the Location.</summary>
    public SourceLocation? Location { get; }
    /// <summary>Gets the RelatedLocations.</summary>
    public ImmutableArray<SourceLocation> RelatedLocations { get; }
    /// <summary>Gets the Limit.</summary>
    public string? Limit { get; }
}

/// <summary>Immutable InspectionResult evidence.</summary>
public sealed class InspectionResult
{
    internal InspectionResult(InspectedTemplate? template, ImmutableArray<Diagnostic> diagnostics)
    {
        Template = template;
        Diagnostics = diagnostics;
    }
    /// <summary>Gets the Template.</summary>
    public InspectedTemplate? Template { get; }
    /// <summary>Gets the Diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

/// <summary>An independently owned source snapshot and discovered inputs.</summary>
public sealed class InspectedTemplate
{
    internal InspectedTemplate(string source, ImmutableArray<TemplateInput> inputs)
    {
        Source = source;
        Inputs = inputs;
    }
    internal string Source { get; }
    /// <summary>Gets inputs in discovery order.</summary>
    public ImmutableArray<TemplateInput> Inputs { get; }
}
