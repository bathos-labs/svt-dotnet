using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Bathos.Graphics.SvgTemplates;

/// <summary>Inspects supplied SVG bytes without resource access or value interpretation.</summary>
public static class TemplateInspector
{
    private const string Svg = "http://www.w3.org/2000/svg";
    private static readonly string[] Hooks = ["text", "image", "fill", "stroke"];

    /// <summary>Captures and inspects a source snapshot under positive budgets.</summary>
    public static InspectionResult Inspect(ReadOnlyMemory<byte> source, InspectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limits = (options ?? new InspectionOptions()).Snapshot();
        try
        {
            Check(source.Length, limits.MaxSourceBytes, nameof(limits.MaxSourceBytes));
            var bytes = source.Span;
            cancellationToken.ThrowIfCancellationRequested();
            var text = Decode(bytes, limits.MaxXmlCharacters);
            cancellationToken.ThrowIfCancellationRequested();
            var root = Parse(text, limits, cancellationToken);
            if (root.Name != XName.Get("svg", Svg))
                return Failure("svt.root.invalid", "Expected an SVG root.", Point(root));
            if ((string?)root.Attribute("data-svt-version") != "0")
                return Failure("svt.version.invalid", "Expected version zero.", Point(root.Attribute("data-svt-version") ?? (XObject)root));
            return Discover(text, root, limits, cancellationToken);
        }
        catch (InspectionFailure failure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(failure.Code, failure.Message, failure.Location, failure.Limit);
        }
        catch (DecoderFallbackException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure("svt.xml.invalid", "Invalid encoded XML bytes.");
        }
        catch (XmlException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure("svt.xml.invalid", error.Message,
                error.LineNumber > 0 && error.LinePosition > 0 ? new(error.LineNumber, error.LinePosition) : null);
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes, int maxCharacters)
    {
        Encoding encoding = new UTF8Encoding(false, true);
        var skip = 0;
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) skip = 3;
        else if (bytes.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 }) || bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff }) ||
                 bytes.StartsWith(new byte[] { 0, 0, 0, 0x3c }) || bytes.StartsWith(new byte[] { 0x3c, 0, 0, 0 }))
            throw new InspectionFailure("svt.xml.encoding", "Only UTF-8 and UTF-16 are supported.");
        else if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        else if (bytes.StartsWith(new byte[] { 0x3c, 0 })) encoding = new UnicodeEncoding(false, false, true);
        else if (bytes.StartsWith(new byte[] { 0, 0x3c })) encoding = new UnicodeEncoding(true, false, true);
        var payload = bytes[skip..];
        // Validate the declaration before decoding its payload: an unsupported single-byte
        // encoding can contain bytes that are invalid UTF-8. Let XML validate header syntax.
        var declarationLength = 0;
        if (payload.StartsWith(encoding.GetBytes("<?xml")))
        {
            var terminator = encoding.GetBytes("?>");
            var width = encoding.CodePage == 65001 ? 1 : 2;
            var end = payload.IndexOf(terminator);
            if (end >= 0 && end % width == 0) declarationLength = end + terminator.Length;
        }
        var header = payload[..declarationLength];
        Check(encoding.GetCharCount(header), maxCharacters, nameof(InspectionOptions.MaxXmlCharacters));
        var text = encoding.GetString(header);
        // Parse the declaration with XML itself, avoiding a second declaration grammar.
        using var declaration = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        if (text.StartsWith("<?xml", StringComparison.Ordinal) && declaration.Read() && declaration.NodeType == XmlNodeType.XmlDeclaration)
        {
            var name = declaration.GetAttribute("encoding");
            if (name is not null)
            {
                var utf8 = name.Equals("utf-8", StringComparison.OrdinalIgnoreCase);
                var utf16 = name.Equals("utf-16", StringComparison.OrdinalIgnoreCase);
                var little = name.Equals("utf-16le", StringComparison.OrdinalIgnoreCase);
                var big = name.Equals("utf-16be", StringComparison.OrdinalIgnoreCase);
                if (!utf8 && !utf16 && !little && !big)
                    throw new InspectionFailure("svt.xml.encoding", "Unsupported declared encoding.");
                if ((utf8 && encoding.CodePage != 65001) || (utf16 && encoding.CodePage == 65001) ||
                    (little && encoding.CodePage != 1200) || (big && encoding.CodePage != 1201))
                    throw new InspectionFailure("svt.xml.invalid", "Declaration and byte encoding disagree.");
            }
        }
        Check(encoding.GetCharCount(payload), maxCharacters, nameof(InspectionOptions.MaxXmlCharacters));
        return encoding.GetString(payload);
    }

    private static XElement Parse(string source, InspectionOptions limits, CancellationToken token)
    {
        // Replace only a lexically identified DTD opening with a private marker. This lets
        // XML report earlier malformed content/limits without ever parsing a DTD subset.
        // Matching the marker location prevents authored processing instructions from colliding.
        var dtd = FindDtd(source);
        var dtdPoint = dtd < 0 ? null : PositionAt(source, dtd + 2);
        var parseSource = dtd < 0 ? source : source[..dtd] + "<?svt-rejected-dtd?>";
        using var reader = XmlReader.Create(new StringReader(parseSource), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
        });
        var position = (IXmlLineInfo)reader;
        var stack = new Stack<XElement>();
        XElement? root = null;
        var elements = 0;
        var hooks = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!reader.Read()) break;
            token.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.ProcessingInstruction && dtd >= 0 && reader.Name == "svt-rejected-dtd" &&
                position.LineNumber == dtdPoint?.Line && position.LinePosition == dtdPoint.Column)
                throw new InspectionFailure("svt.xml.dtd", "DTD declarations are prohibited.", new(position.LineNumber, position.LinePosition));
            if (reader.NodeType == XmlNodeType.Element)
            {
                Check(++elements, limits.MaxElements, nameof(limits.MaxElements));
                Check(stack.Count + 1, limits.MaxElementDepth, nameof(limits.MaxElementDepth));
                Check(reader.AttributeCount, limits.MaxAttributesPerElement, nameof(limits.MaxAttributesPerElement));
                var element = new XElement(XName.Get(reader.LocalName, reader.NamespaceURI));
                element.AddAnnotation(new SourceLocation(position.LineNumber, position.LinePosition));
                if (stack.TryPeek(out var parent)) parent.Add(element);
                else if (root is null) root = element;
                else throw new InspectionFailure("svt.xml.invalid", "Multiple root elements.");
                var empty = reader.IsEmptyElement;
                while (reader.MoveToNextAttribute())
                {
                    var name = reader.Prefix == "xmlns" ? XNamespace.Xmlns + reader.LocalName :
                        reader.Name == "xmlns" ? XName.Get("xmlns") : XName.Get(reader.LocalName, reader.NamespaceURI);
                    var attribute = new XAttribute(name, reader.Value);
                    attribute.AddAnnotation(new SourceLocation(position.LineNumber, position.LinePosition));
                    element.Add(attribute);
                    if (reader.NamespaceURI.Length == 0 && Hooks.Any(hook => reader.LocalName == "data-svt-" + hook))
                        Check(++hooks, limits.MaxHooks, nameof(limits.MaxHooks));
                }
                reader.MoveToElement();
                if (!empty) stack.Push(element);
            }
            else if (reader.NodeType == XmlNodeType.EndElement) stack.Pop();
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                if (stack.TryPeek(out var parent)) parent.Add(new XText(reader.Value));
            }
            else if (reader.NodeType == XmlNodeType.EntityReference)
                throw new InspectionFailure("svt.xml.invalid", "Undeclared entity reference.");
        }
        return root ?? throw new InspectionFailure("svt.xml.invalid", "Missing root element.");
    }

    private static InspectionResult Discover(string source, XElement root, InspectionOptions limits, CancellationToken token)
    {
        var diagnostics = new List<Diagnostic>();
        var hookLocations = new Dictionary<string, List<SourceLocation>>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<(string? Label, TemplateTarget Target)>>(StringComparer.Ordinal);
        void Add(string code, DiagnosticSeverity severity, string? key, SourceLocation location, IEnumerable<SourceLocation>? related = null)
        {
            Check(diagnostics.Count + 1, limits.MaxDiagnostics, nameof(limits.MaxDiagnostics));
            var message = code switch
            {
                "svt.hook.target" => "The hook requires a compatible SVG element.",
                "svt.hook.key" => "A hook key must not be empty.",
                "svt.input.ambiguous" => "The key has incompatible or duplicate targets.",
                "svt.input.labels" => "Labels differ; the first target supplies the label.",
                "svt.annotation.unknown" => "Unknown SVT annotation ignored.",
                "svt.version.nested" => "Descendant version marker ignored.",
                "svt.metadata.container" => "Multiple input metadata containers; central metadata ignored.",
                "svt.metadata.duplicate" => "Duplicate metadata identity; all its declarations ignored.",
                "svt.metadata.input" => "Metadata references no admitted input; declaration ignored.",
                "svt.metadata.group" => "Metadata group reference cannot be resolved; input remains ungrouped.",
                "svt.metadata.invalid" => "Invalid metadata envelope, identity, field or text ignored.",
                "svt.metadata.unknown" => "Unknown metadata element or attribute ignored.",
                _ => throw new InvalidOperationException("Unknown diagnostic code."),
            };
            diagnostics.Add(new(code, severity, message, key, location, related?.ToImmutableArray() ?? [], null));
        }
        foreach (var element in BindingElements(root, token))
        {
            token.ThrowIfCancellationRequested();
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.NamespaceName.Length != 0) continue;
                var name = attribute.Name.LocalName;
                if (name == "data-svt-version" && element != root)
                    Add("svt.version.nested", DiagnosticSeverity.Warning, null, Point(attribute));
                else if (name.StartsWith("data-svt-", StringComparison.Ordinal) && name is not "data-svt-version" and not "data-svt-label" && !Hooks.Any(h => name == "data-svt-" + h))
                    Add("svt.annotation.unknown", DiagnosticSeverity.Warning, null, Point(attribute));
            }
            for (var i = 0; i < Hooks.Length; i++)
            {
                var attribute = element.Attribute("data-svt-" + Hooks[i]);
                if (attribute is null) continue;
                var key = attribute.Value;
                if (!hookLocations.TryGetValue(key, out var locations))
                {
                    locations = [];
                    hookLocations.Add(key, locations);
                }
                locations.Add(Point(attribute));
                if (element.Name.NamespaceName != Svg || (i == 0 && element.HasElements))
                { Add("svt.hook.target", DiagnosticSeverity.Error, key, Point(attribute)); continue; }
                if (key.Length == 0)
                { Add("svt.hook.key", DiagnosticSeverity.Error, key, Point(attribute)); continue; }
                var label = InputMetadata.Trim((string?)element.Attribute("data-svt-label"));
                string? raw;
                DefaultSource? origin;
                if (i == 0) { raw = element.Value; origin = DefaultSource.TextContent; }
                else
                {
                    var value = i == 1 ? element.Attribute("href") ?? element.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink")) : element.Attribute(Hooks[i]);
                    raw = value?.Value;
                    origin = i == 2 ? DefaultSource.Fill : i == 3 ? DefaultSource.Stroke : value is null ? null : value.Name.NamespaceName.Length == 0 ? DefaultSource.Href : DefaultSource.XLinkHref;
                }
                var target = new TemplateTarget((TargetHook)i, element.Name.LocalName, Point(attribute), new(raw is null ? TargetDefaultState.Absent : TargetDefaultState.Present, raw, origin));
                if (!groups.TryGetValue(key, out var group)) { group = []; groups.Add(key, group); }
                group.Add((label, target));
            }
        }
        var admittedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, group) in groups)
        {
            token.ThrowIfCancellationRequested();
            var first = group[0];
            var kind = Kind(first.Target.Hook);
            if (group.Any(t => Kind(t.Target.Hook) != kind) || (kind != InputKind.Paint && group.Count > 1))
            { Add("svt.input.ambiguous", DiagnosticSeverity.Error, key, first.Target.Location, group.Skip(1).Select(t => t.Target.Location)); continue; }
            admittedKeys.Add(key);
        }
        var metadata = InputMetadata.Resolve(root, admittedKeys,
            (code, key, node, related) => Add(code, DiagnosticSeverity.Warning, key, Point(node), related?.Select(Point)), token);
        var inputs = ImmutableArray.CreateBuilder<TemplateInput>();
        foreach (var (key, group) in groups)
        {
            token.ThrowIfCancellationRequested();
            if (!admittedKeys.Contains(key)) continue;
            var first = group[0];
            metadata.Inputs.TryGetValue(key, out var fields);
            if (fields?.Label is null)
            {
                var differing = group.Where(t => (t.Label ?? key) != (first.Label ?? key)).Select(t => t.Target.Location).ToArray();
                if (differing.Length > 0) Add("svt.input.labels", DiagnosticSeverity.Warning, key, first.Target.Location, differing);
            }
            var raw = first.Target.Default.Raw;
            var same = group.All(t => t.Target.Default.Raw == raw);
            var state = same ? raw is null ? InputDefaultState.Absent : InputDefaultState.Common : InputDefaultState.Mixed;
            inputs.Add(new(key, fields?.Label ?? first.Label ?? key, Kind(first.Target.Hook), group.Select(t => t.Target).ToImmutableArray(),
                new(state, state == InputDefaultState.Common ? raw : null), fields?.Description,
                fields?.Label is not null ? InputLabelSource.Metadata : first.Label is not null ? InputLabelSource.Element : InputLabelSource.Key,
                fields?.Order));
        }
        var rejected = hookLocations.Where(pair => !admittedKeys.Contains(pair.Key))
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableArray(), StringComparer.Ordinal);
        var resolvedInputs = inputs.ToImmutable();
        var presentation = metadata.Present(resolvedInputs, token);
        InspectionOrder.Sort(diagnostics, (a, b) =>
        {
            var line = Nullable.Compare(a.Location?.Line, b.Location?.Line);
            if (line != 0) return line;
            var column = Nullable.Compare(a.Location?.Column, b.Location?.Column);
            return column != 0 ? column : StringComparer.Ordinal.Compare(a.Code, b.Code);
        }, token);
        return new(new(source, resolvedInputs, presentation) { RejectedLocations = rejected }, diagnostics.ToImmutableArray());
    }

    private static IEnumerable<XElement> BindingElements(XElement root, CancellationToken token)
    {
        // Traverse once without recursion or repeated ancestor scans; skip whole metadata subtrees.
        XNode? node = root;
        while (node is not null)
        {
            token.ThrowIfCancellationRequested();
            if (node is XElement element && element.Name != InputMetadata.SvgMetadata)
            {
                yield return element;
                if (element.FirstNode is { } child) { node = child; continue; }
            }
            while (node is not null && node != root && node.NextNode is null)
            {
                token.ThrowIfCancellationRequested();
                node = node.Parent;
            }
            node = node == root ? null : node?.NextNode;
        }
    }

    private static SourceLocation PositionAt(string source, int offset)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset; i++)
        {
            if (source[i] == '\r')
            {
                if (i + 1 < offset && source[i + 1] == '\n') i++;
                line++;
                column = 1;
            }
            else if (source[i] == '\n') { line++; column = 1; }
            else column++;
        }
        return new(line, column);
    }

    private static int FindDtd(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '<') continue;
            var remaining = source.AsSpan(i);
            string? close = remaining.StartsWith("<!--", StringComparison.Ordinal) ? "-->" :
                remaining.StartsWith("<![CDATA[", StringComparison.Ordinal) ? "]]>" :
                remaining.StartsWith("<?", StringComparison.Ordinal) ? "?>" : null;
            if (close is not null)
            {
                var end = source.IndexOf(close, i + 2, StringComparison.Ordinal);
                if (end < 0) return -1;
                i = end + close.Length - 1;
            }
            else if (remaining.StartsWith("<!DOCTYPE", StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private static InputKind Kind(TargetHook hook) => hook switch { TargetHook.Text => InputKind.Text, TargetHook.Image => InputKind.Image, _ => InputKind.Paint };
    private static SourceLocation Point(XObject node) => node.Annotation<SourceLocation>() ?? throw new InvalidOperationException("Missing parser location.");
    private static void Check(int value, int maximum, string limit)
    {
        if (value > maximum) throw new InspectionFailure("svt.limit.exceeded", "Inspection budget exceeded.", limit: limit);
    }
    private static InspectionResult Failure(string code, string message, SourceLocation? location = null, string? limit = null) =>
        new(null, [new(code, DiagnosticSeverity.Error, message, null, location, [], limit)]);
    private sealed class InspectionFailure(string code, string message, SourceLocation? location = null, string? limit = null) : Exception(message)
    {
        internal string Code { get; } = code;
        internal SourceLocation? Location { get; } = location;
        internal string? Limit { get; } = limit;
    }
}
