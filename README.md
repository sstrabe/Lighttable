<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo/lockup-dark.svg">
    <img src="assets/logo/lockup.svg" alt="Lighttable" width="360">
  </picture>
</p>

# PhotoProcessing

Upload a raw file to Nextcloud and get back a finished JPEG, edited by Claude in darktable.

```
cloud.sstrabe.dev  Photos/Processing/     this PC (photoedit watch)
─────────────────────────────────────     ─────────────────────────────────────────────
Inbox/IMG_4899.CR2          ──download──▶  job folder
Inbox/IMG_4899.txt          (optional        │ darktable renders its defaults (v00)
                             instructions)   │ claude -p: look → edit recipe.json →
                                             │            render → look … → finalize
Processed/IMG_4899.jpg         ◀──upload──   │ full-res JPEG
Archive/IMG_4899.CR2             ◀──move──   │ raw +
Archive/IMG_4899.CR2.xmp         ◀──upload── │ darktable sidecar (open the raw in darktable
Archive/IMG_4899.notes.md        ◀──upload── ┘ to refine Claude's edit) + Claude's notes
```

Claude does the editing itself. It looks at each render and adjusts exposure, white
balance, tone mapping, shadows/highlights, colour, local contrast, noise reduction,
sharpening, straightening and crop, then judges the result before exporting. It runs
through the Claude Code CLI on your normal subscription, so there is no API key and no
per-token billing.

## Setup

Requirements: darktable 5.6, Claude Code (`claude` logged in), and the .NET 10 SDK.

1. **Register the app on Heimdall.** Nextcloud access comes from signing in with Heimdall
   (sso.heimdall.sstrabe.dev). On the app's OAuth2 page:
   - Redirect URI: `http://127.0.0.1:38517/callback`. `photoedit login` listens there for the
     redirect back, so it must match exactly.
   - Scopes: `openid profile nextcloud.files.write offline_access`.
   - Make it a public app if Heimdall offers that, so there is no secret to store.

   Put the app's client ID in `src/PhotoProcessing.Cli/appsettings.json` under
   `Heimdall:ClientId`. A confidential app's secret goes in .NET user secrets, outside the repo:
   ```bash
   dotnet user-secrets --project src/PhotoProcessing.Cli set PhotoProcessing:Heimdall:ClientSecret <secret>
   ```
2. **Publish** to `%USERPROFILE%\.photoprocessing\bin`:
   ```bash
   pwsh scripts/publish.ps1
   ```
3. **Sign in.** This opens Heimdall in your browser. Sign in as the Nextcloud account the
   watcher should work in. That account must have signed in to Nextcloud once:
   ```bash
   ~/.photoprocessing/bin/photoedit.exe login
   ```
   If the browser is already signed in to Heimdall as someone else, add `--switch-account`.
   The sign-in is kept in `%USERPROFILE%\.photoprocessing\state\heimdall.json`. Because of
   `offline_access` it outlives your Heimdall browser session. It ends only if it goes 30 days
   unused or you remove the app from your Heimdall account. Then sign in again (or use the tray
   icon's "Sign in to Heimdall…").
4. **Check** that everything is reachable:
   ```bash
   ~/.photoprocessing/bin/photoedit.exe check
   ```
5. **Install the watcher.** This registers a hidden scheduled task that starts at boot and
   runs as you even before you log in. It asks for one UAC approval:
   ```bash
   pwsh scripts/install-watcher.ps1
   ```

6. **Add the tray icon** (optional, no admin). It starts at every logon:
   ```bash
   pwsh scripts/install-tray.ps1
   ```
   Windows 11 puts new icons in the `^` overflow. To keep it visible, drag it onto the taskbar,
   or turn it on under Settings → Personalization → Taskbar → Other system tray icons.

Everything at runtime lives in `%USERPROFILE%\.photoprocessing`, deliberately not under
AppData. Files that a packaged app (such as the Claude desktop app) creates under AppData
are redirected into that app's private storage, where the watcher task can't see them.

The inbox, `Photos/Processing/Inbox`, must already exist. The watcher creates `Processed`,
`Archive` and `Failed` next to it when it needs them. Folder names, poll interval, model and other settings are in
`src/PhotoProcessing.Cli/appsettings.json`. Re-run `publish.ps1` after changing it.

## Use

- Upload raws (CR2, CR3, NEF, ARW, DNG, RAF, …) to `Photos/Processing/Inbox`. A photo is picked up
  within about 1–2 minutes once its upload has settled, and an edit takes about 1–3
  minutes. Photos are processed one at a time.
- **Instructions:** to steer an edit, upload a text file with the same name next to the
  raw, e.g. `IMG_4899.txt` containing `moody, crop to 4:5`. It takes priority over Claude's
  own taste.
- A photo that fails twice is moved to `Photos/Processing/Failed` with an `.error.txt`. Move it back
  to the inbox to retry.
- **Tray icon:**
  - Colours: 🟢 watching, 🔵 spinning while Claude edits (the tooltip shows the current
    step), 🟠 no inbox check recently, 🔴 an error (for example "Sign in to Heimdall needed")
    or the watcher isn't running.
  - A notification appears when a photo is done. Click it, or double-click the icon, to open
    the Nextcloud folder.
  - Right-click for "Check inbox now", the last edit's previews and notes, today's log,
    "Sign in to Heimdall…" and "Restart watcher".
- Logs are in `%USERPROFILE%\.photoprocessing\logs\`. Every job keeps its previews,
  recipe, notes and the full Claude transcript (`claude.jsonl`) in
  `%USERPROFILE%\.photoprocessing\editor\jobs\<job>\`.
- The watcher reads its settings and secrets only at start. After changing either, restart
  it with `Start-ScheduledTask 'PhotoProcessing Watcher'` (or re-run `publish.ps1`). A new
  Heimdall sign-in needs no restart: the watcher picks it up within seconds.

### Without Nextcloud

```bash
photoedit edit path/to/IMG_1234.CR2 --instructions "warmer, keep it natural"
```

### Re-editing a photo together with Claude

The editor workspace is a normal Claude Code project. Open
`%USERPROFILE%\.photoprocessing\editor` in Claude Code and ask, for example, "make
jobs/20261003-121159-IMG_4899 a bit warmer and less cropped, then finalize". The new
`output/` files can then be uploaded by hand.

## Usage limits

Each photo is one headless Claude Code session of roughly 15–25 turns with a few image
reads. The CLI reports an API-equivalent figure of about $0.40 for a typical photo, but on
a subscription that counts against your plan's usage limits rather than being billed. To
make edits lighter, set `Claude:Model` (for example `sonnet`) or `Claude:Effort` in
`appsettings.json`.

## Development

See `CLAUDE.md` for the architecture and the darktable details. Build and test with:

```bash
dotnet test PhotoProcessing.slnx
```
