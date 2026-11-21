import { copyFile, mkdir, readdir, rm } from "node:fs/promises";
import { join, relative, resolve } from "node:path";

const root = resolve(import.meta.dir, "..");
const source = join(root, "public");
const output = join(root, "dist");

async function copyDirectory(from: string, to: string): Promise<void> {
  await mkdir(to, { recursive: true });
  const entries = await readdir(from, { withFileTypes: true });

  for (const entry of entries) {
    const fromPath = join(from, entry.name);
    const toPath = join(to, entry.name);

    if (entry.isDirectory()) {
      await copyDirectory(fromPath, toPath);
    } else if (entry.isFile()) {
      await copyFile(fromPath, toPath);
    }
  }
}

await rm(output, { recursive: true, force: true });
await copyDirectory(source, output);
console.log(`Built ${relative(root, output)}`);
