import test from "node:test";
import assert from "node:assert/strict";
import { cp, mkdtemp, mkdir, readFile, writeFile, rm, utimes } from "node:fs/promises";
import { tmpdir } from "node:os";
import { resolve, basename } from "node:path";
import { command, root } from "../package/common.mjs";

// Exercise the real project/analyzer/SDK wiring in an isolated copy. Never mutate the checkout
// or use a failed compile as evidence of an API diagnostic.
test("compatibility enforcement rejects contract changes and accepts reviewed additions", async (t) => {
  const work = await mkdtemp(resolve(tmpdir(), "svt-compatibility-"));
  const project = "src/Bathos.Graphics.SvgTemplates/Bathos.Graphics.SvgTemplates.csproj";
  const folder = "src/Bathos.Graphics.SvgTemplates";
  const cliFolder = "src/Bathos.Graphics.SvgTemplates.Cli";
  const cliProject = `${cliFolder}/Bathos.Graphics.SvgTemplates.Cli.csproj`;
  let packAttempt = 0;
  const run = (args) => command("dotnet", args, { cwd: work });
  const build = (properties = []) =>
    run([
      "build",
      project,
      "-c",
      "Release",
      "--no-restore",
      "--no-incremental",
      "-p:EnableSourceControlManagerQueries=false",
      "-p:EnableSourceLink=false",
      ...properties,
    ]);
  const pack = (properties = []) =>
    run([
      "pack",
      project,
      "-c",
      "Release",
      "--no-build",
      "--no-restore",
      "-o",
      resolve(work, `packages-${packAttempt++}`),
      ...properties,
    ]);
  async function rejects(action, diagnostic) {
    await assert.rejects(action, (error) => {
      const output = (error.stdout ?? "") + (error.stderr ?? "");
      assert.match(output, diagnostic);
      assert.doesNotMatch(output, /error CS\d+/);
      return true;
    });
  }
  async function change(name, transform, action) {
    const path = resolve(work, name);
    const original = await readFile(path, "utf8");
    const changed = transform(original);
    assert.notEqual(changed, original, `Mutation did not apply: ${name}`);
    await writeFile(path, changed);
    try {
      await action();
    } finally {
      await writeFile(path, original);
    }
  }
  try {
    for (const name of [
      "Directory.Build.props",
      "Directory.Packages.props",
      "NuGet.Config",
      "global.json",
      ".editorconfig",
      "LICENSE",
      "NOTICE",
      "docs/package-readme.md",
      "docs/tool-readme.md",
      "eng/compatibility/PackageBaseline.props",
    ]) {
      const path = resolve(work, name);
      await mkdir(resolve(path, ".."), { recursive: true });
      await cp(resolve(root, name), path);
    }
    await cp(resolve(root, folder), resolve(work, folder), {
      recursive: true,
      filter: (path) => !["bin", "obj"].includes(basename(path)),
    });
    await cp(resolve(root, cliFolder), resolve(work, cliFolder), {
      recursive: true,
      filter: (path) => !["bin", "obj"].includes(basename(path)),
    });
    await run(["restore", cliProject, "--locked-mode"]);
    const properties = JSON.parse(
      (
        await run([
          "msbuild",
          project,
          "-p:Configuration=Release",
          "-getProperty:_ApiCompatValidatePackageSemaphoreFile,NuGetPackageRoot,PackageValidationBaselineName,PackageValidationBaselineVersion",
        ])
      ).stdout,
    ).Properties;

    await t.test("unchanged RC surface and exact published package pass", async () => {
      await build();
      await pack();
    });
    await t.test("removed members fail source and package checks independently", async () => {
      await change(
        `${folder}/BindingResult.cs`,
        (s) => s.replace("public byte[] ToUtf8Bytes()", "internal byte[] ToUtf8Bytes()"),
        async () => {
          await rejects(() => build(), /error RS0017/);
          // Bypass only the source analyzer to prove the independent binary package comparison.
          await build(["-p:RunAnalyzers=false"]);
          // A cached successful verdict must not hide the broken assembly. Use a deterministic
          // future stamp rather than sleeping or depending on filesystem timestamp resolution.
          const stamp = resolve(
            work,
            folder,
            properties._ApiCompatValidatePackageSemaphoreFile.replaceAll("\\", "/"),
          );
          const future = new Date("2100-01-01T00:00:00Z");
          await utimes(stamp, future, future);
          await rejects(() => pack(), /error CP0002/);
        },
      );
    });
    await t.test("renamed parameters fail source and package checks independently", async () => {
      await change(
        `${folder}/TemplateBinding.cs`,
        (s) => s.replaceAll(/\bkey\b/g, "renamedKey"),
        async () => {
          await rejects(() => build(), /error RS001[67]/);
          await build(["-p:RunAnalyzers=false"]);
          await rejects(() => pack(), /error CP0017/);
        },
      );
    });
    await t.test("nullable return changes fail the source API contract", async () => {
      await change(
        `${folder}/TemplateBinding.cs`,
        (s) =>
          s.replace("public static TemplateBinding Text", "public static TemplateBinding? Text"),
        async () => {
          await rejects(() => build(), /error RS001[67]/);
        },
      );
    });
    await t.test("enum value changes fail the source API contract", async () => {
      await change(
        `${folder}/InspectionModel.cs`,
        (s) => s.replace("Text,", "Text = 99,"),
        async () => {
          await rejects(() => build(), /error RS001[67]/);
        },
      );
    });
    await t.test("optional parameter defaults remain part of the source API contract", async () => {
      await change(
        `${folder}/TemplateInspector.cs`,
        (s) => s.replace("InspectionOptions? options = null", "InspectionOptions? options"),
        async () => {
          await rejects(() => build(), /error RS001[67]/);
        },
      );
    });
    await t.test("missing API files fail instead of silently disabling analysis", async () => {
      const path = resolve(work, folder, "PublicAPI.Shipped.txt");
      const bytes = await readFile(path);
      await rm(path);
      try {
        await rejects(() => build(), /error RS0048/);
      } finally {
        await writeFile(path, bytes);
      }
    });
    await t.test(
      "a new API needs explicit review but does not break the package baseline",
      async () => {
        await change(
          `${folder}/BindingResult.cs`,
          (s) =>
            s.replace(
              "public int Utf8ByteCount { get; }",
              "public int Utf8ByteCount { get; }\n    /// <summary>Fixture addition.</summary>\n    public int AdditionalByteCount => Utf8ByteCount;",
            ),
          async () => {
            await rejects(() => build(), /error RS0016/);
            await change(
              `${folder}/PublicAPI.Unshipped.txt`,
              (s) => s + "Bathos.Graphics.SvgTemplates.BoundSvg.AdditionalByteCount.get -> int\n",
              async () => {
                await build();
                await pack();
              },
            );
          },
        );
      },
    );
    await t.test("an incorrect baseline digest fails closed", async () => {
      await build();
      await rejects(
        () => pack(["-p:SvtPackageBaselineSha256=" + "0".repeat(64)]),
        /baseline package differs/,
      );
    });
    await t.test("a replacement baseline path cannot bypass the verified download", async () => {
      await rejects(
        () => pack(["-p:PackageValidationBaselinePath=unreviewed.nupkg"]),
        /not a replacement baseline path/,
      );
    });
    await t.test(
      "missing or altered baseline bytes fail in an isolated package cache",
      async () => {
        const cache = resolve(work, "baseline cache");
        const id = properties.PackageValidationBaselineName.toLowerCase();
        const version = properties.PackageValidationBaselineVersion;
        const relativePath = `${id}/${version}/${id}.${version}.nupkg`;
        const baseline = resolve(cache, relativePath);
        // Also exercise a cache path containing spaces, without a trailing separator.
        const args = [`-p:NuGetPackageRoot=${cache}`];
        await rejects(() => pack(args), /baseline package is missing/);
        await mkdir(resolve(baseline, ".."), { recursive: true });
        const bytes = await readFile(resolve(properties.NuGetPackageRoot, relativePath));
        await writeFile(baseline, bytes);
        await pack(args);
        bytes[0] ^= 1;
        await writeFile(baseline, bytes);
        await rejects(() => pack(args), /baseline package differs/);
      },
    );
    for (const property of [
      "EnablePackageValidation=false",
      "DisablePackageBaselineValidation=true",
      "RunApiCompat=false",
      "ApiCompatEnableRuleCannotChangeParameterName=false",
    ]) {
      await t.test(`packing rejects disabled enforcement: ${property}`, async () => {
        await rejects(() => pack([`-p:${property}`]), /validation disable flags/);
      });
    }
    await t.test("the CLI cannot accidentally expose a public assembly API", async () => {
      const cliBuild = () =>
        run([
          "build",
          cliProject,
          "-c",
          "Release",
          "--no-restore",
          "--no-incremental",
          "-p:EnableSourceControlManagerQueries=false",
          "-p:EnableSourceLink=false",
        ]);
      await cliBuild();
      await change(
        `${cliFolder}/Program.cs`,
        (s) =>
          s.replace(
            "internal static class Program",
            "/// <summary>Fixture API exposure.</summary>\npublic static class Program",
          ),
        async () => {
          await rejects(cliBuild, /error RS0016/);
        },
      );
    });
    await t.test("removing the portable framework fails package validation", async () => {
      // Same runtime references, but platform-specific packaging no longer serves net10.0 callers.
      const target = "-p:TargetFramework=net10.0-windows";
      await run(["restore", project, target, "-p:RestoreLockedMode=false"]);
      await build([target]);
      await rejects(() => pack([target]), /error PKV006/);
    });
  } finally {
    await rm(work, { recursive: true, force: true });
  }
});
