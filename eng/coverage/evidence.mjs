import assert from "node:assert/strict";

const modules = ["Bathos.Graphics.SvgTemplates", "Bathos.Graphics.SvgTemplates.Cli"];

// LCOV keeps both line and branch evidence inspectable without an XML parser dependency.
export function normalizeCoverage(text, repositoryRoot, sourceFiles, expectedModules) {
  const root = repositoryRoot.replaceAll("\\", "/").replace(/\/$/, "") + "/";
  const records = text.trim().split("end_of_record");
  assert.equal(records.pop(), "", "Truncated coverage record");
  assert.ok(records.length > 0, "Coverage contains no source records");
  const seen = new Set();
  const totals = {};
  const normalized = records.map((record) => {
    const lines = record
      .trim()
      .split(/\r?\n/)
      .filter((line) => !line.startsWith("TN:"));
    assert.ok(lines[0]?.startsWith("SF:"), "Coverage record must start with a source file");
    let name = lines[0].slice(3).replaceAll("\\", "/");
    if (name.startsWith(root)) name = name.slice(root.length);
    else if (name.startsWith("/_/")) name = name.slice(3);
    assert.ok(sourceFiles.has(name), `Coverage source is not repository-owned: ${name}`);
    const module = modules.find((item) => name.startsWith(`src/${item}/`));
    assert.ok(module && expectedModules.includes(module), `Unexpected coverage source: ${name}`);
    assert.ok(!seen.has(name), `Duplicate coverage source: ${name}`);
    seen.add(name);
    lines[0] = `SF:${name}`;
    const summary = {};
    const hits = [];
    const branches = [];
    for (const line of lines.slice(1)) {
      if (/^DA:[1-9]\d*,\d+$/.test(line)) hits.push(Number(line.split(",")[1]));
      else if (/^BRDA:[1-9]\d*,\d+,\d+,(\d+|-)$/.test(line)) branches.push(line.split(",")[3]);
      else if (/^(LF|LH|BRF|BRH|FNF|FNH):\d+$/.test(line)) {
        const [key, value] = line.split(":");
        assert.ok(!(key in summary), `Duplicate coverage total: ${key}`);
        summary[key] = Number(value);
        assert.ok(Number.isSafeInteger(summary[key]), "Invalid coverage count");
      } else assert.match(line, /^(FN:\d+|FNDA:\d+),.+$/, `Invalid LCOV entry: ${line}`);
    }
    assert.ok(hits.every(Number.isSafeInteger), "Invalid line hits");
    assert.ok(
      branches.every((value) => value === "-" || Number.isSafeInteger(Number(value))),
      "Invalid branch hits",
    );
    assert.equal(summary.LF, hits.length, "Incorrect instrumented line count");
    assert.equal(
      summary.LH,
      hits.filter((value) => value > 0).length,
      "Incorrect covered line count",
    );
    assert.equal(summary.BRF, branches.length, "Incorrect instrumented branch count");
    assert.equal(
      summary.BRH,
      branches.filter((value) => value !== "-" && Number(value) > 0).length,
      "Incorrect covered branch count",
    );
    const total = (totals[module] ??= {
      lines: 0,
      coveredLines: 0,
      branches: 0,
      coveredBranches: 0,
    });
    total.lines += summary.LF;
    total.coveredLines += summary.LH;
    total.branches += summary.BRF;
    total.coveredBranches += summary.BRH;
    return lines.join("\n") + "\nend_of_record\n";
  });
  assert.deepEqual(
    Object.keys(totals).sort(),
    [...expectedModules].sort(),
    "Incomplete coverage modules",
  );
  for (const [name, total] of Object.entries(totals)) {
    assert.ok(
      total.coveredLines > 0 && total.branches > 0,
      `No executed lines or instrumented branches: ${name}`,
    );
  }
  return { text: normalized.join(""), totals, files: [...seen].sort() };
}
