import assert from "node:assert/strict";
import { mkdir, cp, readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { command, tool, fileHash, json, treeHashes } from "../package/common.mjs";
import { validateIdentity, identityHash } from "./publication.mjs";

assert.equal(process.argv.length, 5, "Usage: archive.mjs <transfer> <native-root> <destination>");
const [transfer, native, destination] = process.argv.slice(2).map((p) => resolve(p));
await command(process.execPath, ["eng/release/verify-candidate.mjs", transfer, native]);
const manifest = JSON.parse(await readFile(resolve(transfer, "bundle/manifest.json")));
await mkdir(destination); // No candidate replacement.
const content = resolve(destination, "content");
await cp(transfer, resolve(content, "transfer"), { recursive: true });
await cp(native, resolve(content, "native"), { recursive: true });
const inventory = await treeHashes(content);
await command("dotnet", [tool, "archive", content, resolve(destination, "candidate.zip")]);
const identity = {
  format_version: 0,
  repository: "bathos-labs/svt-dotnet",
  tag: manifest.release_tag,
  commit: manifest.source.commit,
  manifest_sha256: await fileHash(resolve(transfer, "bundle/manifest.json")),
  archive_sha256: await fileHash(resolve(destination, "candidate.zip")),
  validation_run: process.env.GITHUB_RUN_ID,
  files: inventory,
};
validateIdentity(identity);
await json(resolve(destination, "candidate-identity.json"), identity);
console.log(`Recovery identity: ${identityHash(identity)}`);
