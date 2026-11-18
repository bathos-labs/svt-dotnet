import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve } from "node:path";
import { command, json, hash } from "../package/common.mjs";
import { verifyPublicDebugging } from "./public-evidence.mjs";

test("public debugging verifies the exact PDB and mapped repository source, rejecting unavailable or changed bytes", async (t) => {
  const folder = await mkdtemp(resolve(tmpdir(), "svt-public-evidence-"));
  const commit = (await command("git", ["rev-parse", "HEAD"])).stdout.trim();
  const source = (await command("git", ["show", `${commit}:README.md`], { encoding: "buffer" }))
    .stdout;
  const pdb = Buffer.from("symbol fixture");
  let corruption;
  const requests = [];
  t.mock.method(globalThis, "fetch", async (url, options) => {
    requests.push(url);
    if (url.startsWith("https://symbols.nuget.org/")) {
      assert.equal(options.headers.SymbolChecksumValidationSupported, "1");
      assert.equal(options.headers.SymbolChecksum, "SHA256:" + "A".repeat(64));
      if (corruption === "unavailable") return new Response("Not indexed", { status: 404 });
      return new Response(corruption === "symbols" ? "wrong PDB" : pdb);
    }
    assert.equal(
      url,
      `https://raw.githubusercontent.com/bathos-labs/svt-dotnet/${commit}/README.md`,
    );
    return new Response(corruption === "source" ? "wrong source" : source);
  });
  try {
    await json(resolve(folder, "inspection.json"), {
      symbol_key: "A".repeat(32) + "FFFFFFFF",
      symbol_checksum: "SHA256:" + "A".repeat(64),
      pdb_sha256: hash(pdb),
      source_documents: ["/_/README.md", "/_/obj/Generated.cs"],
    });
    const manifest = { source: { commit } };
    const evidence = await verifyPublicDebugging(folder, manifest, folder);
    assert.equal(evidence.source_documents, 1);
    assert.equal(evidence.source_link, "passed");
    assert.equal(evidence.pdb_sha256, hash(pdb));
    assert.equal(requests.length, 2);
    for (const value of ["symbols", "source", "unavailable"]) {
      corruption = value;
      await assert.rejects(verifyPublicDebugging(folder, manifest, folder));
    }
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
});
