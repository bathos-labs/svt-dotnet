using System.Text;
using System.Xml.Linq;
using SvgTemplateExamples;

namespace ScalableVectorTemplates.Tests;

/// <summary>Host interaction must not turn interpreted presentation into authoring intent.</summary>
[TestClass]
public sealed class EditorAdapterTests
{
    /// <summary>Quantization and widget initialization notifications preserve omission and original spelling.</summary>
    [TestMethod]
    [DataRow("red", 255, 0, 0)]
    [DataRow("#abc", 170, 187, 204)]
    [DataRow("rgb(.1 50% 0)", 0, 128, 0)]
    public void InitializationAndNoEditAcceptance(string raw, int red, int green, int blue)
    {
        var template = Inspect(raw);
        var input = template.Inputs[0];
        var adapter = new PaintEditorAdapter(input);
        Assert.AreSame(input, adapter.AuthoredEvidence);
        Assert.AreSame(input.Targets[0], adapter.Presentation[0].Target);
        var seed = new OpaqueRgb((byte)red, (byte)green, (byte)blue);
        Assert.AreEqual(seed, adapter.InitialChooserValue);
        var session = adapter.OpenChooser();
        // Simulate both normal initialization and a widget's own altered/rounded display event.
        session.ObserveWidgetValue(seed);
        session.ObserveWidgetValue(new(12, 34, 56));
        Assert.IsEmpty(adapter.GetBindings(), "Opening or initialization emitted a preview replacement.");
        session.Accept();
        Omitted(template, adapter);
        Assert.AreEqual(raw, adapter.AuthoredEvidence.Targets[0].Default.Raw);
        if (raw.StartsWith("rgb", StringComparison.Ordinal))
        {
            var color = adapter.Presentation[0].Color;
            Assert.IsNotNull(color);
            Assert.AreEqual(.1 / 255, color.Red, 1e-12);
            Assert.AreEqual(.5, color.Green, 1e-12);
        }
    }

    /// <summary>Cancel and returning to the initial chooser value preserve default intent.</summary>
    [TestMethod]
    [DataRow("red")]
    [DataRow("#abc")]
    [DataRow("rgb(.1 50% 0)")]
    public void CancelAndReturnToInitial(string raw)
    {
        var template = Inspect(raw);
        var adapter = new PaintEditorAdapter(template.Inputs[0]);
        var initial = adapter.InitialChooserValue ?? throw new AssertFailedException("Missing chooser seed.");
        var cancelled = adapter.OpenChooser();
        cancelled.SelectFromUser(new(1, 2, 3));
        Assert.IsEmpty(adapter.GetBindings(), "An unaccepted edit created a preview replacement.");
        cancelled.Cancel();
        Omitted(template, adapter);
        var reverted = adapter.OpenChooser();
        reverted.SelectFromUser(new(1, 2, 3));
        reverted.SelectFromUser(initial);
        reverted.Accept();
        Omitted(template, adapter);
        var unchanged = adapter.OpenChooser();
        unchanged.SelectFromUser(initial);
        unchanged.Accept();
        Omitted(template, adapter);
    }

    /// <summary>Only accepted changes create one replacement; reset removes it, preserving raw evidence.</summary>
    [TestMethod]
    public void AcceptedChangeReopenAndReset()
    {
        var template = Inspect("rgb(.1 50% 0)");
        var adapter = new PaintEditorAdapter(template.Inputs[0]);
        var choice = new OpaqueRgb(1, 2, 3);
        var session = adapter.OpenChooser();
        session.SelectFromUser(choice);
        session.ObserveWidgetValue(new(99, 99, 99)); // A delayed non-user notification is not a new edit.
        session.Accept();
        Assert.HasCount(1, adapter.GetBindings());
        Assert.AreEqual("paint", adapter.GetBindings()[0].Key);
        var bound = template.Bind(adapter.GetBindings());
        Assert.IsNotNull(bound.Document);
        Assert.AreEqual("#010203", XDocument.Parse(bound.Document.Xml).Root?.Elements().Single().Attribute("fill")?.Value);
        Assert.AreEqual("rgb(.1 50% 0)", adapter.AuthoredEvidence.Targets[0].Default.Raw);
        var reopened = adapter.OpenChooser();
        Assert.AreEqual(choice, reopened.InitialChooserValue);
        reopened.SelectFromUser(new(4, 5, 6));
        reopened.SelectFromUser(choice);
        reopened.Accept();
        Assert.HasCount(1, adapter.GetBindings());
        Assert.AreEqual(bound.Document.Xml, template.Bind(adapter.GetBindings()).Document?.Xml);
        var cancelled = adapter.OpenChooser();
        cancelled.SelectFromUser(new(7, 8, 9));
        cancelled.Cancel();
        Assert.AreEqual(bound.Document.Xml, template.Bind(adapter.GetBindings()).Document?.Xml);
        var pending = adapter.OpenChooser();
        pending.SelectFromUser(new(7, 8, 9));
        adapter.Reset();
        Assert.ThrowsExactly<InvalidOperationException>(() => pending.Accept());
        Omitted(template, adapter);
        Assert.AreEqual(new OpaqueRgb(0, 128, 0), adapter.OpenChooser().InitialChooserValue);
    }

    /// <summary>Opaque controls do not invent defaults or discard alpha; presentation remains truthful.</summary>
    [TestMethod]
    [DataRow("#abcd", PaintInterpretationKind.Literal)]
    [DataRow("rgba(255,0,0,.5)", PaintInterpretationKind.Literal)]
    [DataRow("transparent", PaintInterpretationKind.Literal)]
    [DataRow("none", PaintInterpretationKind.NoPaint)]
    [DataRow("currentColor", PaintInterpretationKind.RequiresContext)]
    [DataRow("var(--accent)", PaintInterpretationKind.RequiresContext)]
    [DataRow("url(#paint) red", PaintInterpretationKind.ResourceReference)]
    [DataRow("", PaintInterpretationKind.UnsupportedOrUnrecognized)]
    [DataRow(null, PaintInterpretationKind.Absent)]
    public void ConstrainedChooserPreservesUnsupportedStates(string? raw, PaintInterpretationKind kind)
    {
        var template = Inspect(raw);
        var adapter = new PaintEditorAdapter(template.Inputs[0]);
        Assert.AreEqual(kind, adapter.Presentation[0].Kind);
        Assert.IsNull(adapter.InitialChooserValue);
        var session = adapter.OpenChooser();
        Assert.IsFalse(session.CanChoose);
        session.ObserveWidgetValue(new(0, 0, 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.SelectFromUser(new(1, 2, 3)));
        session.Accept();
        Omitted(template, adapter);
        Assert.AreEqual(raw is null ? InputDefaultState.Absent : InputDefaultState.Common, adapter.AuthoredEvidence.Default.State);
        if (kind == PaintInterpretationKind.Literal)
        {
            var color = adapter.Presentation[0].Color;
            Assert.IsNotNull(color);
            Assert.IsLessThan(1d, color.Alpha);
        }
        else Assert.IsNull(adapter.Presentation[0].Color);
    }

    /// <summary>Equivalent per-target swatches cannot collapse raw Mixed or silently replace all targets.</summary>
    [TestMethod]
    public void SharedInputPolicyAndLifecycle()
    {
        var template = Inspect("red", "#ff0000");
        var adapter = new PaintEditorAdapter(template.Inputs[0]);
        Assert.AreEqual(InputDefaultState.Mixed, adapter.AuthoredEvidence.Default.State);
        Assert.HasCount(2, adapter.Presentation);
        foreach (var item in adapter.Presentation)
        {
            Assert.IsNotNull(item.Color);
            Assert.AreEqual(1d, item.Color.Red);
            Assert.AreEqual(0d, item.Color.Green);
            Assert.AreEqual(0d, item.Color.Blue);
        }
        Assert.AreSame(template.Inputs[0].Targets[1], adapter.Presentation[1].Target);
        Assert.IsNull(adapter.InitialChooserValue);
        var session = adapter.OpenChooser();
        Assert.ThrowsExactly<InvalidOperationException>(() => adapter.OpenChooser());
        session.Accept();
        Assert.ThrowsExactly<InvalidOperationException>(() => session.ObserveWidgetValue(new(1, 2, 3)));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.SelectFromUser(new(1, 2, 3)));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Cancel());
        Omitted(template, adapter);
        Assert.AreEqual(InputDefaultState.Mixed, adapter.AuthoredEvidence.Default.State);
        var common = new PaintEditorAdapter(Inspect("red", "red").Inputs[0]);
        var chooser = common.OpenChooser();
        chooser.SelectFromUser(new(1, 2, 3));
        chooser.Accept();
        Assert.HasCount(1, common.GetBindings());
    }

    private static InspectedTemplate Inspect(params string?[] raw)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        var elements = raw.Select(value => new XElement(svg + "g", new XAttribute("data-svt-fill", "paint"),
            value is null ? null : new XAttribute("fill", value)));
        var xml = new XElement(svg + "svg", new XAttribute("data-svt-version", "0"), elements);
        var inspection = TemplateInspector.Inspect(Encoding.UTF8.GetBytes(xml.ToString()));
        Assert.IsEmpty(inspection.Diagnostics);
        Assert.IsNotNull(inspection.Template);
        return inspection.Template;
    }

    private static void Omitted(InspectedTemplate template, PaintEditorAdapter adapter)
    {
        Assert.IsEmpty(adapter.GetBindings());
        var original = template.Bind([]).Document;
        var current = template.Bind(adapter.GetBindings()).Document;
        Assert.IsNotNull(original);
        Assert.IsNotNull(current);
        Assert.IsTrue(XNode.DeepEquals(XDocument.Parse(original.Xml), XDocument.Parse(current.Xml)));
    }
}
