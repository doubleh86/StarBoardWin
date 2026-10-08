import { copyFile, mkdir, rm } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const outputDirectory = new URL("./dist/", import.meta.url);

await rm(outputDirectory, { recursive: true, force: true });
await mkdir(outputDirectory, { recursive: true });

await build({
  entryPoints: [fileURLToPath(new URL("./src/index.ts", import.meta.url))],
  bundle: true,
  format: "iife",
  minify: true,
  outfile: fileURLToPath(new URL("./dist/app.js", import.meta.url)),
  platform: "browser",
  sourcemap: false,
  target: ["chrome120"],
});

await copyFile(
  new URL("./src/index.html", import.meta.url),
  new URL("./dist/index.html", import.meta.url),
);
await copyFile(
  new URL("./node_modules/@xterm/xterm/LICENSE", import.meta.url),
  new URL("./dist/xterm-LICENSE.txt", import.meta.url),
);
await copyFile(
  new URL("./node_modules/@xterm/addon-fit/LICENSE", import.meta.url),
  new URL("./dist/xterm-addon-fit-LICENSE.txt", import.meta.url),
);
await copyFile(
  new URL("./node_modules/@xterm/addon-search/LICENSE", import.meta.url),
  new URL("./dist/xterm-addon-search-LICENSE.txt", import.meta.url),
);
await copyFile(
  new URL("./node_modules/@xterm/addon-web-links/LICENSE", import.meta.url),
  new URL("./dist/xterm-addon-web-links-LICENSE.txt", import.meta.url),
);
