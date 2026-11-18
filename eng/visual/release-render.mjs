import assert from "node:assert/strict";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import { root, command, cli, inspect, assertInventory, resourceFailure } from "./common.mjs";
import { runRendering } from "./rendering.mjs";

assert.ok(process.argv.length <= 4, "Usage: release-render.mjs [corpus [pre-save-corpus]]");
await runRendering(
  {
    purpose: "release-check",
    source: resolve(process.argv[2] || resolve(root, "examples/authoring")),
    baseline: process.argv[3] ? resolve(process.argv[3]) : null,
  },
  async ({ work, output, suite, report, baseline, engines, compare }) => {
    for (const name of suite.cases) {
      const folder = resolve(work, name),
        input = resolve(folder, "input.svg");
      assertInventory(
        JSON.parse(await readFile(resolve(folder, "inventory.json"))),
        await inspect(input),
      );
      for (const mode of ["empty", "partial", "complete"]) {
        const result = await command("dotnet", [
          cli,
          "bind",
          input,
          "--bindings",
          resolve(folder, mode + ".json"),
          "--output",
          resolve(folder, mode + "-bound.svg"),
          "--diagnostics",
          "json",
        ]);
        assert.deepEqual(JSON.parse(result.stderr).diagnostics, []);
      }
      for (const [engine, render] of engines) {
        const image = (name) => resolve(output, `${engine}-${name}.png`);
        await render(input, image(name + "-original"));
        if (baseline) {
          await render(
            resolve(work, "__baseline", name, "input.svg"),
            image(name + "-before-save"),
          );
          await compare(
            `${engine}/${name}/authoring`,
            image(name + "-before-save"),
            image(name + "-original"),
          );
        }
        await render(input, image(name + "-repeat"));
        await compare(
          `${engine}/${name}/repeat`,
          image(name + "-original"),
          image(name + "-repeat"),
        );
        for (const mode of ["empty", "partial", "complete"]) {
          const expected =
            mode === "empty" ? image(name + "-original") : image(name + "-" + mode + "-expected");
          if (mode !== "empty") await render(resolve(folder, mode + ".svg"), expected);
          const actual = image(name + "-" + mode);
          await render(resolve(folder, mode + "-bound.svg"), actual);
          await compare(`${engine}/${name}/${mode}`, expected, actual);
        }
      }
    }
    for (const [engine, render] of engines) {
      const expected = resolve(output, engine + "-reference-embedded.png");
      await render(resolve(work, "references/embedded.svg"), expected);
      const actual = resolve(output, engine + "-reference-relative.png");
      await render(resolve(work, "references/relative.svg"), actual);
      await compare(engine + "/references/relative", expected, actual);
      // Relocation is prepared explicitly by the host, preserving the target resource.
      const relocated = resolve(work, "relocated/deep");
      await mkdir(relocated, { recursive: true });
      const xml = await readFile(resolve(work, "references/relative.svg"), "utf8");
      await writeFile(
        resolve(relocated, "reference.svg"),
        xml.replace("../assets/portrait.png", "../../assets/portrait.png"),
      );
      const relocatedImage = resolve(output, engine + "-reference-relocated.png");
      await render(resolve(relocated, "reference.svg"), relocatedImage);
      await compare(engine + "/references/relocated", expected, relocatedImage);
      const cssInput = resolve(work, "hazards/css-override.svg");
      const cssOutput = resolve(work, "hazards/css-bound.svg");
      const request = resolve(work, "hazards/css-bindings.json");
      await writeFile(
        request,
        JSON.stringify({
          format_version: 0,
          bindings: [{ key: "accent", kind: "paint", value: "blue" }],
        }),
      );
      if (engine === "chrome") {
        const result = await command("dotnet", [
          cli,
          "bind",
          cssInput,
          "--bindings",
          request,
          "--output",
          cssOutput,
          "--diagnostics",
          "json",
        ]);
        assert.deepEqual(JSON.parse(result.stderr).diagnostics, []);
      }
      const cssExpected = resolve(output, engine + "-css-original.png");
      const cssActual = resolve(output, engine + "-css-bound.png");
      await render(cssInput, cssExpected);
      await render(cssOutput, cssActual);
      await compare(engine + "/hazards/css-override", cssExpected, cssActual);
    }
    // A well-formed, bindable reference to a missing image must fail the rendering harness.
    const missing = resolve(work, "hazards/missing-resource.svg");
    await assert.rejects(engines[0][1](missing, resolve(output, "missing.png")), (error) =>
      resourceFailure("chrome", error),
    );
    await assert.rejects(engines[1][1](missing, resolve(output, "missing-resvg.png")), (error) =>
      resourceFailure("resvg", error),
    );
    report.missing_resource_rejected = true;
  },
);
