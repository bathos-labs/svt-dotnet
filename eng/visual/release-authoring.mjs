import assert from "node:assert/strict";
import { readFile, writeFile, mkdir, cp, copyFile, mkdtemp } from "node:fs/promises";
import { resolve, relative } from "node:path";
import { release } from "node:os";
import {
  root,
  pins,
  command,
  inspect,
  assertInventory,
  assertSuite,
  fileHash,
  cli,
} from "./common.mjs";

const inkscape = process.argv[2];
assert.ok(
  inkscape && process.argv.length === 3,
  "Usage: release-authoring.mjs <Inkscape executable>",
);
const version = (await command(inkscape, ["--version"])).stdout.trim();
assert.ok(version.startsWith(`Inkscape ${pins.inkscape.version} `));
const source = resolve(root, "examples/authoring");
await mkdir(resolve(root, "artifacts"), { recursive: true });
const output = await mkdtemp(resolve(root, "artifacts/native-release-check-authoring-"));
const report = {
  format_version: 0,
  purpose: "release-check",
  git_head: (await command("git", ["rev-parse", "HEAD"])).stdout.trim(),
  dirty: Boolean((await command("git", ["status", "--porcelain"])).stdout.trim()),
  hashes: {},
  tool: { ...pins.inkscape, actual_version: version, binary_sha256: await fileHash(inkscape) },
  environment: {
    platform: process.platform,
    release: release(),
    architecture: process.arch,
    node: process.version,
    dotnet: (await command("dotnet", ["--version"])).stdout.trim(),
  },
  scope:
    "Inkscape command-line save/reopen and attribute-edit paths; interactive UI evidence is separate.",
  modes: [],
};
function recordDiagnostics(result, item, operation) {
  if (result.stderr.trim())
    item.diagnostics.push({ operation, stderr: result.stderr.replaceAll(root, "<repository>") });
}
for (const name of ["release-authoring.mjs", "common.mjs", "tools.json"])
  report.hashes["eng/visual/" + name] = await fileHash(resolve(root, "eng/visual", name));
const suite = JSON.parse(await readFile(resolve(source, "suite.json")));
assertSuite(suite);
const tests = resolve(
  root,
  "tests/Bathos.Graphics.SvgTemplates.Cli.Tests/bin/Release/net10.0/Bathos.Graphics.SvgTemplates.Cli.Tests.dll",
);
for (const [name, path] of [
  ["svt.dll", cli],
  ["library.dll", resolve(cli, "../Bathos.Graphics.SvgTemplates.dll")],
  ["tests.dll", tests],
  ["suite.json", resolve(source, "suite.json")],
])
  report.hashes[name] = await fileHash(path);
async function verifyBindings(destination) {
  try {
    const api = await command(
      "dotnet",
      [tests, "--filter", "FullyQualifiedName~AuthoringExamples", "--minimum-expected-tests", "1"],
      { env: { ...process.env, SVT_AUTHORING_ROOT: destination } },
    );
    await writeFile(resolve(destination, "api-cli.log"), api.stdout + api.stderr);
  } catch (error) {
    await writeFile(
      resolve(destination, "api-cli.log"),
      (error.stdout ?? "") + (error.stderr ?? "") + "\n" + error.message,
    );
    throw error;
  }
}
for (const name of suite.cases)
  for (const file of [
    "input.svg",
    "partial.svg",
    "complete.svg",
    "inventory.json",
    "empty.json",
    "partial.json",
    "complete.json",
  ])
    report.hashes[`${name}/${file}`] = await fileHash(resolve(source, name, file));
for (const mode of ["native", "plain"]) {
  const destination = resolve(output, mode);
  await cp(source, destination, { recursive: true });
  const item = { mode, status: "running", cases: [], diagnostics: [] };
  report.modes.push(item);
  try {
    for (const name of suite.cases) {
      for (const variant of ["input", "partial", "complete"]) {
        const staging = resolve(output, "staging", mode, name, variant);
        await mkdir(staging, { recursive: true });
        // Same authored document name in independent processes, avoiding incidental docname differences.
        await copyFile(resolve(source, name, variant + ".svg"), resolve(staging, "input.svg"));
        for (const pass of ["first", "reopened"]) {
          const target = resolve(staging, pass);
          await mkdir(target);
          const input =
            pass === "first" ? resolve(staging, "input.svg") : resolve(staging, "first/input.svg");
          const result = await command(inkscape, [
            input,
            "--export-type=svg",
            ...(mode === "plain" ? ["--export-plain-svg"] : []),
            "--export-filename=" + resolve(target, "input.svg"),
          ]);
          recordDiagnostics(result, item, `${name}/${variant}/${pass}`);
        }
        await copyFile(
          resolve(staging, "reopened/input.svg"),
          resolve(destination, name, variant + ".svg"),
        );
      }
      assertInventory(
        JSON.parse(await readFile(resolve(source, name, "inventory.json"))),
        await inspect(resolve(destination, name, "input.svg")),
      );
      item.cases.push({ name, inventory: "passed" });
    }
    await verifyBindings(destination);
    item.api_cli = "passed";
    item.status = item.diagnostics.length === 0 ? "passed" : "failed";
    if (item.status === "failed")
      item.error = "Inkscape diagnostics; preservation checks completed.";
  } catch (error) {
    item.status = "failed";
    item.error = String(error.message).replaceAll(root, "<repository>");
  }
}
// Deliberate geometry and direct-paint edits use Inkscape's own documented actions.
const edits = [
  {
    name: "geometry",
    case: "text-card",
    actions: "select-by-id:heading;object-set-attribute:x,32",
  },
  {
    name: "paint",
    case: "shared-palette",
    actions: "select-by-id:accent;object-set-attribute:fill,#526a9d",
  },
];
report.edits = [];
for (const mode of ["native", "plain"])
  for (const edit of edits) {
    const item = { mode, name: edit.name, status: "running", diagnostics: [] };
    report.edits.push(item);
    try {
      const destination = resolve(output, `${mode}-${edit.name}`);
      await cp(resolve(output, mode), destination, { recursive: true });
      await writeFile(
        resolve(destination, "suite.json"),
        JSON.stringify({ ...suite, cases: [edit.case] }, null, 2) + "\n",
      );
      // Expected replacement artwork is edited independently; no binder output becomes a baseline.
      // A paint assignment replaces the edited fill, so its replacement expectations stay unchanged.
      for (const variant of edit.name === "geometry"
        ? ["input", "partial", "complete"]
        : ["input"]) {
        const staging = resolve(output, "staging", `${mode}-${edit.name}`, variant);
        await mkdir(staging, { recursive: true });
        const path = resolve(staging, "input.svg");
        const result = await command(inkscape, [
          resolve(output, mode, edit.case, variant + ".svg"),
          "--export-type=svg",
          ...(mode === "plain" ? ["--export-plain-svg"] : []),
          "--actions=" + edit.actions + ";export-filename:" + path + ";export-do",
        ]);
        recordDiagnostics(result, item, `${edit.case}/${variant}/edit`);
        const second = resolve(destination, edit.case, variant + ".svg");
        const reopened = await command(inkscape, [
          path,
          "--export-type=svg",
          ...(mode === "plain" ? ["--export-plain-svg"] : []),
          "--export-filename=" + second,
        ]);
        recordDiagnostics(reopened, item, `${edit.case}/${variant}/reopen`);
        if (edit.name === "geometry") {
          const text = await readFile(second, "utf8");
          const heading = text.match(/<text\b[^>]*\bid="heading"[^>]*>/)?.[0];
          assert.ok(heading, "Edited heading missing");
          assert.match(heading, /\bx="32"/);
        }
      }
      const expected = JSON.parse(await readFile(resolve(source, edit.case, "inventory.json")));
      if (edit.name === "paint") {
        const accent = expected.inputs.find((input) => input.key === "accent");
        accent.targets[0].default.raw = "#526a9d";
        accent.default = { state: "mixed", raw: null };
      }
      await writeFile(
        resolve(destination, edit.case, "inventory.json"),
        JSON.stringify(expected, null, 2) + "\n",
      );
      const edited = resolve(destination, edit.case, "input.svg");
      assertInventory(expected, await inspect(edited));
      await verifyBindings(destination);
      item.api_cli = "passed";
      item.sha256 = await fileHash(edited);
      item.status = item.diagnostics.length === 0 ? "passed" : "failed";
      if (item.status === "failed")
        item.reason = "Inkscape diagnostics; preservation checks completed.";
    } catch (error) {
      item.status = "failed";
      item.reason = String(error.message).replaceAll(root, "<repository>");
    }
  }
report.status = [...report.modes, ...report.edits].every((item) => item.status === "passed")
  ? "passed"
  : "failed";
await writeFile(resolve(output, "report.json"), JSON.stringify(report, null, 2) + "\n");
console.log(`${report.status}: ${relative(root, output)}/report.json`);
if (report.status !== "passed") process.exitCode = 1;
