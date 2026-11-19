import test from "node:test";
import assert from "node:assert/strict";
import { normalizeCoverage } from "./evidence.mjs";

const module = "Bathos.Graphics.SvgTemplates";
const source = `src/${module}/Template.cs`;
const files = new Set([source]);
const fixture = (path) =>
  `SF:${path}\nFN:1,Inspect()\nFNDA:1,Inspect()\nDA:1,1\nDA:2,0\nBRDA:1,0,0,1\nBRDA:1,0,1,-\nLF:2\nLH:1\nBRF:2\nBRH:1\nFNF:1\nFNH:1\nend_of_record\n`;
const check = (text, root = "/checkout") => normalizeCoverage(text, root, files, [module]);

test("coverage normalizes POSIX, Windows, deterministic and relative paths without changing hits", () => {
  for (const [root, path] of [
    ["/checkout", `/checkout/${source}`],
    ["C:\\work", `C:\\work\\${source.replaceAll("/", "\\")}`],
    ["/checkout", `/_/${source}`],
    ["/checkout", source],
  ]) {
    const result = check(fixture(path), root);
    assert.equal(result.text, fixture(source));
    assert.deepEqual(result.totals[module], {
      lines: 2,
      coveredLines: 1,
      branches: 2,
      coveredBranches: 1,
    });
  }
});

test("coverage rejects missing, truncated, duplicate or inconsistent records", () => {
  const valid = fixture(source);
  for (const text of [
    "",
    valid.replace("end_of_record", ""),
    valid + valid,
    valid.replace("LF:2", "LF:3"),
    valid.replace("BRH:1", "BRH:2"),
    valid.replace("DA:2,0", "DA:2,NaN"),
  ])
    assert.throws(() => check(text));
});

test("coverage rejects foreign sources, traversal and non-production files", () => {
  for (const path of [
    `/other/${source}`,
    `../${source}`,
    `/checkout-other/${source}`,
    "tests/Test.cs",
    "src/Other/Foo.cs",
  ])
    assert.throws(() => check(fixture(path)));
  assert.throws(() =>
    normalizeCoverage(fixture("tests/Test.cs"), "/checkout", new Set(["tests/Test.cs"]), [module]),
  );
});

test("coverage requires the complete production scope and meaningful instrumentation", () => {
  assert.throws(() =>
    normalizeCoverage(fixture(source), "/checkout", files, [
      module,
      "Bathos.Graphics.SvgTemplates.Cli",
    ]),
  );
  assert.throws(() => check(fixture(source).replace("DA:1,1", "DA:1,0").replace("LH:1", "LH:0")));
  assert.throws(() =>
    check(
      fixture(source)
        .replace(/BRDA:.*\n/g, "")
        .replace("BRF:2", "BRF:0")
        .replace("BRH:1", "BRH:0"),
    ),
  );
});
