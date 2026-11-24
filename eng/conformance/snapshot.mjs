import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readdir, readFile } from "node:fs/promises";
import { resolve } from "node:path";

const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");

export async function verifySnapshot(directory) {
  const snapshot = JSON.parse(await readFile(resolve(directory, "snapshot.json"), "utf8"));
  assert.equal(snapshot.manifest_version, 1);
  assert.equal(snapshot.source_repository, "https://github.com/svt-project/svt-spec");
  assert.match(snapshot.source_commit, /^[a-f0-9]{40}$/);
  assert.deepEqual(snapshot.exclusions, [], "This snapshot accepts the complete reference corpus");
  assert.equal(snapshot.license, "LICENSE");
  const files = { LICENSE: hash(await readFile(resolve(directory, "LICENSE"))) };
  async function walk(relative) {
    const entries = await readdir(resolve(directory, relative), { withFileTypes: true });
    assert.ok(entries.length, `Empty reference directory: ${relative}`);
    for (const entry of entries) {
      const name = `${relative}/${entry.name}`;
      if (entry.isDirectory()) await walk(name);
      else {
        assert.ok(entry.isFile(), `Reference must be a regular file: ${name}`);
        files[name] = hash(await readFile(resolve(directory, name)));
      }
    }
  }
  for (const name of ["inspection", "interpretation", "substitution"]) await walk(name);
  const sorted = Object.fromEntries(
    Object.entries(files).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)),
  );
  assert.deepEqual(
    sorted,
    snapshot.files,
    "Embedded reference files differ from the accepted inventory",
  );
  const digest = hash(
    Object.entries(sorted)
      .map(([name, sha256]) => `${name}\0${sha256}\n`)
      .join(""),
  );
  assert.equal(
    digest,
    snapshot.sha256,
    "Embedded reference digest differs from the accepted snapshot",
  );
  return snapshot;
}
