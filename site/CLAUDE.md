@AGENTS.md

# Lighttable website

The landing page at lighttable.sstrabe.dev: Next.js (App Router), TypeScript, Tailwind v4 and
lucide-react, built as a static export (`output: "export"`, written to `out/`) and served by a
Cloudflare Worker as static assets (`wrangler.jsonc`; the repo's CLAUDE.md has its settings).
It is a static site, so there is no server code, no cookies, no route handlers and no redirects
(see the static exports guide in `node_modules/next/dist/docs/`).

- **Style:** the Heimdall family look (the `heimdall-style` skill). `src/app/globals.css` holds its
  tokens with Lighttable's accent, violet (OKLCH hue 300). Use the semantic colour names
  (`bg-surface`, `text-muted`, `border-line`, `bg-primary`, ...), never raw colours or Tailwind's
  palette. The logo's orange stays in the logo: it is too close to Heimdall's.
- **Theme:** it follows the system setting (`prefers-color-scheme`). There is no switcher, because
  a static site can't render `data-theme` from a cookie.
- **Logo:** the masters are in `../assets/logo`. `npm run dev` and `npm run build` first run
  `scripts/copy-logo.mjs`, which copies the files the site serves into `public/logo` (not
  committed). `src/components/logo.tsx` shows them.
- **Copy:** say what things do, briefly, in sentence case. Facts about the tool (folders, file
  names, timings, darktable modules) come from the code and the README, so check them there when
  the tool changes.
- `public/_headers` sets the response headers (Cloudflare reads it from the assets folder).

```bash
npm install
npm run dev     # http://localhost:3000
npm run build   # static site in out/
npm run lint
npx wrangler dev   # serves out/ as Cloudflare will, with _headers and the 404 page
```
