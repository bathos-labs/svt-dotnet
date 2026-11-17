using System.Collections.Immutable;
using System.Text;
using System.Xml;

namespace Bathos.Graphics.SvgTemplates;

internal static class BindingOperation
{
    internal static BindingResult Run(InspectedTemplate template, IReadOnlyList<TemplateBinding> bindings,
        BindingConfiguration configuration, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        token.ThrowIfCancellationRequested();
        var options = configuration.BindingOptions;
        try
        {
            var count = bindings.Count;
            BindingLimitException.Check(count, options.MaxBindings, nameof(options.MaxBindings));
            var assignments = new TemplateBinding[count];
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                assignments[i] = bindings[i] ?? throw new ArgumentException("A binding entry cannot be null.", nameof(bindings));
            }
            long characters = 0;
            long imageBytes = 0;
            foreach (var binding in assignments)
            {
                token.ThrowIfCancellationRequested();
                BindingLimitException.Check(binding.Key.Length, options.MaxValueCharacters, nameof(options.MaxValueCharacters));
                BindingLimitException.Check(binding.Value?.Length ?? 0, options.MaxValueCharacters, nameof(options.MaxValueCharacters));
                characters += (long)binding.Key.Length + (binding.Value?.Length ?? 0);
                BindingLimitException.Check(characters, options.MaxTotalValueCharacters, nameof(options.MaxTotalValueCharacters));
                if (binding.Image is { } image)
                {
                    BindingLimitException.Check(image.ByteLength, configuration.ImageOptions.MaxEncodedImageBytes, nameof(EncodedImageOptions.MaxEncodedImageBytes));
                    BindingLimitException.Check(image.MediaType.Length, configuration.ImageOptions.MaxMediaTypeCharacters, nameof(EncodedImageOptions.MaxMediaTypeCharacters));
                    imageBytes += image.ByteLength;
                    BindingLimitException.Check(imageBytes, options.MaxTotalEncodedImageBytes, nameof(options.MaxTotalEncodedImageBytes));
                }
            }
            var inputs = template.Inputs.ToDictionary(input => input.Key, StringComparer.Ordinal);
            var diagnostics = new List<(int Index, Diagnostic Diagnostic)>();
            var groups = assignments.Select((binding, index) => (binding, index)).GroupBy(p => p.binding.Key, StringComparer.Ordinal).ToArray();
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            void Add(int index, string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
            {
                BindingLimitException.Check(diagnostics.Count + 1, options.MaxDiagnostics, nameof(options.MaxDiagnostics));
                var key = assignments[index].Key;
                var locations = inputs.TryGetValue(key, out var input) ? input.Targets.Select(t => t.Location).ToImmutableArray() :
                    template.RejectedLocations.GetValueOrDefault(key, []);
                diagnostics.Add((index, new(code, severity, message, key, locations.IsEmpty ? null : locations[0],
                    locations.IsEmpty ? [] : locations.RemoveAt(0), null)));
            }
            foreach (var group in groups)
            {
                token.ThrowIfCancellationRequested();
                if (group.Skip(1).Any())
                {
                    duplicates.Add(group.Key);
                    Add(group.First().index, "svt.binding.duplicate", "The key has multiple assignments.");
                }
            }
            var accepted = new Dictionary<(int Line, int Column), TemplateBinding>();
            for (var i = 0; i < assignments.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var binding = assignments[i];
                if (duplicates.Contains(binding.Key)) continue;
                if (binding.Key.Length == 0) { Add(i, "svt.binding.key", "An assignment key must not be empty."); continue; }
                if (!inputs.TryGetValue(binding.Key, out var input))
                {
                    if (template.RejectedLocations.ContainsKey(binding.Key)) Add(i, "svt.binding.rejected", "The key has no admitted input.");
                    else Add(i, "svt.binding.unknown", "The key is not known to this template.",
                        options.UnknownKeyBehavior == UnknownKeyBehavior.IgnoreWithWarning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error);
                    continue;
                }
                if (binding.Kind != input.Kind) { Add(i, "svt.binding.kind", "The replacement kind does not match the input."); continue; }
                if (binding.Value is { } value)
                {
                    try { XmlConvert.VerifyXmlChars(value); }
                    catch (XmlException) { Add(i, "svt.binding.value", "The replacement contains characters forbidden in XML 1.0."); continue; }
                }
                foreach (var target in input.Targets) accepted.Add((target.Location.Line, target.Location.Column), binding);
            }
            var findings = diagnostics.OrderBy(d => d.Index).ThenBy(d => d.Diagnostic.Code, StringComparer.Ordinal).Select(d => d.Diagnostic).ToImmutableArray();
            token.ThrowIfCancellationRequested();
            if (findings.Any(d => d.Severity == DiagnosticSeverity.Error)) return new(null, findings);
            var document = Serialize(template.Source, accepted, options.MaxOutputBytes, token);
            token.ThrowIfCancellationRequested();
            return new(document, findings);
        }
        catch (BindingLimitException error)
        {
            token.ThrowIfCancellationRequested();
            return new(null, [error.ToDiagnostic()]);
        }
    }

    private static BoundSvg Serialize(string source, Dictionary<(int Line, int Column), TemplateBinding> accepted,
        int maximum, CancellationToken token)
    {
        using var output = new BoundedOutput(maximum, token);
        using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
        });
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false, true),
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
            CloseOutput = false,
        }))
        {
            var position = (IXmlLineInfo)reader;
            TemplateBinding? text = null;
            var textWritten = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!reader.Read()) break;
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                        var empty = reader.IsEmptyElement;
                        var attributes = new List<(string Prefix, string Local, string Namespace, string Value)>();
                        var replacements = new Dictionary<string, TemplateBinding>(StringComparer.Ordinal);
                        text = null;
                        textWritten = false;
                        while (reader.MoveToNextAttribute())
                        {
                            attributes.Add((reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value));
                            if (accepted.TryGetValue((position.LineNumber, position.LinePosition), out var binding))
                            {
                                if (reader.LocalName == "data-svt-text") text = binding;
                                else replacements.Add(reader.LocalName, binding);
                            }
                        }
                        reader.MoveToElement();
                        foreach (var attribute in attributes)
                        {
                            token.ThrowIfCancellationRequested();
                            TemplateBinding? replacement = null;
                            if (attribute.Namespace.Length == 0 && attribute.Local is "fill" or "stroke")
                                replacements.TryGetValue("data-svt-" + attribute.Local, out replacement);
                            else if (attribute.Local == "href" && attribute.Namespace is "" or "http://www.w3.org/1999/xlink")
                                replacements.TryGetValue("data-svt-image", out replacement);
                            writer.WriteStartAttribute(attribute.Prefix, attribute.Local, attribute.Namespace);
                            if (replacement is null) writer.WriteString(attribute.Value);
                            else WriteValue(writer, replacement, token);
                            writer.WriteEndAttribute();
                        }
                        foreach (var (hook, binding) in replacements)
                        {
                            var attributeName = hook == "data-svt-image" ? "href" : hook[9..];
                            if (attributes.Any(a => a.Namespace.Length == 0 && a.Local == attributeName)) continue;
                            writer.WriteStartAttribute(attributeName);
                            WriteValue(writer, binding, token);
                            writer.WriteEndAttribute();
                        }
                        if (empty)
                        {
                            if (text is not null) WriteValue(writer, text, token);
                            writer.WriteEndElement();
                            text = null;
                        }
                        break;
                    case XmlNodeType.EndElement:
                        if (text is not null && !textWritten) WriteValue(writer, text, token);
                        writer.WriteFullEndElement();
                        text = null;
                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Whitespace:
                        if (text is not null)
                        {
                            if (!textWritten) WriteValue(writer, text, token);
                            textWritten = true;
                        }
                        else if (reader.Depth == 0) writer.WriteWhitespace(reader.Value);
                        else writer.WriteString(reader.Value);
                        break;
                    case XmlNodeType.Comment: writer.WriteComment(reader.Value); break;
                    case XmlNodeType.ProcessingInstruction: writer.WriteProcessingInstruction(reader.Name, reader.Value); break;
                    case XmlNodeType.XmlDeclaration: break;
                    default: throw new InvalidOperationException("Unexpected node in an inspected template.");
                }
            }
        }
        token.ThrowIfCancellationRequested();
        return new(output.GetXml());
    }

    private static void WriteValue(XmlWriter writer, TemplateBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (binding.Image is { } image) image.WriteDataUrl(writer, token);
        else if (binding.Value is { Length: > 0 } value) writer.WriteString(value);
    }

    private sealed class BoundedOutput(int maximum, CancellationToken token) : Stream
    {
        private readonly MemoryStream _buffer = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
        public override void Flush() => token.ThrowIfCancellationRequested();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            var length = _buffer.Length + buffer.Length;
            BindingLimitException.Check(length, maximum, nameof(BindingOptions.MaxOutputBytes));
            if (length > _buffer.Capacity)
                _buffer.Capacity = (int)Math.Min(maximum, Math.Max(length, Math.Max(256L, (long)_buffer.Capacity * 2)));
            _buffer.Write(buffer);
        }
        internal string GetXml() => Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (int)_buffer.Length);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _buffer.Dispose();
            base.Dispose(disposing);
        }
    }
}
