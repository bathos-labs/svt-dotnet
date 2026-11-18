import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { root, pixels, resourceFailure } from "./common.mjs";
import { runRendering } from "./rendering.mjs";

assert.equal(process.argv.length, 2, "Usage: probe-render.mjs");
await runRendering(
  { purpose: "exploratory-probe", source: resolve(root, "examples/authoring") },
  async ({ work, output, report, engines }) => {
    report.observations = [];
    for (const [engine, render] of engines) {
      // A positive control must render before a resource failure can be characterized.
      const expected = resolve(output, engine + "-reference-embedded.png");
      await render(resolve(work, "references/embedded.svg"), expected);
      try {
        const xmlBaseImage = resolve(output, engine + "-reference-xml-base.png");
        await render(resolve(work, "references/xml-base.svg"), xmlBaseImage);
        const diff = pixels(await readFile(expected), await readFile(xmlBaseImage));
        report.observations.push({
          engine,
          case: "xml:base",
          status: diff.changed_pixels === 0 ? "passed" : "renderer-limitation",
          changed_pixels: diff.changed_pixels,
        });
      } catch (error) {
        // Only the observed resource-resolution failure belongs to this limitation.
        // A browser crash, timeout or unrelated assertion must fail the run.
        if (!resourceFailure(engine, error)) throw error;
        report.observations.push({
          engine,
          case: "xml:base",
          status: "renderer-limitation",
          reason: String(error.message).replaceAll(root, "<repository>"),
        });
      }
    }
  },
);
