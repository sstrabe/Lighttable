// Copies the logo files the site serves from assets/logo, where their masters are kept
// (rendered by scripts/render-logo.cs), into public/logo. Runs before `npm run dev` and
// `npm run build`; public/logo is not committed.
import { copyFileSync, mkdirSync, rmSync } from "node:fs";

const from = new URL("../../assets/logo/", import.meta.url);
const to = new URL("../public/logo/", import.meta.url);
const files = ["favicon.ico", "mark.svg", "mark-180.png", "mark-512.png", "lockup.svg", "lockup-dark.svg"];

rmSync(to, { recursive: true, force: true });
mkdirSync(to, { recursive: true });
for (const file of files) copyFileSync(new URL(file, from), new URL(file, to));
