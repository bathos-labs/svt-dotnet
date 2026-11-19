using System.Collections.Immutable;
using System.Globalization;
using Bathos.Graphics.SvgTemplates;

namespace SvgTemplateExamples;

// Host-owned example, compiled by the tests and isolated package consumer, not the library.
internal readonly record struct OpaqueRgb(byte Red, byte Green, byte Blue)
{
    internal static OpaqueRgb ForChooser(SrgbColor color) => new(ToByte(color.Red), ToByte(color.Green), ToByte(color.Blue));
    internal string ToPaint() => string.Create(CultureInfo.InvariantCulture, $"#{Red:x2}{Green:x2}{Blue:x2}");
    private static byte ToByte(double component) => (byte)Math.Round(component * 255, MidpointRounding.AwayFromZero);
}

internal sealed class PaintEditorAdapter
{
    private OpaqueRgb? _replacement;
    private ChooserSession? _active;

    internal PaintEditorAdapter(TemplateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind != InputKind.Paint) throw new ArgumentException("A paint input is required.", nameof(input));
        AuthoredEvidence = input;
        Presentation = input.Targets.Select(target => DefaultInterpreter.InterpretPaint(target)).ToImmutableArray();
        // Deliberately conservative shared-input policy. Equal swatches do not collapse raw Mixed.
        if (input.Default.State == InputDefaultState.Common &&
            Presentation.All(value => value.Color is { Alpha: 1 }) && Presentation[0].Color is { } color)
            InitialChooserValue = OpaqueRgb.ForChooser(color);
    }

    internal TemplateInput AuthoredEvidence { get; }
    internal ImmutableArray<PaintInterpretation> Presentation { get; }
    // A lossy host display value only. This is never serialized without a genuine accepted edit.
    internal OpaqueRgb? InitialChooserValue { get; }

    internal ChooserSession OpenChooser()
    {
        if (_active is not null) throw new InvalidOperationException("A chooser is already open.");
        _active = new(this, _replacement ?? InitialChooserValue);
        return _active;
    }

    internal ImmutableArray<TemplateBinding> GetBindings() => _replacement is { } value
        ? [TemplateBinding.Paint(AuthoredEvidence.Key, value.ToPaint())] : [];

    internal void Reset()
    {
        _active?.Cancel();
        _replacement = null;
    }

    internal sealed class ChooserSession
    {
        private readonly PaintEditorAdapter _owner;
        private OpaqueRgb? _candidate;
        private bool _userSelected;
        private bool _closed;

        internal ChooserSession(PaintEditorAdapter owner, OpaqueRgb? initial)
        {
            _owner = owner;
            InitialChooserValue = initial;
            _candidate = initial;
            ObservedWidgetValue = initial;
        }

        internal OpaqueRgb? InitialChooserValue { get; }
        internal bool CanChoose => InitialChooserValue.HasValue;
        internal OpaqueRgb? ObservedWidgetValue { get; private set; }

        // Generic change notifications may be initialization, rounding, or delayed widget events.
        // They update display observations only, never candidate authoring or preview bindings.
        internal void ObserveWidgetValue(OpaqueRgb value)
        {
            EnsureOpen();
            ObservedWidgetValue = value;
        }

        // The host calls this only from an explicit user-action path, not ValueChanged alone.
        internal void SelectFromUser(OpaqueRgb value)
        {
            EnsureOpen();
            if (!CanChoose) throw new InvalidOperationException("This input requires a richer editor or explicit replacement workflow.");
            _candidate = value;
            ObservedWidgetValue = value;
            _userSelected = true;
        }

        internal void Accept()
        {
            EnsureOpen();
            if (_userSelected && _candidate != InitialChooserValue && _candidate is { } value)
                _owner._replacement = value;
            Close();
        }

        internal void Cancel()
        {
            EnsureOpen();
            Close();
        }

        private void Close()
        {
            _closed = true;
            _owner._active = null;
        }

        private void EnsureOpen()
        {
            if (_closed) throw new InvalidOperationException("This chooser interaction has ended.");
        }
    }
}
