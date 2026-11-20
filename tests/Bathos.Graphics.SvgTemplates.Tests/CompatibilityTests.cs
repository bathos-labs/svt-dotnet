namespace Bathos.Graphics.SvgTemplates.Tests;

/// <summary>Published operational defaults that assembly API comparison cannot observe.</summary>
[TestClass]
public sealed class CompatibilityTests
{
    /// <summary>Format-0 inspection retains its published default budgets.</summary>
    [TestMethod]
    public void InspectionDefaults()
    {
        var options = new InspectionOptions();
        Assert.AreEqual(16_777_216, options.MaxSourceBytes);
        Assert.AreEqual(16_777_216, options.MaxXmlCharacters);
        Assert.AreEqual(64, options.MaxElementDepth);
        Assert.AreEqual(100_000, options.MaxElements);
        Assert.AreEqual(256, options.MaxAttributesPerElement);
        Assert.AreEqual(10_000, options.MaxHooks);
        Assert.AreEqual(1_000, options.MaxDiagnostics);
    }

    /// <summary>Default and newly constructed configurations retain the published failure policy and budgets.</summary>
    [TestMethod]
    public void BindingDefaults()
    {
        foreach (var configuration in new[] { BindingConfiguration.Default, new BindingConfiguration() })
        {
            var options = configuration.BindingOptions;
            Assert.AreEqual(UnknownKeyBehavior.Error, options.UnknownKeyBehavior);
            Assert.AreEqual(10_000, options.MaxBindings);
            Assert.AreEqual(1_048_576, options.MaxValueCharacters);
            Assert.AreEqual(8_388_608, options.MaxTotalValueCharacters);
            Assert.AreEqual(33_554_432, options.MaxTotalEncodedImageBytes);
            Assert.AreEqual(67_108_864, options.MaxOutputBytes);
            Assert.AreEqual(1_000, options.MaxDiagnostics);
            Assert.AreEqual(16_777_216, configuration.ImageOptions.MaxEncodedImageBytes);
            Assert.AreEqual(256, configuration.ImageOptions.MaxMediaTypeCharacters);
        }
    }

    /// <summary>Optional interpretation retains its published work limits.</summary>
    [TestMethod]
    public void InterpretationDefaults()
    {
        var options = new InterpretationOptions();
        Assert.AreEqual(4096, options.MaxPaintCharacters);
        Assert.AreEqual(4096, options.MaxReferenceCharacters);
        Assert.AreEqual(1024, options.MaxDataUriHeaderCharacters);
        Assert.AreEqual(64, options.MaxNumericTokenCharacters);
        Assert.AreEqual(16, options.MaxNestingDepth);
    }
}
