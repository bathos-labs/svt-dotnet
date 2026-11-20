import { resolve } from "node:path";
import { command, root, nonce } from "../package/common.mjs";

// Pack is required: a successful build alone does not run the SDK's package baseline validator.
// Keep each diagnostic package separate. PackageBaseline.props forces fresh SDK validation.
const output = resolve(root, "artifacts/compatibility", nonce());
const result = await command("dotnet", [
  "pack",
  "src/Bathos.Graphics.SvgTemplates/Bathos.Graphics.SvgTemplates.csproj",
  "-c",
  "Release",
  "--no-build",
  "--no-restore",
  "-o",
  output,
]);
process.stdout.write(result.stdout);
process.stderr.write(result.stderr);
