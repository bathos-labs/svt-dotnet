import { fileURLToPath } from "node:url";
import { verifySnapshot } from "./snapshot.mjs";

const snapshot = await verifySnapshot(
  fileURLToPath(new URL("../../conformance/", import.meta.url)),
);
console.log(
  `Verified ${Object.keys(snapshot.files).length} embedded reference files at ${snapshot.source_commit}`,
);
