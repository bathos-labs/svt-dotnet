using System.Text;
using System.Xml.Linq;

namespace ScalableVectorTemplates.Tests;

/// <summary>Observable metadata admission, recovery, presentation and preservation contracts.</summary>
[TestClass]
public sealed class MetadataTests
{
    private const string Start = "<svg xmlns='http://www.w3.org/2000/svg' xmlns:s='https://svt-project.github.io/ns/svt' xmlns:r='http://www.w3.org/1999/02/22-rdf-syntax-ns#' data-svt-version='0'>";
    private const string Paint = "<rect data-svt-fill='a' data-svt-stroke='b' data-svt-label='Inline' fill='red' stroke='none'/>";
    private static string Envelope(string declarations) => "<metadata><r:RDF><r:Description r:about=''><s:inputs r:parseType='Literal'>" + declarations + "</s:inputs></r:Description></r:RDF></metadata>";
    private static string Xml(string declarations, string artwork = Paint) => Start + Envelope(declarations) + artwork + "</svg>";
    private static InspectionResult Inspect(string xml, InspectionOptions? options = null) => TemplateInspector.Inspect(Encoding.UTF8.GetBytes(xml), options);
    private static InspectedTemplate Template(InspectionResult result)
    {
        Assert.IsNotNull(result.Template);
        return result.Template;
    }
    private static string[] Keys(TemplateInputGroup group) => group.Inputs.Select(input => input.Key).ToArray();

    /// <summary>The RDF profile is namespace-based and can coexist with unrelated document properties.</summary>
    [TestMethod]
    public void RdfEnvelopeNamespacesAndOtherResources()
    {
        var xml = Xml("<s:input key='a' label='Central'/>")
            .Replace("<r:Description r:about=''>", """
                <r:Description r:about='#other'><s:inputs r:parseType='Literal'><s:input key='a' label='Other'/></s:inputs></r:Description>
                <r:Description><s:inputs r:parseType='Literal'><s:input key='a' label='Anonymous'/></s:inputs></r:Description>
                <r:Description r:about=''><dc:title xmlns:dc='http://purl.org/dc/elements/1.1/'>Title</dc:title>
                """, StringComparison.Ordinal);
        foreach (var variant in new[]
        {
            xml,
            xml.Replace("xmlns:r=", "xmlns:rdf=", StringComparison.Ordinal).Replace("r:", "rdf:", StringComparison.Ordinal)
                .Replace("xmlns:s=", "xmlns:svt=", StringComparison.Ordinal).Replace("<s:", "<svt:", StringComparison.Ordinal).Replace("</s:", "</svt:", StringComparison.Ordinal),
            xml.Replace("<r:RDF>", "<RDF xmlns='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>", StringComparison.Ordinal)
                .Replace("</r:RDF>", "</RDF>", StringComparison.Ordinal).Replace("r:Description", "Description", StringComparison.Ordinal),
            xml.Replace("data-svt-version='0'", "data-svt-version='0' xml:base='https://example.test/template.svg'", StringComparison.Ordinal)
        })
        {
            var result = Inspect(variant);
            Assert.IsEmpty(result.Diagnostics);
            var template = Template(result);
            Assert.AreEqual("Central", template.Inputs[0].Label);
            var bound = template.Bind([TemplateBinding.Paint("a", "blue")]);
            Assert.IsNotNull(bound.Document);
            Assert.IsEmpty(bound.Diagnostics);
            Assert.IsTrue(XNode.DeepEquals(XElement.Parse(variant).Element(InputMetadataName()),
                XElement.Parse(bound.Document.Xml).Element(InputMetadataName())));
            Assert.AreEqual("Central", Template(Inspect(bound.Document.Xml)).Inputs[0].Label);
        }
    }

    /// <summary>Namespace names match exactly; unrecognized metadata is preserved without affecting hooks.</summary>
    [TestMethod]
    [DataRow("urn:example:foreign")]
    [DataRow("https://svt-project.github.io/ns/svt/")]
    [DataRow("https://svt-project.github.io/ns/SVT")]
    [DataRow("http://svt-project.github.io/ns/svt")]
    public void UnrecognizedMetadataNamespaceIsOpaque(string namespaceName)
    {
        var xml = Xml("<s:group key='g' label='Group'/><s:input key='a' label='Central' order='1' group='g'><s:description>Help</s:description></s:input>")
            .Replace("https://svt-project.github.io/ns/svt", namespaceName, StringComparison.Ordinal);
        var result = Inspect(xml);
        Assert.IsEmpty(result.Diagnostics);
        var template = Template(result);
        Assert.HasCount(2, template.Inputs);
        var input = template.Inputs[0];
        Assert.AreEqual("Inline", input.Label);
        Assert.AreEqual(InputLabelSource.Element, input.LabelSource);
        Assert.IsNull(input.Description);
        Assert.IsNull(input.Order);
        Assert.HasCount(1, template.InputGroups);
        Assert.IsNull(template.InputGroups[0].Key);
        var bound = template.Bind([TemplateBinding.Paint("a", "blue")]);
        Assert.IsNotNull(bound.Document);
        Assert.IsEmpty(bound.Diagnostics);
        var output = XElement.Parse(bound.Document.Xml);
        Assert.IsTrue(XNode.DeepEquals(XElement.Parse(xml).Element(InputMetadataName()), output.Element(InputMetadataName())));
        Assert.AreEqual("blue", (string?)output.Element(XName.Get("rect", "http://www.w3.org/2000/svg"))?.Attribute("fill"));
        Assert.AreEqual("Inline", Template(Inspect(bound.Document.Xml)).Inputs[0].Label);
    }

    /// <summary>Other subjects, namespaces and placements cannot activate the XML literal.</summary>
    [TestMethod]
    [DataRow("<metadata>{0}</metadata>")]
    [DataRow("<metadata><r:RDF>{0}</r:RDF></metadata>")]
    [DataRow("<metadata><r:Description r:about=''>{0}</r:Description></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description>{0}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description about=''>{0}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description r:about='#other'>{0}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description r:about=' '>{0}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><s:Document r:about=''>{0}</s:Document></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF xmlns:r='urn:wrong'><r:Description r:about=''>{0}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description r:about=''><s:wrapper>{0}</s:wrapper></r:Description></r:RDF></metadata>", true)]
    [DataRow("<g><metadata><r:RDF><r:Description r:about=''>{0}</r:Description></r:RDF></metadata></g>")]
    public void RdfPlacementDoesNotSearchOrInferSubjects(string layout, bool unknownWrapper = false)
    {
        var xml = Start + string.Format(System.Globalization.CultureInfo.InvariantCulture, layout,
            "<s:inputs r:parseType='Literal'><s:input key='a' label='Ignored'/></s:inputs>") + Paint + "</svg>";
        var result = Inspect(xml);
        Assert.HasCount(unknownWrapper ? 1 : 0, result.Diagnostics);
        if (unknownWrapper) Assert.AreEqual("svt.metadata.unknown", result.Diagnostics.Single().Code);
        Assert.AreEqual("Inline", Template(result).Inputs[0].Label);
    }

    /// <summary>A property must explicitly carry an XML literal; invalid markers suppress its descendants.</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("parseType='Literal'")]
    [DataRow("s:parseType='Literal'")]
    [DataRow("r:parseType=''")]
    [DataRow("r:parseType='literal'")]
    [DataRow("r:parseType=' Literal '")]
    [DataRow("r:parseType='Resource'")]
    [DataRow("r:parseType='Collection'")]
    public void InvalidRdfLiteralMarker(string marker)
    {
        var xml = Xml("<s:input key='a' label='Central' order='bad'/><s:input/>")
            .Replace("r:parseType='Literal'", marker, StringComparison.Ordinal);
        var result = Inspect(xml);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("svt.metadata.invalid", diagnostic.Code);
        Assert.IsNull(diagnostic.Key);
        Assert.IsEmpty(diagnostic.RelatedLocations);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(diagnostic.Message, "rdf:parseType=\"Literal\"");
        StringAssert.Contains(diagnostic.Message, "container is ignored");
        var position = marker.StartsWith("r:", StringComparison.Ordinal)
            ? xml.IndexOf(marker, StringComparison.Ordinal) + 1
            : xml.IndexOf("<s:inputs", StringComparison.Ordinal) + 2;
        Assert.AreEqual(position, diagnostic.Location?.Column);
        Assert.AreEqual("Inline", Template(result).Inputs[0].Label);
        Assert.IsNotNull(Template(result).Bind([TemplateBinding.Paint("a", "blue")]).Document);
    }

    /// <summary>Additional RDF syntax cannot reinterpret an admitted document description or literal.</summary>
    [TestMethod]
    [DataRow("r:about=''", "r:nodeID='node'")]
    [DataRow("r:about=''", "r:ID='id'")]
    [DataRow("r:parseType='Literal'", "r:resource='other.svg'")]
    [DataRow("r:parseType='Literal'", "r:datatype='urn:datatype'")]
    [DataRow("r:parseType='Literal'", "r:ID='statement'")]
    public void ConflictingRdfAttributesDiscardContainer(string anchor, string attribute)
    {
        var xml = Xml("<s:input key='a' label='Central'/><s:input/>")
            .Replace(anchor, anchor + " " + attribute, StringComparison.Ordinal);
        var result = Inspect(xml);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("svt.metadata.invalid", diagnostic.Code);
        Assert.IsNull(diagnostic.Key);
        StringAssert.Contains(diagnostic.Message, anchor == "r:about=''" ? "Only 'rdf:about'" : "Only 'rdf:parseType'");
        StringAssert.Contains(diagnostic.Message, "container is ignored");
        Assert.AreEqual(xml.IndexOf(attribute, StringComparison.Ordinal) + 1, diagnostic.Location?.Column);
        Assert.AreEqual("Inline", Template(result).Inputs[0].Label);
    }

    /// <summary>Envelope validation has defined stage precedence, then source order within a stage.</summary>
    [TestMethod]
    public void RdfEnvelopeDiagnosticPrecedence()
    {
        var xml = Xml("<s:input key='a' label='Central' order='bad'/><s:input/>")
            .Replace("r:about=''", "r:about='' r:nodeID='node' r:ID='id'", StringComparison.Ordinal)
            .Replace("r:parseType='Literal'", "r:datatype='urn:type' r:resource='other' r:parseType='Resource'", StringComparison.Ordinal);
        foreach (var (attribute, authored) in new[]
        {
            ("r:parseType", "r:parseType='Resource'"), ("r:nodeID", "r:nodeID='node'"),
            ("r:ID", "r:ID='id'"), ("r:datatype", "r:datatype='urn:type'"), ("r:resource", "r:resource='other'")
        })
        {
            var result = Inspect(xml, new() { MaxDiagnostics = 1 });
            var diagnostic = result.Diagnostics.Single();
            Assert.AreEqual("svt.metadata.invalid", diagnostic.Code);
            Assert.IsNull(diagnostic.Key);
            Assert.AreEqual(xml.IndexOf(attribute, StringComparison.Ordinal) + 1, diagnostic.Location?.Column);
            Assert.AreEqual("Inline", Template(result).Inputs[0].Label);
            xml = attribute == "r:parseType"
                ? xml.Replace(authored, "r:parseType='Literal'", StringComparison.Ordinal)
                : xml.Replace(authored, "", StringComparison.Ordinal);
        }
        // Once the envelope is valid, the declaration's own independent findings become visible.
        Assert.HasCount(2, Inspect(xml).Diagnostics);
    }

    /// <summary>Central labels distinguish multiple hooks on an element and unify shared paints.</summary>
    [TestMethod]
    public void ResolvedFieldsAndObjectIdentity()
    {
        var result = Inspect(Xml("""
            <s:group key='colors' label=' Colors ' order='10'><s:description> Help </s:description></s:group>
            <s:input key='a' label=' Accent ' order='20' group='colors'><s:description> A<![CDATA[ & B]]><!-- comment --><?p x?>
            C </s:description></s:input>
            <s:input key='b' label='Outline' order='10' group='colors'/>
            """, Paint + "<path data-svt-stroke='a' stroke='red'/>"));
        Assert.IsEmpty(result.Diagnostics);
        var template = Template(result);
        Sequence(template.Inputs.Select(i => i.Key).ToArray(), "a", "b");
        Assert.AreEqual("Accent", template.Inputs[0].Label);
        Assert.AreEqual("A & B\nC", template.Inputs[0].Description);
        Assert.AreEqual(InputLabelSource.Metadata, template.Inputs[0].LabelSource);
        Assert.AreEqual("red", template.Inputs[0].Default.Raw);
        Assert.HasCount(2, template.Inputs[0].Targets);
        var group = template.InputGroups.Single();
        Assert.AreEqual("colors", group.Key);
        Assert.AreEqual("Colors", group.Label);
        Assert.AreEqual("Help", group.Description);
        Assert.AreEqual(10, group.Order);
        Sequence(Keys(group), "b", "a");
        Assert.AreSame(template.Inputs[1], group.Inputs[0]);
        Assert.AreSame(template.Inputs[0], group.Inputs[1]);
    }

    /// <summary>Provenance describes the source even when labels equal the key; later labels never supply fallback.</summary>
    [TestMethod]
    public void LabelProvenanceAndConflictSuppression()
    {
        const string artwork = "<g data-svt-fill='a'/><g data-svt-stroke='a' data-svt-label='Later'/>" +
            "<g data-svt-fill='b' data-svt-label='b'/><g data-svt-fill='c' data-svt-label='  '/>";
        var result = Inspect(Xml("<s:input key='a' label='a'/>", artwork), new() { MaxDiagnostics = 1 });
        Assert.IsEmpty(result.Diagnostics);
        Sequence(Template(result).Inputs.Select(i => i.LabelSource).ToArray(), InputLabelSource.Metadata, InputLabelSource.Element, InputLabelSource.Key);
        result = Inspect(Xml("<s:input key='a' label=' &#x9; '><s:description>Help</s:description></s:input>", artwork));
        Assert.AreEqual("svt.input.labels", result.Diagnostics.Single().Code);
        Assert.AreEqual("a", Template(result).Inputs[0].Label);
        Assert.AreEqual(InputLabelSource.Key, Template(result).Inputs[0].LabelSource);
        Assert.AreEqual("Help", Template(result).Inputs[0].Description);
        Assert.IsNull(Template(result).Inputs[2].Description);
    }

    /// <summary>Group ties use first structural membership, while input ranks remain local to each section.</summary>
    [TestMethod]
    public void DeterministicGroupAndMemberOrdering()
    {
        var declarations = new[]
        {
            "<s:group key='y' order='10'/>", "<s:group key='x' label=' ' order='10'/>",
            "<s:group key='z'/>", "<s:group key='empty' order='0'/>",
            "<s:input key='a' group='x' order='2147483647'/>",
            "<s:input key='b' group='y'/>", "<s:input key='c' group='y' order='0'/>",
            "<s:input key='d' order='0'/>", "<s:input key='e' group='z'/>",
            "<s:input key='f' group='x'/>", "<s:input key='g' group='x' order='2147483647'/>"
        };
        var artwork = string.Concat("abcdefg".Select(key => $"<g data-svt-fill='{key}'/>"));
        foreach (var sequence in new[] { declarations, declarations.Reverse().ToArray() })
        {
            var result = Inspect(Xml(string.Concat(sequence), artwork));
            Assert.IsEmpty(result.Diagnostics);
            var template = Template(result);
            var groups = template.InputGroups;
            Sequence(groups.Select(g => g.Key).ToArray(), "x", "y", "z", null);
            Assert.AreEqual("x", groups[0].Label);
            Sequence(Keys(groups[0]), "a", "g", "f");
            Sequence(Keys(groups[1]), "c", "b");
            Sequence(Keys(groups[3]), "d");
            var members = groups.SelectMany(g => g.Inputs).ToArray();
            Assert.AreEqual(template.Inputs.Length, members.Length);
            foreach (var input in template.Inputs) Assert.AreSame(input, members.Single(member => member.Key == input.Key));
            Assert.IsNull(groups[3].Label);
            Assert.IsNull(groups[3].Description);
            Assert.IsNull(groups[3].Order);
        }
    }

    /// <summary>Order has a culture-independent ASCII integer grammar with field-local failure.</summary>
    [TestMethod]
    [DataRow("0", 0)]
    [DataRow("00010", 10)]
    [DataRow("00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000002147483647", int.MaxValue)]
    [DataRow("2147483647", int.MaxValue)]
    [DataRow("&#48;", 0)]
    [DataRow("2147483648", -1)]
    [DataRow("-1", -1)]
    [DataRow("+1", -1)]
    [DataRow(" 1", -1)]
    [DataRow("1 ", -1)]
    [DataRow("", -1)]
    [DataRow("1.0", -1)]
    [DataRow("1e1", -1)]
    [DataRow("١", -1)]
    public void OrderGrammar(string value, int expected)
    {
        var result = Inspect(Xml($"<s:group key='g' order='{value}'/><s:input key='a' group='g' order='{value}' label='Central'/>"));
        var template = Template(result);
        Assert.AreEqual(expected < 0 ? null : (int?)expected, template.Inputs[0].Order);
        Assert.AreEqual(expected < 0 ? null : (int?)expected, template.InputGroups[0].Order);
        Assert.AreEqual("Central", template.Inputs[0].Label);
        Assert.HasCount(expected < 0 ? 2 : 0, result.Diagnostics);
        Assert.IsTrue(result.Diagnostics.All(d => d.Code == "svt.metadata.invalid" && d.Severity == DiagnosticSeverity.Warning));
    }

    /// <summary>Duplicate identities suppress descendant/reference diagnostics even when a declaration is otherwise invalid.</summary>
    [TestMethod]
    public void DuplicateRecoveryIsNotFirstValidWins()
    {
        var result = Inspect(Xml("""
            <s:input key='a' label='First' order='bad'><s:unknown/></s:input>
            <s:input key='a' label='Second'/>
            <s:input key='missing' order='bad'/><s:input key='missing'/>
            <s:group key='g' order='bad'/><s:group key='g'/>
            <s:input key='b' group='g' label='Outline' order='2'/>
            """));
        Sequence(result.Diagnostics.Select(d => d.Code).ToArray(), "svt.metadata.duplicate", "svt.metadata.duplicate", "svt.metadata.duplicate", "svt.metadata.group");
        Sequence(result.Diagnostics.Select(d => d.Key).ToArray(), "a", "missing", null, "b");
        Assert.IsTrue(result.Diagnostics.Take(3).All(d => d.RelatedLocations.Length == 1));
        var template = Template(result);
        Assert.AreEqual("Inline", template.Inputs[0].Label);
        Assert.IsNull(template.Inputs[0].Order);
        Assert.AreEqual("Outline", template.Inputs[1].Label);
        Sequence(Keys(template.InputGroups.Single()), "b", "a");
    }

    /// <summary>Ambiguous containers disable all central metadata without cascading field warnings.</summary>
    [TestMethod]
    public void MultipleContainersAndRelatedLocations()
    {
        var first = Envelope("<s:input/>").Replace("r:parseType='Literal'", "wrong='x'", StringComparison.Ordinal);
        var xml = Start + "\r\n" + first + "\r\n" + Envelope("<s:input key='a' label='Central'/>") + Paint + "</svg>";
        var result = Inspect(xml);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("svt.metadata.container", diagnostic.Code);
        Assert.IsNull(diagnostic.Key);
        Assert.AreEqual(2, diagnostic.Location?.Line);
        Assert.AreEqual(first.IndexOf("<s:inputs", StringComparison.Ordinal) + 2, diagnostic.Location?.Column);
        Assert.AreEqual(3, diagnostic.RelatedLocations.Single().Line);
        Assert.AreEqual("Inline", Template(result).Inputs[0].Label);
        Assert.IsNull(Template(result).InputGroups.Single().Key);
    }

    /// <summary>Container ambiguity takes precedence over envelope findings and their diagnostic budget.</summary>
    [TestMethod]
    [DataRow("<metadata><r:RDF><r:Description r:about=''><s:unknown/>{0}{1}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description r:about=''><s:unknown/>{0}</r:Description><r:Description r:about=''>{1}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description r:about=''><s:unknown/>{0}</r:Description></r:RDF><r:RDF><r:Description r:about=''>{1}</r:Description></r:RDF></metadata>")]
    [DataRow("<metadata><r:RDF><r:Description r:about=''><s:unknown/>{0}</r:Description></r:RDF></metadata><metadata><r:RDF><r:Description r:about=''>{1}</r:Description></r:RDF></metadata>")]
    public void ContainerAmbiguitySuppressesEnvelopeDiagnostics(string layout)
    {
        const string first = "<s:inputs r:parseType='Resource'><s:input/></s:inputs>";
        const string second = "<s:inputs r:parseType='Literal'><s:input key='a' label='Central'/></s:inputs>";
        var xml = Start + string.Format(System.Globalization.CultureInfo.InvariantCulture, layout, first, second) + Paint + "</svg>";
        var result = Inspect(xml, new() { MaxDiagnostics = 1 });
        var template = Template(result);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("svt.metadata.container", diagnostic.Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.IsNull(diagnostic.Key);
        Assert.AreEqual(xml.IndexOf(first, StringComparison.Ordinal) + 2, diagnostic.Location?.Column);
        Assert.AreEqual(xml.IndexOf(second, StringComparison.Ordinal) + 2, diagnostic.RelatedLocations.Single().Column);
        Assert.AreEqual("Inline", template.Inputs[0].Label);
        Assert.IsNull(template.InputGroups.Single().Key);
        Assert.IsNotNull(template.Bind([TemplateBinding.Paint("a", "blue")]).Document);
    }

    /// <summary>Unknown document properties are diagnosed when container ambiguity does not suppress them.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnknownRdfPropertyWithoutAmbiguity(bool includeInputs)
    {
        var property = includeInputs ? "<s:inputs r:parseType='Literal'><s:input key='a' label='Central'/></s:inputs>" : "";
        var result = Inspect(Start + "<metadata><r:RDF><r:Description r:about=''><s:unknown>" +
            "<s:input key='a' label='Hidden'/></s:unknown>" + property + "</r:Description></r:RDF></metadata>" + Paint + "</svg>",
            new() { MaxDiagnostics = 1 });
        Assert.AreEqual("svt.metadata.unknown", result.Diagnostics.Single().Code);
        Assert.AreEqual(includeInputs ? "Central" : "Inline", Template(result).Inputs[0].Label);
    }

    /// <summary>Unknown/rejected references and empty identities do not create inputs or cascades.</summary>
    [TestMethod]
    public void RejectedReferencesAndInvalidIdentities()
    {
        var result = Inspect(Xml("""
            <s:input label='Missing key' order='bad'><s:unknown/></s:input>
            <s:input key='' group='unknown'/><s:group key='' order='bad'/>
            <s:input key='unknown' order='bad'/><s:input key='rejected' label='Cannot rescue'/>
            <s:input key='a' group='' label='Valid'/>
            """, Paint + "<text data-svt-text='rejected'/><text data-svt-text='rejected'/>"));
        Sequence(result.Diagnostics.Select(d => d.Code).ToArray(), "svt.metadata.invalid", "svt.metadata.invalid", "svt.metadata.invalid", "svt.metadata.input", "svt.metadata.input", "svt.metadata.invalid", "svt.input.ambiguous");
        Assert.IsTrue(result.Diagnostics.Take(3).All(d => d.Key is null));
        Assert.HasCount(2, Template(result).Inputs);
        Assert.AreEqual("Valid", Template(result).Inputs[0].Label);
        var rejected = Template(result).Bind([TemplateBinding.Text("rejected", "x")]);
        Assert.IsNull(rejected.Document);
        Assert.AreEqual("svt.binding.rejected", rejected.Diagnostics.Single().Code);
        Assert.AreEqual("svt.binding.unknown", Template(result).Bind([TemplateBinding.Text("unknown", "x")]).Diagnostics.Single().Code);
    }

    /// <summary>Optional fields recover independently and descriptions never flatten nested markup.</summary>
    [TestMethod]
    [DataRow("<s:description>One</s:description><s:description wrong='x'><s:unknown/></s:description>")]
    [DataRow("<s:description>Before<s:unknown/>After</s:description>")]
    [DataRow("<s:description>Before<f:b xmlns:f='urn:foreign'/>After</s:description>")]
    public void InvalidDescriptionDoesNotDiscardOtherFields(string content)
    {
        var result = Inspect(Xml("<s:input key='a' label='Central' order='7'>" + content + "</s:input>"));
        Assert.AreEqual("svt.metadata.invalid", result.Diagnostics.Single().Code);
        var input = Template(result).Inputs[0];
        Assert.IsNull(input.Description);
        Assert.AreEqual("Central", input.Label);
        Assert.AreEqual(7, input.Order);
    }

    /// <summary>Warnings sharing a code explain the failed rule and the precise recovery to authors.</summary>
    [TestMethod]
    [DataRow("input")]
    [DataRow("group")]
    public void MetadataMessagesExplainOrderAndDescriptionRules(string kind)
    {
        var cases = new[]
        {
            (Attribute: "order='bad'", Content: "", Rule: "'order'", Requirement: "ASCII digits", Recovery: "this order is ignored", Related: 0),
            (Attribute: "", Content: "<s:description>Before<g/>After</s:description>", Rule: "'svt:description'", Requirement: "without child elements", Recovery: "this description is ignored", Related: 0),
            (Attribute: "", Content: "<s:description>One</s:description><s:description>Two</s:description>", Rule: "'svt:description'", Requirement: "At most one", Recovery: "all descriptions for this declaration are ignored", Related: 1)
        };
        var messages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            var result = Inspect(Xml($"<s:{kind} key='a' label='Central' {item.Attribute}>{item.Content}</s:{kind}>"));
            var diagnostic = result.Diagnostics.Single();
            Assert.AreEqual("svt.metadata.invalid", diagnostic.Code);
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.AreEqual(kind == "input" ? "a" : null, diagnostic.Key);
            Assert.IsNotNull(diagnostic.Location);
            Assert.HasCount(item.Related, diagnostic.RelatedLocations);
            StringAssert.Contains(diagnostic.Message, item.Rule);
            StringAssert.Contains(diagnostic.Message, item.Requirement);
            StringAssert.Contains(diagnostic.Message, item.Recovery);
            if (item.Attribute.Length > 0) StringAssert.Contains(diagnostic.Message, "0 through 2147483647");
            messages.Add(diagnostic.Message);
            Assert.IsNotNull(Template(result).Bind([TemplateBinding.Paint("a", "blue")]).Document);
        }
        Assert.HasCount(3, messages);
    }

    /// <summary>Identity, reference and text failures identify their rule instead of a generic invalid-metadata message.</summary>
    [TestMethod]
    [DataRow("<s:input/>", "'key'", "nonempty", "declaration is ignored")]
    [DataRow("<s:group key=''/>", "'key'", "nonempty", "declaration is ignored")]
    [DataRow("<s:input key='a' group=''/>", "'group'", "nonempty", "remains ungrouped")]
    [DataRow("unexpected text", "'svt:inputs'", "Only whitespace", "character data is ignored")]
    [DataRow("<s:input key='a'>unexpected text</s:input>", "'svt:input'", "Only whitespace", "character data is ignored")]
    [DataRow("<s:group key='g'>unexpected text</s:group>", "'svt:group'", "Only whitespace", "character data is ignored")]
    public void MetadataMessagesExplainOtherInvalidRules(string declarations, string field, string rule, string recovery)
    {
        var result = Inspect(Xml(declarations));
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("svt.metadata.invalid", diagnostic.Code);
        StringAssert.Contains(diagnostic.Message, field);
        StringAssert.Contains(diagnostic.Message, rule);
        StringAssert.Contains(diagnostic.Message, recovery);
        Assert.HasCount(2, Template(result).Inputs);
    }

    /// <summary>Only exact namespaces/placements participate; foreign payloads are opaque.</summary>
    [TestMethod]
    public void NamespaceAndPlacementBoundaries()
    {
        var xml = Start + """
            <s:inputs><s:input key='a' label='Outside'/></s:inputs>
            <g><metadata><s:inputs><s:input key='a' label='Nested'/></s:inputs></metadata></g>
            <metadata>uninterpreted text<f:wrapper xmlns:f='urn:foreign'><s:inputs><s:input/></s:inputs></f:wrapper></metadata>
            <metadata><r:RDF><r:Description r:about=''><s:inputs r:parseType='Literal'>
              <s:input key='a' label='Valid' group='g' s:label='Wrong' xmlns:f='urn:foreign' f:extra='ignored'>
                <s:description xml:space='preserve'>  Help  </s:description>
                <f:payload><s:input/></f:payload>
              </s:input>
              <s:group key='g'><s:group key='nested'/></s:group>
              <s:inputs><s:input/></s:inputs>
              <input key='b' label='Wrong namespace'/>
            </s:inputs></r:Description></r:RDF></metadata>
            """ + Paint + "</svg>";
        var result = Inspect(xml);
        Assert.HasCount(3, result.Diagnostics);
        Assert.IsTrue(result.Diagnostics.All(d => d.Code == "svt.metadata.unknown"));
        Assert.AreEqual("Valid", Template(result).Inputs[0].Label);
        Assert.AreEqual("Help", Template(result).Inputs[0].Description);
        Assert.AreEqual("Inline", Template(result).Inputs[1].Label);
    }

    /// <summary>Exact normalized identities are independent for groups/inputs and never trimmed or folded.</summary>
    [TestMethod]
    public void ExactKeysAndXmlWhitespace()
    {
        var result = Inspect(Xml("<s:group key=' a '/><s:input key=' a ' group=' a ' label='&#160;'><s:description> &#160; </s:description></s:input>" +
            "<s:input key='A' group='a' label='Upper'/><s:input key=' '/>",
            "<g data-svt-fill=' a '/><g data-svt-fill='A'/><g data-svt-fill=' '/>"));
        Assert.AreEqual("svt.metadata.group", result.Diagnostics.Single().Code);
        var template = Template(result);
        Assert.AreEqual(" a ", template.InputGroups[0].Key);
        Assert.AreEqual(" a ", template.InputGroups[0].Label);
        Assert.AreEqual("\u00a0", template.Inputs[0].Label);
        Assert.AreEqual("\u00a0", template.Inputs[0].Description);
        Assert.AreEqual(" ", template.Inputs[2].Key);
    }

    /// <summary>SVG metadata contents do not expose hooks, annotations, or rejected binding keys.</summary>
    [TestMethod]
    public void MetadataSubtreesAreNonbindingButBudgeted()
    {
        var xml = Start + "<g><metadata data-svt-fill='a' data-svt-unknown='x' data-svt-version='99'>" +
            "<svg><text data-svt-text='hidden'/><g data-svt-fill='a'/></svg></metadata></g>" +
            "<f:metadata xmlns:f='urn:foreign'><g data-svt-fill='ordinary'/></f:metadata>" + Paint + "</svg>";
        var result = Inspect(xml, new() { MaxHooks = 6 });
        Assert.IsEmpty(result.Diagnostics);
        Sequence(Template(result).Inputs.Select(i => i.Key).ToArray(), "ordinary", "a", "b");
        Assert.AreEqual("svt.binding.unknown", Template(result).Bind([TemplateBinding.Text("hidden", "x")]).Diagnostics.Single().Code);
        result = Inspect(xml, new() { MaxHooks = 5 });
        Assert.IsNull(result.Template);
        Assert.AreEqual("MaxHooks", result.Diagnostics.Single().Limit);
    }

    /// <summary>Empty groups disappear without hiding validation failures; empty templates have no sections.</summary>
    [TestMethod]
    public void EmptyAndHookOnlyTemplates()
    {
        var empty = Inspect(Xml("<s:group key='unused' order='bad'/>", ""));
        Assert.AreEqual("svt.metadata.invalid", empty.Diagnostics.Single().Code);
        Assert.IsEmpty(Template(empty).InputGroups);
        var template = Template(Inspect(Start + Paint + "</svg>"));
        Assert.IsNull(template.InputGroups.Single().Key);
        Sequence(Keys(template.InputGroups.Single()), "a", "b");
        var partial = Template(Inspect(Xml("<s:input key='b' order='0'/>", Paint)));
        Sequence(Keys(partial.InputGroups.Single()), "b", "a");
    }

    /// <summary>Empty descriptions/labels are absence, while an empty property retains all artwork inputs.</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow(" &#x9;&#xD;&#xA; ")]
    public void EmptyPresentationValues(string text)
    {
        var result = Inspect(Xml($"<s:group key='g' label='{text}'><s:description>{text}</s:description></s:group>" +
            $"<s:input key='a' label='{text}' group='g'><s:description>{text}</s:description></s:input>"));
        Assert.IsEmpty(result.Diagnostics);
        var template = Template(result);
        Assert.AreEqual("Inline", template.Inputs[0].Label);
        Assert.AreEqual(InputLabelSource.Element, template.Inputs[0].LabelSource);
        Assert.IsNull(template.Inputs[0].Description);
        Assert.AreEqual("g", template.InputGroups[0].Label);
        Assert.IsNull(template.InputGroups[0].Description);
        var empty = Inspect(Xml(text));
        Assert.IsEmpty(empty.Diagnostics);
        Sequence(Keys(Template(empty).InputGroups.Single()), "a", "b");
    }

    /// <summary>Metadata counts toward every XML budget and shares the scoped diagnostic budget.</summary>
    [TestMethod]
    public void MetadataBudgetBoundaries()
    {
        var xml = Xml("<s:group key='g'/><s:input key='a' group='g' label='Central'><s:description>Help</s:description></s:input>");
        var limits = new InspectionOptions
        {
            MaxSourceBytes = Encoding.UTF8.GetByteCount(xml),
            MaxXmlCharacters = xml.Length,
            MaxElements = 9,
            MaxElementDepth = 7,
            MaxAttributesPerElement = 5,
            MaxHooks = 2,
            MaxDiagnostics = 1
        };
        Assert.IsEmpty(Inspect(xml, limits).Diagnostics);
        foreach (var property in typeof(InspectionOptions).GetProperties().Where(p => p.Name != "MaxDiagnostics"))
        {
            var value = property.GetValue(limits) as int? ?? throw new AssertFailedException("Missing limit.");
            var reduced = new InspectionOptions();
            property.SetValue(reduced, value - 1);
            var result = Inspect(xml, reduced);
            Assert.IsNull(result.Template, property.Name);
            Assert.AreEqual(property.Name, result.Diagnostics.Single().Limit);
        }
        var warnings = Xml("<s:input key='a' order='bad' group='unknown'/>");
        Assert.HasCount(2, Inspect(warnings, new() { MaxDiagnostics = 2 }).Diagnostics);
        var terminal = Inspect(warnings, new() { MaxDiagnostics = 1 });
        Assert.IsNull(terminal.Template);
        Assert.AreEqual("MaxDiagnostics", terminal.Diagnostics.Single().Limit);
    }

    /// <summary>Binding retains metadata and authored raw defaults while replacing only admitted artwork.</summary>
    [TestMethod]
    public void BindingPreservesMetadataAndRawEvidence()
    {
        var xml = Xml("<s:input key='a' label='Central'><s:description>Use &lt;b&gt;literally&lt;/b&gt;.</s:description></s:input>" +
            "<s:input key='missing'/>", Paint + "<g data-svt-fill='a' fill='url(odd)'/>" +
            "<metadata><text data-svt-text='hidden'>Keep</text></metadata>");
        var template = Template(Inspect(xml));
        Assert.AreEqual(InputDefaultState.Mixed, template.Inputs[0].Default.State);
        foreach (var assignments in new[] { Array.Empty<TemplateBinding>(), new[] { TemplateBinding.Paint("a", "blue") } })
        {
            var result = template.Bind(assignments);
            Assert.IsNotNull(result.Document);
            Assert.IsEmpty(result.Diagnostics);
            var before = XElement.Parse(xml).Elements(InputMetadataName()).ToArray();
            var after = XElement.Parse(result.Document.Xml).Elements(InputMetadataName()).ToArray();
            Assert.AreEqual(before.Length, after.Length);
            for (var i = 0; i < before.Length; i++) Assert.IsTrue(XNode.DeepEquals(before[i], after[i]));
            var reinspected = Template(Inspect(result.Document.Xml));
            Assert.AreEqual("Central", reinspected.Inputs[0].Label);
            Assert.AreEqual("Use <b>literally</b>.", reinspected.Inputs[0].Description);
        }
        Assert.AreEqual("red", template.Inputs[0].Targets[0].Default.Raw);
    }

    private static void Sequence<T>(IEnumerable<T> actual, params T[] expected) => CollectionAssert.AreEqual(expected, actual.ToArray());

    private static XName InputMetadataName() => XName.Get("metadata", "http://www.w3.org/2000/svg");
}
