# PhotoProcessing

This tool develops raws with darktable, with Claude doing the edits. A raw uploaded to
Nextcloud (`Photos/Processing/Inbox` on cloud.strabix.com) is downloaded. Headless Claude
Code then edits it by iterating on a JSON recipe that is compiled into darktable history.
The JPEG goes back to `Photos/Processing/Processed`, and the raw goes to
`Photos/Processing/Archive` with a darktable `.xmp` sidecar and Claude's notes.

This file is for **developing** the tool. The prompt for the Claude that *edits photos* is
`editor/CLAUDE.md`. It is copied to the runtime workspace and is not loaded here.

## Stack and constraints

- C# / .NET 10, Windows only. darktable 5.6.1 is installed per-user at
  `%LOCALAPPDATA%\Programs\darktable` and is on PATH.
- **No Python, anywhere.** Use C# for code and PowerShell for scripts.
- Claude is invoked only through the `claude` CLI (`claude -p`, the user's subscription).
  **Never** use the Anthropic API or SDK, and never require an API key. `--bare` is out
  because it forces API-key auth.

## Layout

- `src/PhotoProcessing.Core`: everything testable.
  - `Darktable/`: XMP history read/write (`DarktableXmp`), params blob codec, and
    `Modules.cs`, the binary layouts of each edited module, transcribed from darktable's
    `src/iop/*.c` structs. Also `DarktableCli`, which runs darktable-cli against a private
    config dir.
  - `Editing/`: `Recipe` (the JSON Claude edits, in GUI units), `RecipeCompiler`
    (recipe + baseline history → history), and `EditJob` (job folder lifecycle:
    init/render/zoom/finalize).
  - `Color/WhiteBalance.cs`: xy ↔ temperature/tint (Krystek locus, Duv).
  - `Imaging/`: preview statistics (SkiaSharp) and EXIF (MetadataExtractor).
  - `Claude/ClaudeEditor.cs`: spawns `claude -p` with a locked-down tool allowlist.
  - `Heimdall/`: `HeimdallClient` (the OIDC calls: discovery, authorization URL, code
    exchange, refresh, userinfo) and `HeimdallSession` (the stored sign-in, access tokens,
    and `photoedit login`'s loopback flow).
  - `Nextcloud/NextcloudClient.cs`: a minimal WebDAV client that sends Heimdall bearer tokens.
  - `Net/HttpHandlers.cs`: the shared handler that works around dead IPv6 routes.
- `src/PhotoProcessing.Cli`: the single `photoedit.exe`. It provides the editing
  subcommands Claude calls, `edit` (one photo end to end), `login` (Heimdall sign-in),
  `watch` (the hosted `InboxWatcher`), and `check`.
- `src/PhotoProcessing.Tray`: `photoedit-tray.exe`, a WinForms notification-area icon. The
  watcher runs in session 0 and can't show UI, so it publishes `<Home>/state/status.json`
  (`Core/Status/WatcherStatus.cs`). The tray polls that file every 2 s. "Check inbox now"
  drops a `state/poll-now` file, which the watcher's wait loop picks up. "Sign in to
  Heimdall…" runs `photoedit login` hidden and shows the result as a balloon. The tray also
  reads its settings from the shared `appsettings.json`, because both apps publish into the
  same bin folder. It starts from the `PhotoProcessing Tray` logon task
  (`scripts/install-tray.ps1`).
- `editor/`: the template for the photo-editing Claude workspace (`CLAUDE.md` and
  `.claude/settings.json`). It is copied into the build output and synced to
  `%USERPROFILE%\.photoprocessing\editor` before every edit.
- `assets/logo/`: the Lighttable logo. The SVGs are the masters: `mark.svg` (24 px and up),
  `mark-small.svg` (pixel-fitted for 16–24 px), and `lockup.svg` / `lockup-dark.svg` (mark plus
  the hand-drawn wordmark). `dotnet run scripts/render-logo.cs` renders the committed PNGs and
  `.ico` files from them. `lighttable.ico` is both exes' `ApplicationIcon`, and Core embeds
  `mark.svg` for the `photoedit login` landing page. The tray's notification-area icon is
  separate: `TrayIcons` draws a status-coloured lens at runtime.
- `tests/`: xUnit tests. `Fixtures/baseline-IMG_4899.xmp` is real darktable 5.6.1 output.
- `scripts/`: `publish.ps1` (both apps), `install-watcher.ps1`, `uninstall-watcher.ps1` and `install-tray.ps1` (`-Uninstall` removes it). The
  watcher is a scheduled task with boot and logon triggers, using S4U logon ("run whether
  logged on or not", no stored password). It runs in session 0 with no window. Claude's
  login (`~/.claude/.credentials.json`), the Heimdall sign-in (`state/heimdall.json`) and
  the user secrets are plain files, so S4U can read them.

## The runtime home must not be under AppData

The runtime home is `%USERPROFILE%\.photoprocessing`. The Claude desktop app is an MSIX
package, and Windows redirects files that its child processes (including your shell) create
under `%LOCALAPPDATA%` / `%APPDATA%` into
`%LOCALAPPDATA%\Packages\Claude_*\LocalCache\…`. They look normal from inside the app, but
Task Scheduler, Explorer and everything else don't see them. Registry writes to
`HKCU\Environment` are not redirected. To see the real, unredirected view from inside the
app, run the check as a scheduled task.

## Commands

```bash
dotnet build PhotoProcessing.slnx
dotnet test PhotoProcessing.slnx
```

To try changes without touching the real runtime folder, point `Home` at a scratch dir.
`samples/` holds test raws and is gitignored:

```bash
export PHOTOPROC_PhotoProcessing__Home="$(cygpath -w "$PWD/.scratch/home")"
./src/PhotoProcessing.Cli/bin/Debug/net10.0/photoedit.exe new samples/IMG_4899.CR2   # baseline only
./src/PhotoProcessing.Cli/bin/Debug/net10.0/photoedit.exe edit samples/IMG_4899.CR2  # full Claude edit (~1 min, uses Claude usage)
```

Config comes from `appsettings.json`, then user secrets (id `photoprocessing-photoedit`),
then `PHOTOPROC_`-prefixed env vars (`PHOTOPROC_PhotoProcessing__Nextcloud__InboxFolder=…`).

## Nextcloud access: Heimdall sign-in

Nextcloud WebDAV is authorized with access tokens from Heimdall
(`sso.heimdall.strabix.com/realms/heimdall`, Keycloak), not app passwords. The scopes are
`openid profile nextcloud.files.write offline_access`. The WebDAV path uses the
`preferred_username` from userinfo.

- **Signing in needs a browser, and the watcher has none** (session 0). `photoedit login`
  runs the authorization code flow with PKCE. Heimdall redirects back to a fixed loopback URI
  (`Heimdall:RedirectUri`, registered on the app's OAuth2 page), where an `HttpListener`
  receives the redirect. A non-admin process can bind `http://127.0.0.1:<port>/`. Keep the
  port below 49152: Hyper-V/WSL reserve shifting ranges above that (`netsh int ipv4 show
  excludedportrange protocol=tcp`). The page sent back needs a `Content-Length`. A chunked
  response followed by the process exiting resets the connection, and the browser shows an
  error.
- **The sign-in file is the source of truth.** Keycloak rotates the refresh token on every
  refresh, and several processes refresh: the watcher, `check`, `login`. Each refresh re-reads
  `state/heimdall.json` and writes the new token back, under `state/heimdall.lock`. A cached
  access token is dropped when the file changes, so a new login, possibly as someone else,
  takes effect at once. The watcher rebuilds its client when the signed-in username changes.
- **What breaks a sign-in:** `invalid_grant` on refresh, meaning the app was removed from
  the account, or the offline session went 30 days unused. It surfaces as
  `HeimdallException { SignInRequired: true }`. The watcher backs off 15 minutes, and
  `login` cuts that short with `state/poll-now`.
- Access tokens last 5 minutes. Every request gets one with at least 2 minutes left, because
  the server may check it only once an upload's body has arrived.
- Test the login flow without Heimdall: `HeimdallTests` fakes the server and uses a real
  loopback request as the browser.

## How the darktable side works (and its traps)

- **Baseline:** render once with no sidecar. darktable auto-applies its scene-referred
  defaults, and with `metadata_flags=21` (EXIF + develop history) it embeds the resulting
  history in the JPEG's XMP. `EditJob.InitAsync` extracts that as `baseline.xmp`.
- **Edits:** exposure, sigmoid and channelmixerrgb are patched in place. Every other module
  is appended as a new history entry with default params plus the recipe values. The
  sidecar is passed as darktable-cli's `XMP_FILE` argument.
- **Module versions are hard-coded.** A darktable upgrade can bump a module's version and
  layout. `Module_layouts_match_what_darktable_writes` catches it for the patched modules.
  For appended modules, re-check `Modules.cs` against the new release's `src/iop/<op>.c`
  `dt_iop_*_params_t` and `DT_MODULE_INTROSPECTION` version, then regenerate the fixture
  from a fresh `photoedit new`.
- **darktable-cli output paths** go through variable expansion, where `\` is an escape
  character. Always pass forward slashes. darktable never overwrites; it writes `name_01.jpg`
  instead, so delete the target first.
- **As-shot white balance** is read from the `-d params` log line
  `[commit color calibration] … xy=…`. The recipe holds Kelvin/tint, which is only
  converted to a custom illuminant when it differs from as-shot, so an untouched recipe
  reproduces the baseline.
- **Tint convention:** + is magenta, − is green, as an image effect. Rotation: + is
  counter-clockwise. ashift's crop box is computed by us (the GUI normally does it), in the
  sensor's unrotated frame.
- Runs share one config dir and are serialized with a lock file (`photoedit.lock`).

## When changing the recipe

`Recipe.cs`, `RecipeCompiler.cs`, `Recipe.Clamp()` and the reference section of
`editor/CLAUDE.md` must change together. The editor prompt is the only documentation the
editing Claude sees.
