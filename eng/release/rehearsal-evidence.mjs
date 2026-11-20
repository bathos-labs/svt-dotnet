import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { command, tool, treeHashes, fileHash } from "../package/common.mjs";
import { simulatePublication } from "./simulation.mjs";
import { publishRelease, publicationOrder } from "./publication.mjs";

// Runs the same state machine on actual retained bytes with an in-memory remote only.
export async function rehearseArchive(content, retained, bundle, manifest, manifestHash) {
  const inventory = await treeHashes(content);
  const archiveFile = resolve(retained, "candidate.zip");
  await command("dotnet", [tool, "archive", content, archiveFile]);
  await command("dotnet", [tool, "extract", archiveFile, resolve(retained, "restored")]);
  assert.deepEqual(
    await treeHashes(resolve(retained, "restored")),
    inventory,
    "Archive round trip changed candidate/evidence bytes",
  );
  // A synthetic identity is used only by the in-memory transport. Review candidates cannot
  // enter the production publisher, and this command never calls it or creates a real tag.
  const identity = {
    format_version: 0,
    repository: "bathos-labs/svt-dotnet",
    tag: "v0.0.0-rehearsal",
    commit: manifest.source.commit,
    manifest_sha256: manifestHash,
    archive_sha256: await fileHash(archiveFile),
    validation_run: "1",
    files: inventory,
  };
  const archive = await readFile(archiveFile);
  const artifacts = [];
  for (const key of publicationOrder) {
    const [role, kind] = key.split("-");
    const product = manifest.products[role];
    const artifact = kind === "symbols" ? product.symbols : product;
    artifacts.push({
      key,
      sha256: artifact.sha256,
      bytes: await readFile(resolve(bundle, artifact.file)),
    });
  }
  let interruptions = 0;
  for (const stop of [
    "archive:candidate-identity.json",
    "archive:candidate.zip",
    "archive:sealed",
    ...publicationOrder.map((key) => "upload:" + key),
    "verification:public",
    "verification:retained",
    "release:published",
  ]) {
    const { input, state } = simulatePublication(identity, archive, artifacts);
    await assert.rejects(
      publishRelease({
        ...input,
        boundary: async (point) => {
          if (point === stop) throw new Error("rehearsal interruption");
        },
      }),
      /rehearsal interruption/,
    );
    await publishRelease(input);
    assert.equal(state.published, true);
    assert.deepEqual(state.uploads, publicationOrder);
    interruptions++;
  }
  return {
    status: "passed",
    path: archiveFile,
    sha256: identity.archive_sha256,
    files: Object.keys(inventory).length,
    restored_inventory: "identical",
    actual_artifact_recovery_scenarios: interruptions,
  };
}
