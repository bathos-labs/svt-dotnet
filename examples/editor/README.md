# Host-owned paint editor example

[PaintEditorAdapter.cs](PaintEditorAdapter.cs) is a framework-independent example for an opaque,
8-bit RGB chooser. Its internal types belong to the host, not the library API. The same source is
compiled into
[behavioral tests](../../tests/Bathos.Graphics.SvgTemplates.Tests/EditorAdapterTests.cs) and the
[isolated package consumer](../../eng/package/README.md#isolated-consumption). Repository-authored
example code is covered by the [MIT license](../../LICENSE).

## Keep three states separate

- `AuthoredEvidence` retains the inspected input and every target's original raw value, location and
  origin. Its Common/Mixed/Absent state remains authoritative for raw defaults.
- `Presentation` contains independent per-target interpretation results, in target order. These
  explain what can be displayed; they do not record edits or establish rendered appearance.
- `InitialChooserValue` is a host-owned, potentially lossy byte RGB seed. It is available only for a
  raw Common input whose targets all interpret as opaque literals. It never becomes a binding merely
  because the chooser opens or reports a value.

For example, `red`, `#abc` and `rgb(.1 50% 0)` seed `(255,0,0)`, `(170,187,204)` and `(0,128,0)`.
The last seed quantizes fractional components for this widget; the interpretation retains the
original double precision. Formatting a seed as hex does not establish a user edit, even when the
formatted text differs from the authored string.

This deliberately conservative shared-input policy leaves `red` plus `#ff0000` as raw Mixed, even
though their independent swatches match. No shared chooser seed is inferred. A host can offer an
explicit replace-all workflow separately; the core supplies no interpreted aggregate.

## Present unsupported chooser states honestly

| Evidence                                       | Presentation                                           | Opaque chooser                                 |
| ---------------------------------------------- | ------------------------------------------------------ | ---------------------------------------------- |
| Common opaque literal                          | Per-target sRGB swatch and raw spelling                | Enabled                                        |
| Alpha-bearing literal, including `transparent` | Color and alpha; use a suitable transparency indicator | Disabled; never discard alpha                  |
| `none`                                         | No paint                                               | Disabled; not transparent black                |
| `currentColor` or `var(--accent)`              | Requires context                                       | Disabled; no invented color                    |
| `url(#paint) red`                              | Resource reference                                     | Disabled; fallback is not an effective default |
| Absent                                         | Inherited/unspecified authored default                 | Disabled; no invented color                    |
| Empty or unsupported syntax                    | Raw value and interpretation outcome                   | Disabled                                       |
| Raw Mixed                                      | Separate target evidence and presentation              | Disabled, including equal swatches             |

Disabled here means read-only in this constrained chooser, not invalid SVG or a prohibition on a
richer host editor. The example does not fetch resources, compute CSS, or implement a raw
replacement editor.

## Connect explicit user intent

```csharp
var editor = new PaintEditorAdapter(paintInput);
var interaction = editor.OpenChooser();
if (interaction.InitialChooserValue is { } initial)
{
    // Set the widget value. Ordinary ValueChanged notifications go here:
    interaction.ObserveWidgetValue(initial);
}
// This path is invoked only for a genuine user action, with CanChoose true:
// interaction.SelectFromUser(new OpaqueRgb(12, 34, 56));
interaction.Accept(); // Or Cancel(), when dismissed.
var output = template.Bind(editor.GetBindings()); // No edit means no assignment.
// A separate explicit Reset action removes an accepted replacement:
editor.Reset();
```

The host must establish event origin. `ObserveWidgetValue` accepts initialization, rounding and late
generic notifications without changing authoring intent. `SelectFromUser` must be connected to a
trusted user gesture/action, never to generic `ValueChanged` alone. If the framework cannot identify
such actions reliably, provide an explicit user selection/apply flow. Comparing formatted hex with
the raw source, or ignoring just the first notification, is insufficient.

No draft value is published as a preview replacement by this example. `GetBindings()` contains only
accepted edits. Merge these assignments with other inputs in a real host's binding collection.

| Interaction                                                        | Binding outcome                                           |
| ------------------------------------------------------------------ | --------------------------------------------------------- |
| Open, initialize widget, accept without a user edit                | Existing binding state unchanged; omission stays omitted  |
| Select, then cancel                                                | Existing binding state unchanged                          |
| Select, then return to the session's initial chooser value, accept | Existing binding state unchanged                          |
| Select a different value and accept                                | Exactly one explicit paint replacement for this input     |
| Reopen an accepted replacement                                     | Seed from that replacement; unchanged/cancel preserves it |
| Reset                                                              | Remove the replacement and cancel any active interaction  |

A session can finish only once, and only one session may be active per adapter. Calls on an ended
session fail deliberately. Reset is the explicit return-to-default action: a later accepted edit
back to the authored color's quantized seed still represents an explicit replacement, not omission.
The example assumes UI-thread ownership and introduces no thread-safe editing service.

## Executable evidence

The tests cover initialization notifications, quantization, no edit, cancel, change-and-return,
accepted edits, reopened overrides, Reset, ended sessions, alpha/context/resource/absent states and
shared raw Mixed evidence. They assert bindings and retained XML/evidence rather than documentation
wording or framework event timing. Run the full portable gate with `task check`;
`task package:check` then compiles and exercises the same adapter against an isolated
PackageReference. Actual widget integration and native preview behavior remain host validation
responsibilities.
