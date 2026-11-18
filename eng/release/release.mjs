import assert from "node:assert/strict";
import { command } from "../package/common.mjs";
import { verifyRelease } from "./policy.mjs";

const [action, tagArgument] = process.argv.slice(2);
const tag = tagArgument || process.env.RELEASE_TAG;
assert.ok(
  ["check", "verify-tag", "tag", "push"].includes(action) && process.argv.length <= 4,
  "Usage: release.mjs check|verify-tag|tag|push [v<version>]",
);
assert.ok(tag, "Supply TAG=v<version> (RELEASE_TAG) or an explicit tag argument");
const info = await verifyRelease({
  tag,
  remote: ["verify-tag", "push"].includes(action),
  requireTag: ["verify-tag", "push"].includes(action),
});
if (action === "tag") {
  const result = await command(process.execPath, ["eng/check.mjs"]);
  process.stdout.write(result.stdout);
  process.stderr.write(result.stderr);
  await verifyRelease({ tag });
  await command("git", ["tag", "-a", info.tag, "-m", `Release ${info.version}`]);
} else if (action === "push") {
  await command("git", ["push", "origin", `refs/tags/${info.tag}:refs/tags/${info.tag}`]);
}
console.log(JSON.stringify({ action, ...info }));
