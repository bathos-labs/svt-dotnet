# SVT command-line contract

This document defines the implemented CLI. The lifecycle is inspect, prepare values, bind, then
explicitly publish. The
[inspection](https://github.com/svt-project/svt-spec/blob/master/spec/inspection.md) and
[substitution](https://github.com/svt-project/svt-spec/blob/master/spec/substitution.md) contracts
own template and replacement semantics. The CLI owns bounded file acquisition, request parsing,
diagnostics presentation and output publication. It does not render, fetch references, interpret
paint, decode images or certify SVG validity.

## Tool distribution

The [packaging and release contract](tool-distribution.md) defines the separate
`ScalableVectorTemplates.Cli` tool package, version-pinned installation, prepared execution for
automation and recovery of a partially published release. The same command contract applies to
source-built and installed execution. SDK setup/restore failures occur outside the SVT diagnostic
protocol.

## Commands and invocation

```sh
svt inspect template.svg
svt inspect template.svg --format json
svt bind template.svg --bindings bindings.json --output result.svg
svt bind template.svg --bindings bindings.json --output -
svt --help
svt -h
svt inspect --help
svt bind --help
svt --version
```

Exactly one template operand is required for either operation. Bind requires both `--bindings` and
`--output`; an empty or omitted bindings array explicitly requests preservation and serialization.
There are no separate validate, substitute or render commands, inline assignment syntax or in-place
mode. Help and version require no input access and exit successfully. Invoking `svt` without a
command prints usage and exits with an invocation error; it does not implicitly inspect standard
input.

| Option                                      | Applies to | Meaning and default                                           |
| ------------------------------------------- | ---------- | ------------------------------------------------------------- |
| `--format text\|json`                       | inspect    | Discovery report format; default text                         |
| `--bindings PATH`                           | bind       | Required binding JSON input                                   |
| `--output PATH`                             | bind       | Required SVG destination                                      |
| `--overwrite`                               | bind       | Permit replacement of an existing destination; default refuse |
| `--configuration PATH`                      | both       | Explicit configuration file; otherwise defaults               |
| `--diagnostics text\|json`                  | both       | Stderr presentation; default text                             |
| `--unknown-keys error\|ignore-with-warning` | bind       | Override binding unknown-key policy                           |
| `--inspection-errors fail\|allow-scoped`    | bind       | Override publication policy for scoped inspection errors      |

Options and values are case-sensitive. Options may precede or follow the template operand; `--` ends
option recognition. Unknown options, repeated singleton options, extra operands and missing option
values are invocation errors. `-h` is the only short alias, equivalent to `--help` at the top level
and for either command. Mixed/repeated spellings count as a repeated singleton option. After `--`,
`-h` is an operand. There are no abbreviated options, interactive prompts, implicit configuration
searches or environment-variable overrides. Terminal detection does not change formats.
Human-readable rendering follows the escaping rules below.

`-` denotes standard input for the template or binding file, or standard output for the SVG. At most
one input may consume standard input. Configuration must name a file. A literal file named `-` can
be addressed as `./-`. Overwrite is invalid with standard output. Paths supplied on the command line
resolve against the working directory; filenames beginning with `-` can be supplied after `--` as
the template operand or through an unambiguous relative path as an option value.

### Help and version presentation

Use brief, natural language. State the task or problem first, then the next useful action. Keep
technical details below the main explanation. Top-level help is a short introduction, two runnable
command shapes, command descriptions and a command-help hint:

```text
Inspect and update SVG templates.

svt inspect template.svg
svt bind template.svg --bindings bindings.json --output result.svg

inspect   Show the template's editable inputs.
bind      Apply values and save an SVG.

Use svt <command> --help for options and examples.
```

Command help lists only that command's options, required operands, defaults and two practical
examples. Explain stdin/stdout, explicit configuration and overwrite where relevant. Bind help shows
a minimum useful binding request and describes `{}` as “Keep the template's existing values.” Avoid
implementation terminology in introductory prose; link detailed guidance where needed.

- Valid explicit help writes readable multiline text to stdout and exits 0 without reading inputs.
  It waives required template/binding/output operands, but does not forgive invalid supplied
  options, values, duplicates or incompatible combinations. Help is not recognized after `--` or as
  a missing option's value. Top-level help and version remain standalone invocations.
- No arguments writes the invocation diagnostic and readable top-level usage to stderr and exits 2,
  with stdout empty. Other invocation errors give a short explanation and the relevant help hint,
  rather than a full manual. Name the offending option/value and list accepted choices where known.
- If JSON diagnostics are selected for an invocation error, emit only the usual diagnostic envelope
  on stderr, without separate usage text or help on stdout. A missing command with
  `--diagnostics json` follows this rule. Valid command help still goes to stdout; explicitly
  requested JSON diagnostics use the normal successful envelope on stderr.

`--version` writes one line to stdout and exits 0, without inspecting files or Git:

```text
svt 1.0.0-rc.2 (commit cfdb2b429a03)
```

The example version is illustrative, not a new release target embedded in code. Report the exact
semantic version supplied when building the executable, including prerelease and explicit build
metadata. Use embedded package-version and source-revision evidence separately; do not blindly split
on `+` and discard a genuine version suffix or mistake build metadata for a commit. The assembly
numeric version is not the product version. When a full hexadecimal Git revision is embedded, append
its first 12 lowercase hexadecimal digits as shown; otherwise omit the whole commit clause. A
revision alone does not establish a clean checkout, a published artifact or release status. Do not
infer or claim those states, query Git at runtime, or print local build paths or timestamps.

Builds must embed a nonempty product version. Missing version metadata is a build defect, covered by
validation; if encountered at runtime, report an internal failure with exit 4 instead of inventing a
version. Tool packaging must later verify this same output against the actual package identity.

## Binding JSON

```json
{
  "format_version": 0,
  "bindings": [
    { "key": "heading", "kind": "text", "value": "Welcome" },
    { "key": "accent", "kind": "paint", "value": "#2457a7" },
    { "key": "background", "kind": "image_reference", "value": "assets/background.svg" },
    {
      "key": "portrait",
      "kind": "image_content",
      "file": "assets/portrait.png",
      "media_type": "image/png"
    }
  ]
}
```

The root is an object allowing only optional `format_version` and `bindings` properties. An omitted
`format_version` selects format 0 permanently, not the latest format. When supplied, it must be the
JSON number spelled exactly `0`: other versions, `-0`, decimal/exponent forms, strings, booleans,
objects, arrays and null are errors. Any future format requires an explicit marker; omission does
not introduce format detection or migration.

An omitted `bindings` property means an empty array. A supplied value must be an ordered array, not
an object keyed by input name; null and other types are errors. Each entry still requires string
`key` and `kind`, plus the fields below. Unknown or duplicate properties remain errors, including a
misspelled `bindings` property in an otherwise empty request.

A minimum useful request replaces one input:

```json
{
  "bindings": [{ "key": "heading", "kind": "text", "value": "Welcome" }]
}
```

`{}` is a valid no-assignment request, equivalent to `{"format_version":0,"bindings":[]}`. The
caller must still supply `--bindings` and `--output`. Both file and stdin requests follow normal
inspection, policy/budget checks, binding, serialization and output publication protections; this is
not a byte-for-byte copy operation. Omitting assignments preserves authored values, whereas an
assignment with an empty string remains an explicit replacement. These input defaults do not affect
the SVG root marker or the explicit version markers in discovery and diagnostic JSON.

| Kind              | Additional required fields                                          | Meaning                                        |
| ----------------- | ------------------------------------------------------------------- | ---------------------------------------------- |
| `text`            | String `value`                                                      | Literal replacement character data             |
| `paint`           | String `value`                                                      | Raw direct paint attribute value               |
| `image_reference` | String `value`                                                      | Raw reference, without acquisition or rebasing |
| `image_content`   | String `media_type`, exactly one of string `file` or string `value` | Local encoded bytes or base64 encoded bytes    |

For image content, `value` uses the standard base64 alphabet with padding where required, without
whitespace or a data-URL prefix. Empty base64 represents zero bytes. The media type is supplied
explicitly and validated by [encoded-image construction](binding-api.md#encoded-image-preparation).
Do not infer it from a filename, inspect signatures or decode pixels.

JSON inputs use UTF-8 with an optional BOM. Reject malformed UTF-8, unknown or duplicate object
properties, unknown kinds, missing required fields, invalid field combinations, null values,
comments and trailing commas. Property names and kind values are case-sensitive. Preserve decoded
keys and string values without trimming or normalization. Reject unpaired high or low surrogate
escapes in every JSON property name and string value; never preserve them as arbitrary UTF-16 code
units or silently replace them. Valid surrogate pairs are accepted without normalization. This
transport rule applies to configuration and all assignments, including duplicate or ignored unknown
keys. It is checked during JSON validation, before inspection, image preparation or binding lookup.
Report `svt.cli.json` with exit 1 for binding JSON, or `svt.cli.configuration` with exit 2 for
configuration. The library's string API is unchanged.

XML-forbidden scalar values, such as an escaped U+0000 in a replacement, remain binding
responsibilities rather than JSON syntax errors. They still follow binding admission and precedence:
an admitted assignment is rejected for an invalid XML value, while an ignored unknown assignment
skips value validation after budget and duplicate checks.

Duplicate assignment keys are distinct from duplicate JSON properties: retain the assignments and
let binding report duplicate-key groups. Empty keys likewise reach binding validation. Omitted
assignments preserve authored content; empty replacement strings are explicit values. Do not infer
kinds from inspection or introduce a default/unassigned sentinel.

Image file paths are nonempty and resolve relative to the binding file's containing directory. For
binding JSON read from stdin, they resolve against the working directory. The `file` field always
names a local file, never stdin, a URL, a glob or a shell expression. Absolute paths are permitted.
The CLI supplies resource authority for these explicit file reads; it is not a filesystem sandbox.

Raw image references, including data URLs and relative references, are neither opened nor rebased.
Moving output can change their rendering context; publication preserves their strings.

## Configuration

```json
{
  "format_version": 0,
  "inspection": {},
  "binding": { "unknown_key_behavior": "ignore_with_warning" },
  "image": {},
  "cli": { "inspection_errors": "fail" }
}
```

The root permits an optional `format_version` with the same format-0 omission default and exact
explicit-marker validation as binding files. The four groups are optional objects. `{}` uses all
defaults; omitted group properties also use defaults, while nulls and unknown properties are errors.
Use the same strict JSON rules as binding files. Precedence is defaults, then the explicit
configuration, then command-line overrides. Validate the configuration before applying overrides; an
override does not repair an invalid configuration. There are no paths or output destinations in
configuration.

The first three groups map to [InspectionOptions](inspection-api.md), BindingOptions and
EncodedImageOptions in the [.NET binding API](binding-api.md). JSON property names use snake_case;
library diagnostic Limit values retain the exact public option names. Supported fields:

- `inspection`: `max_source_bytes`, `max_xml_characters`, `max_element_depth`, `max_elements`,
  `max_attributes_per_element`, `max_hooks`, `max_diagnostics`.
- `binding`: `unknown_key_behavior`, `max_bindings`, `max_value_characters`,
  `max_total_value_characters`, `max_total_encoded_image_bytes`, `max_output_bytes`,
  `max_diagnostics`.
- `image`: `max_encoded_image_bytes`, `max_media_type_characters`.
- `cli`: `inspection_errors`, `max_bindings_file_bytes`.

Unknown-key policy values in JSON are `error` and `ignore_with_warning`; the default is `error`.
Inspection policy values are `fail` and `allow_scoped`; the default is `fail`. Command-line values
use the hyphenated spellings in the option table. Inspect accepts a reusable configuration but only
uses its inspection settings; it still validates the entire file.

All library budgets retain their documented defaults, units and counting rules. Configurable limits
are positive integers no greater than 2,147,483,647; equality with a limit is allowed and there is
no unlimited sentinel. The binding-file transport default is 67,108,864 bytes (64 MiB).
Configuration has a fixed 65,536-byte (64 KiB) input limit; it cannot override its own loading
limit. Both JSON inputs have a fixed maximum container depth of 16, counting the root object as
depth 1. Byte limits include any BOM and apply while reading, rather than after an unbounded
allocation.

Transport limits are additional to decoded-value budgets. A request within library limits can exceed
the JSON transport limit because of escaping or base64. File and stdin reads must be bounded; file
length metadata alone is not enforcement. Enforce MaxBindings before image acquisition or typed
assignment capture. Image acquisition must respect both per-image and aggregate submitted-image
budgets before retaining an oversized collection; repeated image files count once per assignment.
Binding still performs its own authoritative checks. These limits do not promise a process-wide
memory ceiling.

## Lifecycle and failure policy

1. Validate invocation, load and validate configuration, and capture bounded inputs. Validate the
   binding-file structure before preparing images. No image preparation or output write occurs for a
   malformed request envelope.
2. Inspect the captured template and retain its diagnostics. A null template always stops binding.
   Under the default `fail` policy, any inspection error also stops binding.
3. Under `allow_scoped`, a non-null template may proceed despite scoped inspection errors. Do not
   change diagnostic severities, expose rejected hooks as usable inputs or relax binding admission.
4. Prepare encoded images in assignment order using the same BindingConfiguration that will be
   passed to Bind. Stop at the first preparation failure. Failed preparation never becomes omission.
5. Construct typed assignments and call Bind. Its validation order, atomicity and diagnostic
   ordering remain those in the substitution contract. Do not reproduce them in a separate CLI
   binder.
6. Publish only a complete result accepted under the selected policy.

Preparation precedes binding even for duplicate or unknown keys. An ignored unknown encoded-image
assignment still requires a readable file or valid base64 and successful EncodedImage construction.
Preparation failure can therefore precede binding diagnostics. CLI acquisition and parsing failures
are not library semantic validation results. Preserve diagnostics from completed earlier phases; a
terminal library budget diagnostic replaces findings only within its own operation.

Inspect always returns failure when inspection reports errors, while still reporting any admitted
inputs. The `allow_scoped` policy applies only to bind. Warnings do not fail either command; there
is no general continue-on-error or warnings-as-errors option. Successful binding under
`allow_scoped` can coexist with unchanged inspection error diagnostics. Success means the chosen
operation policy was satisfied, not that the template is free of errors or guaranteed to render.

## Results and diagnostics

| Operation      | Standard output               | Standard error |
| -------------- | ----------------------------- | -------------- |
| inspect        | Text or JSON discovery report | Diagnostics    |
| bind to a file | Empty                         | Diagnostics    |
| bind to `-`    | SVG bytes only                | Diagnostics    |

Text diagnostics are the default. Successful operations with no findings emit no text diagnostics.
`--diagnostics json` selects one final JSON envelope, including on success with no findings. It is
independent of inspect's `--format`. No banners, progress messages or success prose contaminate
machine streams. JSON output is UTF-8 without BOM, with a terminating newline. Property order and
indentation are not contracts; array order is.

The [compatibility policy](compatibility.md#cli-and-diagnostic-evolution) allows additive output
fields and diagnostic codes in stable minor releases. Consumers ignore unknown output fields and
handle unfamiliar codes through severity and structured context. Input JSON remains strict; this
output extension rule does not relax request validation or permit new enum values without review.

### Human-readable output

These rules apply to text output only. Keep JSON shapes, complete raw evidence, codes, phase order,
exit categories and publication protections unchanged. Text is plain UTF-8 with no ANSI colors,
automatic terminal-width wrapping or terminal-dependent layout. Tool-authored line breaks separate
records; user-supplied text cannot introduce new report lines. Wording may improve without changing
these semantics; the examples are not whole-screen snapshot requirements.

#### Diagnostic layout

Lead with `Error:` or `Warning:` and a short explanation or corrective action. Follow with indented
source/context lines and a `Code:` line. For example:

```text
Error: Add a "value" to this binding.
  bindings.json — binding 1, /bindings/0/value
  Code: svt.cli.json

Error: "result.svg" already exists.
  Choose another name or use --overwrite.
  Code: svt.cli.output-exists
```

Render available source, request source, key, JSON pointer, assignment and limit context; omit
unavailable fields rather than printing `null`. Show both template and request locations when they
identify different evidence. Related SVG locations remain associated with their source and retain
order. For the stream marker `-`, say `stdin` when reading and `stdout` when publishing; retain `-`
in JSON. A literal filename such as `./-` is still a file, not a stream marker. The image `file`
field always names a file; its bare `-` is represented as `./-` in diagnostic source context to
avoid ambiguity with stdin.

Human binding numbers are one-based. JSON assignment indices and JSON-pointer array segments stay
zero-based: “binding 1” and `/bindings/0/value` identify the same entry. Do not invent an assignment
for aggregate failures. Label positions explicitly as `SVG line N, UTF-16 column M` or
`JSON line N, UTF-8 byte column M`; both line and column numbers remain one-based. Include positions
only when supplied; never attach a template column to a request path.

Keep stable codes and exact limit identifiers in detail lines. CLI-owned error messages should name
invalid fields/options and a useful remedy. Retain library messages and evidence rather than adding
a parallel table that rewrites every core diagnostic. Escape dynamic fields wherever they appear,
including inside messages; tool-controlled prose and line breaks are not JSON-quoted as a whole.

Distinguish missing files/directories, denied access and other I/O failures when the captured
failure establishes the cause. Use the caller-facing path and operation, such as `File not found:`
or `Cannot write to:`. If the cause is uncertain, use a neutral operation-specific explanation
instead of guessing. Do not forward raw exception text, temporary staging paths or expected-failure
stack traces. These refinements retain `svt.cli.io` and exit 3; output-exists/conflict codes retain
their existing distinct meanings. A remedy must not suggest bypassing an input collision or promise
rollback after publication.

#### Inspection layout

Show the resolved label first, retain the exact binding key, and name the kind in ordinary lowercase
text. Include an optional description preview and the existing target/hook locations:

```text
Heading [heading] — text
  Default: "Welcome"
  Target: text, SVG line 8, UTF-16 column 4
```

Inputs and targets retain structural discovery order. Show a raw Common default once, with target
locations beneath it; do not repeat a large shared value for every target. Raw Mixed displays
`Defaults differ` and a preview or absence marker for each target. All-Absent displays
`No authored default`. Present empty strings as `""`, distinct from absence. Neither absence nor
Mixed invents an effective default, and equal interpreted colors do not collapse raw Mixed. The text
report does not invoke optional interpretation or reorder inputs by presentation metadata.

A usable template with no inputs reports `No editable inputs found.` rather than only a heading. The
empty inventory itself is not an error; existing inspection findings still determine the exit
status. An unusable template remains `No usable template.` with its diagnostics on stderr.

#### Preview limits

The fixed per-field limit is 160 displayed Unicode code points after escaping. It applies to raw
value previews, descriptions and display labels. Identifying keys, paths, pointers, codes and limit
names remain complete, with escaping; the cap is not a whole-report size limit or a new input
budget. Surrounding quotes and the separate ` (shortened)` annotation do not count toward the 160
limit.

An escaped value of exactly 160 code points is complete. If it exceeds 160, emit the longest prefix
of whole escaped source characters that fits within 159, append `…` inside the value, and append
` (shortened)` outside any quotes. Never split a Unicode scalar, surrogate pair or escape sequence.
This counts code points, not screen cells or grapheme clusters; a combining or joined sequence can
end at a scalar boundary. Quoting and the separate annotation distinguish truncation from an
authored ellipsis. There is no terminal-width adjustment or preview-length option.

Escape and bound the preview incrementally; do not allocate a complete escaped copy or scan a long
suffix just to produce a short preview. Complete raw values remain available through
`--format json`.

For an image default beginning with ASCII case-insensitive `data:`, display exactly
`Embedded data (payload hidden)` instead of the header or payload, regardless of length. This is a
presentation rule based only on the prefix, not URI/media validation or optional interpretation;
malformed data-prefixed references are hidden too. Do not trim or normalize the value to detect the
prefix, decode content, fetch resources, infer size/type, or copy its payload for display. Other
image references use the ordinary bounded preview.

#### Unicode escaping

Preserve printable Unicode and ordinary punctuation, including accented text, non-Latin scripts and
emoji, without normalization. Apply one escaping policy to dynamic values, labels, descriptions,
keys, paths, pointers and message fragments:

- Render newline, carriage return and tab as `\n`, `\r` and `\t`.
- Escape remaining C0/C1 control characters (U+0000–U+001F and U+007F–U+009F), Unicode
  line/paragraph separators (U+2028/U+2029), and invisible directional controls U+061C,
  U+200E–U+200F, U+202A–U+202E and U+2066–U+206F. Joiners, variation selectors and emoji sequences
  otherwise remain readable; do not blanket-escape all non-ASCII or Unicode format characters.
- Use `\uXXXX` with uppercase hexadecimal digits for the remaining escaped characters. If an
  external UTF-16 string contains an unpaired surrogate, render that code unit as `\uXXXX` rather
  than silently replacing it. Valid supplementary Unicode scalars remain intact.
- Escape a literal backslash as `\\` and a double quote as `\"` inside quoted values. Outside quoted
  values, retain ordinary quotation punctuation but still escape backslashes and controls.

Escaping makes control characters visible; it does not promise identical glyph shaping in every
terminal or disambiguate all Unicode lookalikes. JSON keeps its existing serialization policy, and
all underlying raw strings remain unchanged.

### Discovery JSON

The root contains `format_version: 0`, boolean `template_available` and ordered `inputs`. Each input
contains `key`, `label`, `kind`, ordered `targets` and `default`, corresponding to the complete
[inspection model](inspection-api.md#immutable-result-model). Field names and enum values use
snake_case. Each target contains `hook`, `element_name`, `location` and `default`. Defaults carry
`state` and `raw`; target defaults also carry `source`. Use explicit nulls for unavailable values.
Retain raw strings, locations, source evidence and discovery order without interpretation.

Diagnostics appear only on stderr, not in this discovery object. A root rejection produces
`template_available: false` and an empty inputs array. A scoped error may accompany usable inputs.
If inspection never runs because acquisition or request validation failed, stdout is empty.

[Metadata](https://github.com/svt-project/svt-spec/blob/master/spec/metadata.md) fields are included
on every discovery report, with `format_version: 0`:

- Each input also contains `description`, `label_source` (`key`, `element` or `metadata`) and
  `order`. `label` is the resolved effective label. Description and order use explicit nulls when
  absent or discarded. Inputs and targets retain structural discovery order.
- The root also contains ordered `input_groups`. Each entry contains `key`, `label`, `description`,
  `order` and ordered `input_keys`. The keys reference the existing inputs; do not repeat full input
  objects. Group order, input order, omissions and unnamed-section nulls follow the
  [presentation model](inspection-api.md#presentation-metadata-extension).
- A root rejection or usable template with no inputs has an empty `input_groups` array. A usable
  hook-only template with inputs has one unnamed section. Every admitted key appears exactly once
  across the groups, with exact case-sensitive spelling.

Text reports continue to use structural discovery order and display the resolved input labels. This
extension does not add a form renderer or change binding JSON, exit codes or diagnostic streams.
Metadata warnings alone do not fail inspect or block bind.

### Diagnostic JSON

```json
{
  "format_version": 0,
  "command": "bind",
  "outcome": "succeeded",
  "exit_code": 0,
  "diagnostics": []
}
```

Command is `inspect`, `bind`, or null if no command was recognized. Outcome is `succeeded`, `failed`
or `cancelled`. A diagnostic entry contains these fields, with explicit nulls where unavailable:

- `phase`: `invocation`, `configuration`, `input`, `inspection`, `preparation`, `binding` or
  `publication`.
- Unchanged library evidence: `code`, `severity`, `message`, `key`, `location`, `related_locations`
  and `limit`. Severity uses `error` or `warning`; related locations is always an array.
- CLI context: `source`, `request_source`, `json_pointer`, `assignment_index` and `json_position`.

Source identifies the input associated with the primary finding, or `-` for stdin. When a library
location is present, source names the original SVG. A preparation I/O failure identifies the image
file; an output failure identifies its destination. Request source separately names the binding or
configuration JSON associated with `json_pointer` and `json_position`, so template coordinates
cannot be mistaken for positions in a binding file. Do not expose temporary-file paths as user input
locations. Assignment indices are zero-based; duplicate-key diagnostics identify the first
assignment. Use null when context is unavailable, including an assignment index for an aggregate
budget finding. Image-creation diagnostics retain their original null key/location; assignment
context belongs in the CLI fields.

Library locations retain one-based UTF-16 line and column conventions and related-location order.
JSON parser positions are separate objects with one-based `line` and UTF-8 `byte_column`, excluding
an initial BOM from column counting. Do not mislabel byte columns as XML character columns. Retain
findings in phase order, and library findings in their original order within a phase. Message text
is explanatory, not a machine contract; do not assert exact parser wording.

Library diagnostic codes remain unchanged. CLI-owned codes are:

| Code                      | Meaning                                            |
| ------------------------- | -------------------------------------------------- |
| `svt.cli.usage`           | Invalid command or options                         |
| `svt.cli.configuration`   | Invalid configuration shape, value or policy       |
| `svt.cli.json`            | Malformed binding JSON or invalid request shape    |
| `svt.cli.base64`          | Invalid encoded-image base64                       |
| `svt.cli.limit`           | CLI transport/depth limit exceeded                 |
| `svt.cli.io`              | File or stream acquisition/publication failure     |
| `svt.cli.output-exists`   | Destination exists without overwrite authorization |
| `svt.cli.output-conflict` | Destination conflicts with a consumed input        |
| `svt.cli.cancelled`       | Operation cancelled                                |
| `svt.cli.internal`        | Unexpected internal failure                        |

CLI transport limit names are qualified settings such as `cli.max_bindings_file_bytes`, or fixed
bounds `cli.max_configuration_bytes` and `cli.max_json_depth`. Early CLI acquisition guards also use
`svt.cli.limit`, with the exact library option name when enforcing a library budget such as
MaxSourceBytes, MaxBindings or MaxTotalEncodedImageBytes. Actual library findings retain their
original `svt.limit.exceeded` code and public limit name. A malformed configuration uses
`svt.cli.configuration`, rather than the binding-file JSON code. Failure to read a configuration
file is an I/O failure. Expected authoring failures must not become stack traces; unexpected defects
must not be relabeled as invalid authoring input.

### Exit codes

| Code | Meaning                                                                                                     |
| ---- | ----------------------------------------------------------------------------------------------------------- |
| 0    | Operation completed under selected policy; also help/version                                                |
| 1    | Input or processing rejected, including malformed binding JSON, inspection/binding errors and input budgets |
| 2    | Invalid invocation or configuration, including configuration loading limits                                 |
| 3    | File or stream acquisition/publication failure, overwrite refusal or output conflict                        |
| 4    | Unexpected internal failure                                                                                 |
| 130  | Cancellation                                                                                                |

Use the first failing lifecycle phase; do not continue later work to collect unrelated failures.
Scoped inspection errors explicitly permitted for bind do not select exit 1. Publication failure
after successful binding selects exit 3. Exit status and selected policy determine command success;
the diagnostic array alone cannot determine it.

## Publication

Bind requires an explicit destination. Refuse existing destinations unless `--overwrite` is
supplied. Do not create missing destination directories automatically. Refuse collisions with the
template, binding file, configuration and image files consumed by the invocation, even with
overwrite enabled. There is no in-place template update. Inputs are captured independently; do not
reopen them during binding to obtain a newer version.

Collision protection identifies normalized, resolved directory entries, not every physical-file
alias. Resolve input paths and the destination's parent path through symbolic links, respecting the
filesystem's actual case behavior rather than assuming it from the operating system. Refuse a
destination that resolves to a consumed input entry. Refuse a symbolic link at the final destination
component, including a dangling link, even with `--overwrite`; report `svt.cli.output-conflict` with
exit 3. Check these restrictions before publication without following a final destination link to
write its target.

A different hard-linked name is a distinct destination entry. It still requires `--overwrite` when
it exists. Publication replaces that entry rather than modifying the shared file's contents, so
bytes accessible through the input entry remain unchanged. No platform-wide file-identity search or
detection of every hard-link alias is promised. Collision protection is not a security guarantee
against concurrent adversarial directory replacement; normal destination creation races remain
subject to the no-overwrite rule below.

For file output, stage complete bytes in the destination directory and publish using supported
rename/replacement semantics. Never truncate an existing destination before success. A destination
appearing after an initial check must still be protected when overwrite is absent. Unsupported
publication semantics fail rather than falling back to a destructive copy. Remove unpublished
staging files when possible. This promises complete-file publication, not crash durability or a
transaction spanning stdout, stderr and the filesystem. Publication is the commit point:
cancellation before it leaves the destination unchanged; once publication completes, do not roll it
back in response to later cancellation or diagnostic delivery failure. A stderr write failure
returns exit 3 when the process can still report status, but cannot guarantee a complete JSON
envelope. The output may already have been published. Do not retry by appending a second envelope to
a partial one. Abrupt process termination cannot guarantee diagnostics or staging-file cleanup.

Output bytes follow the library contract: UTF-8 without BOM, no XML declaration, no appended newline
and no pretty-printing. SVT annotations and meaningful content remain. Even empty bindings follow
this serialization policy rather than copying original source bytes verbatim.

For stdout, finish binding before writing any SVG bytes. A failed validation cannot emit partial
SVG; a stream failure or cancellation during delivery can. The CLI cannot control shell redirection
that opens or truncates a file before it starts. File publication guarantees apply to a named
`--output` destination, not to shell redirection.

## Conformance

Reuse portable inspection and substitution cases, with CLI-specific process tests for argument
errors, strict JSON, duplicate properties versus assignments, configuration precedence, partial
inspection, ignored-key preparation failures, relative image paths, stdin ownership, stream
separation, structured diagnostics and exit codes. Cover limits at equality and excess, bounded
reads and base64 preparation, existing-output protection, publication races, cancellation and I/O
failures. Cover resolved-path collisions, linked parent directories, final symbolic links (including
dangling links), distinct hard-linked entries and filesystem case behavior. Verify rejected unpaired
surrogates in JSON names and values, valid pairs, ignored/duplicate-key precedence, and
XML-forbidden scalar values that reach binding. Test file behavior on supported operating systems
without sleeps or polling. Keep process fixtures independent of consumer applications and generated
evidence out of source control.

The presentation implementation must add behavioral tests for help aliases and strict error
precedence, command-specific examples/defaults, no-argument stderr usage, and JSON-only error
reporting. Cover both source locations, one-based human binding numbers versus zero-based pointers,
missing context, related locations and safe I/O wording. Test Common/Mixed/Absent and empty
defaults, empty discovery versus unusable templates, 159/160/161-code-point boundaries, escape
expansion, supplementary characters, combining/joined text, literal ellipses, data-prefixed image
hiding and complete JSON evidence. Exercise terminal controls, bidirectional controls and malformed
UTF-16 at available boundaries. Verify exact prerelease/build-metadata preservation, present/absent
revision metadata, missing-version failure and no input/Git access for help/version. Prefer semantic
assertions over whole-screen or documentation-wording snapshots; validate actual packed-tool
identity in the separate distribution work.

The CLI request/report protocol is defined here. Existing conformance JSON remains a test fixture
format; similar field names do not make fixture options or expected-result files CLI inputs. No
package publication, rendering integration or optional value interpretation is implied by this
contract.
