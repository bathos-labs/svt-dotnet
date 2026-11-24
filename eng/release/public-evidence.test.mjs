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
      assert.equal(
        url,
        `https://symbols.nuget.org/download/symbols/bathos.graphics.svgtemplates.pdb/${"A".repeat(32)}FFFFFFFF/bathos.graphics.svgtemplates.pdb`,
      );
      assert.equal(options.headers.SymbolChecksumValidationSupported, "1");
      assert.equal(options.headers.SymbolChecksum, "SHA256:" + "A".repeat(64));
      if (corruption === "unavailable") return new Response("Not indexed", { status: 404 });
      return new Response(corruption === "symbols" ? "wrong PDB" : pdb);
    }
    assert.equal(
      url,
      `https://raw.githubusercontent.com/svt-project/svt-dotnet/${commit}/README.md`,
    );
    return new Response(corruption === "source" ? "wrong source" : source);
  });
  try {
    await json(resolve(folder, "inspection-library.json"), {
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

test("retained public completion requires full consumption, signatures, symbols and tool modes", async () => {
  const { validatePublicCompletion } = await import("./public-evidence.mjs");
  const { smokeChecks, toolChecks } = await import("../package/common.mjs");
  const digest = "a".repeat(64);
  const product = { assembly_sha256: digest, pdb_sha256: digest };
  const manifest = {
    source: { commit: "b".repeat(40), dirty: false },
    sdk: "10.0.403",
    products: { library: product, cli: product },
  };
  const debugging = {
    symbols: "passed",
    source_link: "passed",
    pdb_sha256: digest,
    source_documents: 1,
  };
  const mode = {
    status: "passed",
    assembly_sha256: digest,
    pdb_sha256: digest,
    package_sha256: digest,
    repository_signature: "passed",
    original_contents: "passed",
  };
  const report = {
    format_version: 0,
    status: "passed",
    purpose: "release-candidate",
    distribution: "nuget.org",
    bundle_manifest_sha256: digest,
    source: manifest.source,
    sdk: manifest.sdk,
    products: manifest.products,
    checks: smokeChecks,
    empty_feed_rejected: true,
    published_package_sha256: digest,
    repository_signature: "passed",
    original_contents: "passed",
    debugging,
    tool: {
      status: "passed",
      checks: toolChecks,
      debugging,
      modes: { global: mode, local: mode, "one-off": mode },
    },
  };
  validatePublicCompletion(report, manifest, digest);
  const paths = [
    "format_version",
    "status",
    "purpose",
    "distribution",
    "bundle_manifest_sha256",
    "source.commit",
    "sdk",
    "products.cli.pdb_sha256",
    "checks",
    "empty_feed_rejected",
    "published_package_sha256",
    "repository_signature",
    "original_contents",
    "debugging.symbols",
    "debugging.pdb_sha256",
    "debugging.source_link",
    "debugging.source_documents",
    "tool.status",
    "tool.checks",
    "tool.debugging.symbols",
    "tool.debugging.pdb_sha256",
    "tool.debugging.source_link",
    "tool.debugging.source_documents",
    ...["global", "local", "one-off"].flatMap((name) =>
      [
        "status",
        "assembly_sha256",
        "pdb_sha256",
        "package_sha256",
        "repository_signature",
        "original_contents",
      ].map((key) => `tool.modes.${name}.${key}`),
    ),
  ];
  for (const path of paths) {
    const changed = JSON.parse(JSON.stringify(report));
    const keys = path.split(".");
    const last = keys.pop();
    const parent = keys.reduce((obj, key) => obj[key], changed);
    delete parent[last];
    assert.throws(() => validatePublicCompletion(changed, manifest, digest), path);
  }
});
