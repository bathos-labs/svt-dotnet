import { mkdir, copyFile } from "node:fs/promises";
import { resolve } from "node:path";
import assert from "node:assert/strict";
import puppeteer from "puppeteer";
import { tools, pins, download, fileHash, command } from "./common.mjs";

const profile = pins.resvg[`${process.platform}-${process.arch}`];
assert.ok(profile, `Unsupported native validation platform: ${process.platform}-${process.arch}`);
await mkdir(tools, { recursive: true });
const archive = resolve(tools, process.platform === "darwin" ? "resvg.zip" : "resvg.tar.gz");
await download(profile.url, archive, profile.sha256);
await command(
  process.platform === "darwin" ? "unzip" : "tar",
  process.platform === "darwin" ? ["-o", archive, "-d", tools] : ["-xzf", archive, "-C", tools],
);
assert.equal(
  (await command(resolve(tools, "resvg"), ["--version"])).stdout.trim(),
  pins.resvg_version,
);
const fontArchive = resolve(tools, "dejavu.tar.bz2");
await download(pins.font.url, fontArchive, pins.font.sha256);
await command("tar", ["-xjf", fontArchive, "-C", tools]);
await copyFile(
  resolve(tools, "dejavu-fonts-ttf-2.37/ttf/DejaVuSans.ttf"),
  resolve(tools, "DejaVuSans.ttf"),
);
await copyFile(resolve(tools, "dejavu-fonts-ttf-2.37/LICENSE"), resolve(tools, "DejaVu-LICENSE"));
assert.equal(await fileHash(resolve(tools, "DejaVuSans.ttf")), pins.font.file_sha256);
assert.ok(
  (await command(await puppeteer.executablePath(), ["--version"])).stdout.includes(pins.chrome),
  "Pinned Chrome missing; run the pinned Puppeteer installer.",
);
console.log(
  `Verified resvg ${pins.resvg_version}, Chrome ${pins.chrome}, DejaVu Sans ${pins.font.version}`,
);
