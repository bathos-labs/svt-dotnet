import test from "node:test";
import assert from "node:assert/strict";
import { PNG } from "pngjs";
import {
  assertInventory,
  assertSuite,
  pixels,
  resourceFailure,
  observeOptimizedExport,
} from "./common.mjs";

test("successful empty discovery cannot hide stripped bindings", () => {
  assert.throws(
    () =>
      assertInventory({ inputs: [{ key: "heading" }] }, { template_available: true, inputs: [] }),
    /inventory changed/,
  );
});
test("inventory ignores coordinates but preserves raw defaults and labels", () => {
  const expected = {
    inputs: [{ key: "heading", label: "Heading", targets: [{ default: { raw: "  x  " } }] }],
  };
  const report = {
    template_available: true,
    inputs: [
      {
        key: "heading",
        label: "Heading",
        targets: [{ location: { line: 40, column: 7 }, default: { raw: "  x  " } }],
      },
    ],
  };
  assertInventory(expected, report);
  report.inputs[0].targets[0].default.raw = "x";
  assert.throws(() => assertInventory(expected, report));
});
test("inventory detects metadata loss and changed presentation membership or order", () => {
  const expected = {
    inputs: [
      {
        key: "a",
        label: "Central",
        label_source: "metadata",
        description: "Help",
        order: 20,
        targets: [],
      },
      {
        key: "b",
        label: "Inline",
        label_source: "element",
        description: null,
        order: 10,
        targets: [],
      },
    ],
    input_groups: [
      { key: "g", label: "Group", description: "Group help", order: 0, input_keys: ["b", "a"] },
    ],
  };
  const original = { template_available: true, ...expected };
  assertInventory(expected, original);
  for (const mutate of [
    (report) => {
      report.inputs[0].description = null;
    },
    (report) => {
      report.inputs[0].label_source = "element";
    },
    (report) => {
      report.input_groups = [];
    },
    (report) => {
      delete report.input_groups;
    },
    (report) => {
      report.input_groups[0].input_keys.reverse();
    },
    (report) => {
      report.input_groups[0].description = null;
    },
    (report) => {
      report.input_groups[0].order = null;
    },
  ]) {
    const changed = structuredClone(original);
    mutate(changed);
    assert.throws(() => assertInventory(expected, changed));
  }
});
test("comparison checks decoded samples, alpha, dimensions and exact differences", () => {
  const image = new PNG({ width: 2, height: 1 });
  image.data.fill(255);
  const original = PNG.sync.write(image);
  assert.equal(pixels(original, PNG.sync.write(image, { deflateLevel: 1 })).changed_pixels, 0);
  image.data[3] = 254;
  const changed = pixels(original, PNG.sync.write(image));
  assert.equal(changed.changed_pixels, 1);
  assert.equal(changed.maximum_channel_difference, 1);
  assert.throws(() => pixels(original, PNG.sync.write(new PNG({ width: 1, height: 2 }))));
});

test("resource probes cannot turn crashes or timeouts into expected limitations", () => {
  for (const engine of ["chrome", "resvg"]) {
    assert.equal(resourceFailure(engine, new Error("Protocol timeout")), false);
    assert.equal(resourceFailure(engine, new Error("Browser crashed")), false);
  }
  assert.equal(
    resourceFailure("chrome", new Error("EncodingError: The source image cannot be decoded.")),
    true,
  );
  assert.equal(
    resourceFailure(
      "resvg",
      new Error("Renderer diagnostics: portrait.png is not a path to an image"),
    ),
    true,
  );
});

test("a release suite must exercise distinct named cases with a pinned capture configuration", () => {
  const suite = {
    format_version: 0,
    width: 480,
    height: 320,
    pixel_tolerance: 0,
    font_family: "DejaVu Sans",
    cases: ["text-card"],
  };
  assertSuite(suite);
  for (const change of [
    { cases: [] },
    { cases: ["text-card", "text-card"] },
    { cases: ["../text-card"] },
    { width: 0 },
    { height: 1.5 },
    { pixel_tolerance: 1 },
    { font_family: "Arial" },
  ])
    assert.throws(() => assertSuite({ ...suite, ...change }));
});

test("optimized export distinguishes changed inventory and the observed unavailable extension", async () => {
  const expected = { inputs: [{ key: "heading", targets: [] }] };
  const exportSvg = async () => ({ stderr: "" });
  assert.equal(
    (
      await observeOptimizedExport(
        exportSvg,
        async () => ({ template_available: true, ...expected }),
        expected,
      )
    ).status,
    "inventory-passed",
  );
  for (const template_available of [true, false])
    assert.equal(
      (
        await observeOptimizedExport(
          exportSvg,
          async () => ({ template_available, inputs: [] }),
          expected,
        )
      ).status,
      "inventory-changed",
    );
  const unavailable = await observeOptimizedExport(
    async () => ({
      stderr: "InkFileExportCmd::do_export_vector: Failed to save Inkscape file to: output.svg\n",
    }),
    async () => assert.fail("Unavailable export must not be inspected"),
    expected,
  );
  assert.equal(unavailable.status, "unavailable");
});

test("optimized export fails on infrastructure, unexpected diagnostics and unreadable inventory", async () => {
  const expected = { inputs: [] };
  const exportSvg = async () => ({ stderr: "" });
  const inspectSvg = async () => ({ template_available: true, inputs: [] });
  for (const error of [
    new Error("spawn ENOENT"),
    new Error("Process timeout"),
    Object.assign(new Error("Failed to save Inkscape file"), { killed: true }),
    new SyntaxError("Invalid JSON"),
    new assert.AssertionError({ message: "Unexpected diagnostic" }),
  ]) {
    await assert.rejects(
      observeOptimizedExport(
        async () => {
          throw error;
        },
        inspectSvg,
        expected,
      ),
      (actual) => actual === error,
    );
    await assert.rejects(
      observeOptimizedExport(
        exportSvg,
        async () => {
          throw error;
        },
        expected,
      ),
      (actual) => actual === error,
    );
  }
  await assert.rejects(
    observeOptimizedExport(
      async () => ({ stderr: "Unhandled extension failure" }),
      inspectSvg,
      expected,
    ),
  );
  await assert.rejects(observeOptimizedExport(exportSvg, async () => ({}), expected));
});
