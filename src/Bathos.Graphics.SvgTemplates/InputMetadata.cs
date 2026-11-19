using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;

namespace Bathos.Graphics.SvgTemplates;

// Temporary resolution state only. The inspected template retains the resolved immutable public model.
internal sealed class InputMetadata
{
    internal static readonly XName SvgMetadata = XName.Get("metadata", "http://www.w3.org/2000/svg");
    private static readonly XNamespace Namespace = "https://bathos-labs.github.io/svt";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal sealed record Fields(string? Label, string? Description, int? Order, string? Group);
    internal Dictionary<string, Fields> Inputs { get; } = new(StringComparer.Ordinal);
    private Dictionary<string, Fields> Groups { get; } = new(StringComparer.Ordinal);

    internal static string? Trim(string? value)
    {
        var text = value?.Trim(' ', '\t', '\r', '\n');
        return string.IsNullOrEmpty(text) ? null : text;
    }

    internal static InputMetadata Resolve(XElement root, HashSet<string> admitted,
        Action<string, string, string?, XObject, IEnumerable<XObject>?> report, CancellationToken token)
    {
        var result = new InputMetadata();
        var containers = new List<(XElement Element, XElement Subject)>();
        var unknownProperties = new List<XElement>();
        void Warn(string code, string message, string? key, XObject node, IEnumerable<XObject>? related = null) => report(code, message, key, node, related);
        foreach (var child in root.Elements())
        {
            token.ThrowIfCancellationRequested();
            if (child.Name != SvgMetadata) continue;
            foreach (var graph in child.Elements())
            {
                token.ThrowIfCancellationRequested();
                if (graph.Name != Rdf + "RDF") continue;
                foreach (var subject in graph.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    if (subject.Name != Rdf + "Description" || (string?)subject.Attribute(Rdf + "about") != "") continue;
                    foreach (var element in subject.Elements())
                    {
                        token.ThrowIfCancellationRequested();
                        if (element.Name == Namespace + "inputs") containers.Add((element, subject));
                        else if (element.Name.Namespace == Namespace) unknownProperties.Add(element);
                    }
                }
            }
        }
        if (containers.Count > 1)
        {
            Warn("svt.metadata.container", "At most one 'svt:inputs' property is allowed across document RDF descriptions; all central metadata is ignored.",
                null, containers[0].Element, containers.Skip(1).Select(entry => entry.Element));
            return result;
        }
        // Cardinality is resolved before envelope diagnostics consume the shared warning budget.
        foreach (var property in unknownProperties)
        {
            token.ThrowIfCancellationRequested();
            Warn("svt.metadata.unknown", "Only 'svt:inputs' is supported as an SVT property of 'rdf:Description'; this element and its subtree are ignored.", null, property);
        }
        if (containers.Count == 0) return result;

        var (container, descriptionSubject) = containers[0];
        // SVT reads a fixed RDF/XML profile, not arbitrary RDF graphs or alternate serializations.
        // Conflicting RDF attributes must not turn a resource/reference into active metadata.
        var literal = container.Attribute(Rdf + "parseType");
        if (literal?.Value != "Literal")
        {
            Warn("svt.metadata.invalid", "The 'svt:inputs' property requires rdf:parseType=\"Literal\"; this metadata container is ignored.", null, literal ?? (XObject)container);
            return result;
        }
        var invalidEnvelope = UnexpectedRdfAttribute(descriptionSubject, "about") ?? UnexpectedRdfAttribute(container, "parseType");
        if (invalidEnvelope is not null)
        {
            var rule = invalidEnvelope.Parent == descriptionSubject
                ? "Only 'rdf:about' is supported as an RDF attribute on 'rdf:Description'"
                : "Only 'rdf:parseType' is supported as an RDF attribute on 'svt:inputs'";
            Warn("svt.metadata.invalid", rule + "; this metadata container is ignored.", null, invalidEnvelope);
            return result;
        }
        var inputs = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        Attributes(container, null);
        Children(container, null, element =>
        {
            var isInput = element.Name == Namespace + "input";
            if (!isInput && element.Name != Namespace + "group") { Unknown(element, null); return; }
            var key = element.Attribute("key");
            if (key is null || key.Value.Length == 0)
            {
                Warn("svt.metadata.invalid", $"'svt:{element.Name.LocalName}' requires a nonempty, unnamespaced 'key' attribute; this declaration is ignored.", null, key ?? (XObject)element);
                return;
            }
            var declarations = isInput ? inputs : groups;
            if (!declarations.TryGetValue(key.Value, out var entries)) declarations.Add(key.Value, entries = []);
            entries.Add(element);
        });
        ResolveDeclarations(groups, result.Groups, false);
        ResolveDeclarations(inputs, result.Inputs, true);
        return result;

        XAttribute? UnexpectedRdfAttribute(XElement element, string allowed)
        {
            foreach (var attribute in element.Attributes())
            {
                token.ThrowIfCancellationRequested();
                if (attribute.Name.Namespace == Rdf && attribute.Name.LocalName != allowed) return attribute;
            }
            return null;
        }

        void ResolveDeclarations(Dictionary<string, List<XElement>> declarations, Dictionary<string, Fields> destination, bool isInput)
        {
            foreach (var (key, entries) in declarations)
            {
                token.ThrowIfCancellationRequested();
                var inputKey = isInput ? key : null;
                var element = entries[0];
                if (entries.Count > 1)
                {
                    Warn("svt.metadata.duplicate", $"Each 'svt:{element.Name.LocalName}' key must be unique; all declarations of this {(isInput ? "input" : "group")} key are ignored.",
                        inputKey, element, entries.Skip(1));
                    continue;
                }
                if (isInput && !admitted.Contains(key))
                {
                    Warn("svt.metadata.input", "The 'key' must reference an admitted artwork input; this declaration is ignored because the input is unknown or rejected.",
                        key, element.Attribute("key") ?? (XObject)element);
                    continue;
                }
                Attributes(element, inputKey, isInput ? ["key", "label", "order", "group"] : ["key", "label", "order"]);
                int? order = null;
                if (element.Attribute("order") is { } orderAttribute)
                {
                    var value = orderAttribute.Value;
                    if (value.Length > 0 && value.All(c => c is >= '0' and <= '9') &&
                        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) order = number;
                    else Warn("svt.metadata.invalid", "The 'order' attribute must contain only ASCII digits representing an integer from 0 through 2147483647, without signs or whitespace; this order is ignored.", inputKey, orderAttribute);
                }
                string? group = null;
                if (isInput && element.Attribute("group") is { } groupAttribute)
                {
                    if (groupAttribute.Value.Length == 0)
                        Warn("svt.metadata.invalid", "The 'group' attribute must contain a nonempty group key; the input remains ungrouped.", key, groupAttribute);
                    else if (!result.Groups.ContainsKey(groupAttribute.Value))
                        Warn("svt.metadata.group", "The 'group' attribute must reference a uniquely declared group; the input remains ungrouped because that group is unknown or discarded.", key, groupAttribute);
                    else group = groupAttribute.Value;
                }
                var descriptions = new List<XElement>();
                Children(element, inputKey, child =>
                {
                    if (child.Name == Namespace + "description") descriptions.Add(child);
                    else Unknown(child, inputKey);
                });
                string? description = null;
                if (descriptions.Count > 1)
                    Warn("svt.metadata.invalid", $"At most one 'svt:description' child is allowed on 'svt:{element.Name.LocalName}'; all descriptions for this declaration are ignored.",
                        inputKey, descriptions[0], descriptions.Skip(1));
                else if (descriptions.Count == 1)
                {
                    var child = descriptions[0];
                    if (child.HasElements)
                        Warn("svt.metadata.invalid", "'svt:description' must contain plain text or CDATA without child elements; this description is ignored.", inputKey, child);
                    else
                    {
                        Attributes(child, inputKey);
                        description = Trim(child.Value);
                    }
                }
                destination.Add(key, new(Trim((string?)element.Attribute("label")), description, order, group));
            }
        }

        void Attributes(XElement element, string? key, params string[] allowed)
        {
            foreach (var attribute in element.Attributes())
            {
                token.ThrowIfCancellationRequested();
                if (attribute.IsNamespaceDeclaration) continue;
                if (attribute.Name.Namespace == Namespace ||
                    (attribute.Name.NamespaceName.Length == 0 && !allowed.Contains(attribute.Name.LocalName, StringComparer.Ordinal)))
                    Warn("svt.metadata.unknown", $"This attribute is not supported on 'svt:{element.Name.LocalName}'; it is ignored. Recognized presentation fields must use unnamespaced attributes.", key, attribute);
            }
        }
        void Children(XElement element, string? key, Action<XElement> visit)
        {
            var unexpectedText = false;
            foreach (var node in element.Nodes())
            {
                token.ThrowIfCancellationRequested();
                if (node is XElement child) visit(child);
                else if (node is XText text && !unexpectedText && Trim(text.Value) is not null)
                {
                    unexpectedText = true;
                    Warn("svt.metadata.invalid", $"Only whitespace is allowed between child elements of 'svt:{element.Name.LocalName}'; this character data is ignored.", key, element);
                }
            }
        }
        void Unknown(XElement element, string? key)
        {
            if (element.Name.Namespace == Namespace)
                Warn("svt.metadata.unknown", "This SVT element is not supported in this position; the element and its entire subtree are ignored.", key, element);
        }
    }

    internal ImmutableArray<TemplateInputGroup> Present(ImmutableArray<TemplateInput> inputs, CancellationToken token)
    {
        var members = new Dictionary<string, List<(TemplateInput Input, int Position)>>(StringComparer.Ordinal);
        var ungrouped = new List<(TemplateInput Input, int Position)>();
        for (var i = 0; i < inputs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var input = inputs[i];
            if (Inputs.TryGetValue(input.Key, out var fields) && fields.Group is { } key)
            {
                if (!members.TryGetValue(key, out var group)) members.Add(key, group = []);
                group.Add((input, i));
            }
            else ungrouped.Add((input, i));
        }
        var named = new List<(TemplateInputGroup Group, int Position)>();
        foreach (var (key, group) in members)
        {
            token.ThrowIfCancellationRequested();
            var position = group[0].Position;
            var fields = Groups[key];
            named.Add((new(key, fields.Label ?? key, fields.Description, fields.Order, OrderInputs(group)), position));
        }
        InspectionOrder.Sort(named, (a, b) => Compare(a.Group.Order, a.Position, b.Group.Order, b.Position), token);
        var output = ImmutableArray.CreateBuilder<TemplateInputGroup>();
        foreach (var entry in named) { token.ThrowIfCancellationRequested(); output.Add(entry.Group); }
        if (ungrouped.Count > 0) output.Add(new(null, null, null, null, OrderInputs(ungrouped)));
        token.ThrowIfCancellationRequested();
        return output.ToImmutable();

        ImmutableArray<TemplateInput> OrderInputs(List<(TemplateInput Input, int Position)> values)
        {
            InspectionOrder.Sort(values, (a, b) => Compare(a.Input.Order, a.Position, b.Input.Order, b.Position), token);
            return values.Select(value => value.Input).ToImmutableArray();
        }
    }

    private static int Compare(int? left, int leftPosition, int? right, int rightPosition)
    {
        var order = (left ?? (long)int.MaxValue + 1).CompareTo(right ?? (long)int.MaxValue + 1);
        return order == 0 ? leftPosition.CompareTo(rightPosition) : order;
    }
}
