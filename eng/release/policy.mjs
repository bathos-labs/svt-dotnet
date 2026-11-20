import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { randomUUID } from "node:crypto";
import { command, root } from "../package/common.mjs";

import { releaseVersion, eligibleBranch } from "./version.mjs";

export async function declaredVersion(cwd = root) {
  const props = await readFile(resolve(cwd, "Directory.Build.props"), "utf8");
  const versions = [...props.matchAll(/<Version>([^<]+)<\/Version>/g)];
  assert.equal(versions.length, 1, "Declare exactly one Version in Directory.Build.props");
  return releaseVersion(versions[0][1]);
}

export async function metadata(cwd = root) {
  const version = await declaredVersion(cwd);
  const changelog = await readFile(resolve(cwd, "CHANGELOG.md"), "utf8");
  assert.equal([...changelog.matchAll(/^## \[Unreleased\]$/gm)].length, 1);
  const entries = [
    ...changelog.matchAll(
      /^## \[([^\]]+)\] - (\d{4}-\d{2}-\d{2})\n([\s\S]*?)(?=^## |^\[[^\]]+\]:|$(?![\s\S]))/gm,
    ),
  ];
  assert.equal(entries[0]?.[1], version, "The latest dated changelog entry must match Version");
  assert.equal(entries.filter((entry) => entry[1] === version).length, 1);
  const [, , date, notes] = entries[0];
  assert.equal(new Date(date).toISOString().slice(0, 10), date, "Invalid changelog date");
  assert.ok(date <= new Date().toISOString().slice(0, 10), "Changelog date is in the future");
  assert.match(notes, /^- \S/m, "Release notes must describe the consumer-facing changes");
  assert.ok(
    changelog.includes(
      `[${version}]: https://github.com/bathos-labs/svt-dotnet/releases/tag/v${version}`,
    ),
    "Missing release link",
  );
  return { version, tag: `v${version}` };
}

export async function verifyRelease({ cwd = root, tag, remote = false, requireTag = false } = {}) {
  const git = async (...args) => (await command("git", args, { cwd })).stdout.trim();
  assert.equal(
    await git("status", "--porcelain", "--untracked-files=all"),
    "",
    "Release operations require a clean committed checkout",
  );
  const info = await metadata(cwd);
  if (tag !== undefined) assert.equal(tag, info.tag, "Tag must match the declared version");
  const commit = await git("rev-parse", "HEAD");
  if (requireTag) {
    assert.equal(
      await git("cat-file", "-t", `refs/tags/${info.tag}`),
      "tag",
      "Use an annotated release tag",
    );
    assert.equal(
      await git("rev-parse", `refs/tags/${info.tag}^{commit}`),
      commit,
      "HEAD must be the tagged commit",
    );
  }
  if (!remote) {
    const branch = await git("symbolic-ref", "--quiet", "--short", "HEAD");
    assert.ok(
      eligibleBranch(branch, info.version),
      "Release preparation requires master or a prerelease on release/*",
    );
  } else {
    // A fresh private ref namespace cannot be satisfied by stale or invented origin/* refs.
    const prefix = `refs/svt-release/${randomUUID()}/`;
    try {
      await git("fetch", "--no-tags", "--prune", "origin", `+refs/heads/*:${prefix}*`);
      const refs = (await git("for-each-ref", "--format=%(refname)", prefix))
        .split("\n")
        .filter(Boolean);
      let eligible = false;
      for (const ref of refs.filter((ref) =>
        eligibleBranch(ref.slice(prefix.length), info.version),
      )) {
        try {
          await git("merge-base", "--is-ancestor", commit, ref);
          eligible = true;
        } catch (error) {
          if (error.code !== 1) throw error;
        }
      }
      assert.ok(eligible, "Commit is not on an eligible branch at origin; push the branch first");
    } finally {
      for (const ref of (await git("for-each-ref", "--format=%(refname)", prefix))
        .split("\n")
        .filter(Boolean))
        await git("update-ref", "-d", ref);
    }
  }
  return { ...info, commit };
}
