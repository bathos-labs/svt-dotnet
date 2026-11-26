using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ScalableVectorTemplates.Testing;

/// <summary>Maps API and CLI results into the portable conformance observations.</summary>
internal static class PortableObservations
{
    internal static JsonNode Inspection(JsonNode report)
    {
        var inputs = report["inputs"]?.AsArray();
        Assert.IsNotNull(inputs);
        foreach (var input in inputs)
        {
            var targets = input?["targets"]?.AsArray();
            Assert.IsNotNull(targets);
            foreach (var target in targets)
            {
                Assert.IsNotNull(target);
                target.AsObject().Remove("location");
                var evidence = target["default"];
                Assert.IsNotNull(evidence);
                if ((string?)evidence["source"] == "x_link_href") evidence["source"] = "xlink_href";
            }
        }
        return report;
    }

    internal static XDocument Xml(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        foreach (var element in document.Descendants())
        {
            // Expanded names carry namespace identity independently of prefix spelling.
            var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
                .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal).ToArray();
            element.ReplaceAttributes(attributes);
        }
        foreach (var cdata in document.DescendantNodes().OfType<XCData>().ToArray())
            cdata.ReplaceWith(new XText(cdata.Value));
        return document;
    }
}
