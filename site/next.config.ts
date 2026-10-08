import type { NextConfig } from "next";

const nextConfig: NextConfig = {
    // A static site: `next build` writes it to out/, which Cloudflare serves (wrangler.jsonc).
    output: "export",
    images: { unoptimized: true },
};

export default nextConfig;
