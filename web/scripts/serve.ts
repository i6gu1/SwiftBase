import { join, normalize, resolve } from "node:path";

const rootName = process.argv[2] ?? "public";
const root = resolve(import.meta.dir, "..", rootName);
const port = Number(process.env.PORT ?? 3000);

Bun.serve({
  port,
  async fetch(request) {
    const url = new URL(request.url);
    let pathname = decodeURIComponent(url.pathname);

    if (pathname.includes("..")) {
      return new Response("Forbidden", { status: 403 });
    }

    if (pathname === "/") {
      pathname = "/index.html";
    }

    const file = Bun.file(join(root, normalize(pathname)));
    if (await file.exists()) {
      return new Response(file);
    }

    return new Response("Not found", { status: 404 });
  },
});

console.log(`SwiftBase site running at http://localhost:${port}`);
