import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { command, hash, packageId } from "../package/common.mjs";

async function download(url, headers = {}) {
  const response = await fetch(url, { headers, signal: AbortSignal.timeout(60000) });
  assert.equal(
    response.ok,
    true,
    `Public evidence unavailable (${response.status}): ${url}. Retain the candidate and rerun verification after registry indexing.`,
  );
  return Buffer.from(await response.arrayBuffer());
}

export async function verifyPublicDebugging(bundle, manifest, output) {
  const inspection = JSON.parse(await readFile(resolve(bundle, "inspection.json")));
  assert.match(inspection.symbol_key, /^[A-F0-9]{32}FFFFFFFF$/);
  assert.match(inspection.symbol_checksum, /^SHA256:[A-F0-9]{64}$/);
  const filename = packageId + ".pdb";
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
      `https://raw.githubusercontent.com/bathos-labs/svt-dotnet/${manifest.source.commit}/${name.split("/").map(encodeURIComponent).join("/")}`,
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
