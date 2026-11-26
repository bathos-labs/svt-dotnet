import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { command, hash, packageId, smokeChecks, toolChecks } from "../package/common.mjs";

async function download(url, headers = {}) {
  const response = await fetch(url, { headers, signal: AbortSignal.timeout(60000) });
  assert.equal(
    response.ok,
    true,
    `Public evidence unavailable (${response.status}): ${url}. Retain the candidate and rerun verification after registry indexing.`,
  );
  return Buffer.from(await response.arrayBuffer());
}

export async function verifyPublicDebugging(bundle, manifest, output, role = "library") {
  const inspection = JSON.parse(await readFile(resolve(bundle, `inspection-${role}.json`)));
  assert.match(inspection.symbol_key, /^[A-F0-9]{32}FFFFFFFF$/);
  assert.match(inspection.symbol_checksum, /^SHA256:[A-F0-9]{64}$/);
  const filename = (role === "library" ? packageId : "svt") + ".pdb";
  const symbolFilename = filename.toLowerCase();
  const pdb = await download(
    `https://symbols.nuget.org/download/symbols/${symbolFilename}/${inspection.symbol_key}/${symbolFilename}`,
    {
      SymbolChecksum: inspection.symbol_checksum,
      SymbolChecksumValidationSupported: "1",
    },
  );
  assert.equal(
    hash(pdb),
    inspection.pdb_sha256,
    "Public symbols differ from the validated symbols",
  );
  await writeFile(resolve(output, filename), pdb);
  const tracked = new Set(
    (await command("git", ["ls-tree", "-r", "--name-only", manifest.source.commit])).stdout
      .trim()
      .split("\n"),
  );
  const documents = inspection.source_documents
    .map((name) => name.slice("/_/".length))
    .filter((name) => tracked.has(name));
  assert.ok(documents.length > 0, "No repository source documents to verify");
  for (const name of documents) {
    const bytes = await download(
      `https://raw.githubusercontent.com/svt-project/svt-dotnet/${manifest.source.commit}/${name.split("/").map(encodeURIComponent).join("/")}`,
    );
    const expected = await command("git", ["show", `${manifest.source.commit}:${name}`], {
      encoding: "buffer",
    });
    assert.equal(hash(bytes), hash(expected.stdout), `Public Source Link content differs: ${name}`);
  }
  return {
    symbols: "passed",
    pdb_sha256: hash(pdb),
    source_link: "passed",
    source_documents: documents.length,
  };
}

// Verify the on-demand report against the exact release candidate.
export function validatePublicCompletion(report, manifest, manifestHash) {
  assert.equal(report.format_version, 0);
  assert.equal(report.status, "passed");
  assert.equal(report.purpose, "release-candidate");
  assert.equal(report.distribution, "nuget.org");
  assert.equal(report.bundle_manifest_sha256, manifestHash);
  assert.deepEqual(report.source, manifest.source);
  assert.equal(report.sdk, manifest.sdk);
  assert.deepEqual(report.products, manifest.products);
  assert.deepEqual(report.checks, smokeChecks);
  assert.equal(report.empty_feed_rejected, true);
  const signed = (item, hashField) => {
    assert.equal(item.repository_signature, "passed");
    assert.equal(item.original_contents, "passed");
    assert.match(item[hashField], /^[a-f0-9]{64}$/);
  };
  const debugging = (item, product) => {
    assert.equal(item.symbols, "passed");
    assert.equal(item.source_link, "passed");
    assert.equal(item.pdb_sha256, product.pdb_sha256);
    assert.ok(Number.isSafeInteger(item.source_documents) && item.source_documents > 0);
  };
  signed(report, "published_package_sha256");
  debugging(report.debugging, manifest.products.library);
  assert.equal(report.tool.status, "passed");
  assert.deepEqual(report.tool.checks, toolChecks);
  debugging(report.tool.debugging, manifest.products.cli);
  for (const mode of ["global", "local", "one-off"]) {
    const item = report.tool.modes[mode];
    assert.equal(item.status, "passed");
    assert.equal(item.assembly_sha256, manifest.products.cli.assembly_sha256);
    assert.equal(item.pdb_sha256, manifest.products.cli.pdb_sha256);
    signed(item, "package_sha256");
  }
}
