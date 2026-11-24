import assert from "node:assert/strict";
import { cp, mkdtemp, rm, writeFile, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { verifySnapshot } from "./snapshot.mjs";

async function fixture(action) {
  const work = await mkdtemp(join(tmpdir(), "svt-snapshot-"));
  try {
    await cp(fileURLToPath(new URL("../../conformance/", import.meta.url)), work, {
      recursive: true,
    });
    await action(work);
  } finally {
    await rm(work, { recursive: true, force: true });
  }
}

test("accepted reference snapshot verifies without its source repository", () =>
  fixture(async (work) => {
    assert.ok((await verifySnapshot(work)).source_commit);
  }));
test("changed, missing and additional reference files fail verification", async () => {
  for (const action of [
    (work) => writeFile(join(work, "interpretation/cases.json"), "{}"),
    (work) => rm(join(work, "interpretation/cases.json")),
    (work) => writeFile(join(work, "interpretation/unaccepted.json"), "{}"),
  ])
    await fixture(async (work) => {
      await action(work);
      await assert.rejects(verifySnapshot(work), /differ from the accepted inventory/);
    });
});
test("invalid source identity or digest cannot attest a snapshot", async () => {
  for (const change of [
    (s) => {
      s.specification_version = "latest";
    },
    (s) => {
      s.specification_version = "1.1.0";
    },
    (s) => {
      s.template_format = "0";
    },
    (s) => {
      s.source_commit = "master";
    },
    (s) => {
      s.sha256 = "0".repeat(64);
    },
    (s) => {
      s.files["../outside"] = "0".repeat(64);
    },
  ])
    await fixture(async (work) => {
      const path = join(work, "snapshot.json");
      const snapshot = JSON.parse(await readFile(path, "utf8"));
      change(snapshot);
      await writeFile(path, JSON.stringify(snapshot));
      await assert.rejects(verifySnapshot(work));
    });
});
