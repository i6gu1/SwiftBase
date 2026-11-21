# SwiftBase download site

Static marketing/download site for SwiftBase, built with Bun and deployed to Vercel.

## Local development

```bash
bun run dev
```

Open <http://localhost:3000>.

## Build

```bash
bun run build
```

The static output is written to `web/dist`. Vercel uses the same command via `vercel.json`.

## Deploy with Vercel CLI

```bash
vercel login
vercel --prod
```

The installer served at `/downloads/SwiftBase-Setup.exe` is copied from `artifacts/installer/SwiftBase-Setup.exe` before deployment.
